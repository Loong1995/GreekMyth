from __future__ import annotations

"""战斗引擎：系列 → 局 → 回合 → 行动 状态机（任务书 5.1）。

B3 范围（在 B2 效果原语/状态系统之上）：
- 事件驱动战法架构：状态响应钩子（on_damage_dealt / on_damage_taken /
  on_action_start）+ 全局确定的响应优先级（response_priority → 持有者序 → 实例序）；
- 单挑（仅第 1 局开局，DUEL 相位，决策 D-03）；
- 追击（普攻命中后时机）+ 连击（combo_rate ≥100% 普攻两次，每击独立追击）；
- 犹豫（延迟行动登记/补结算/行动后计次，决策 D-02 人工修订版）；
- 准备型战法（prepare → release，被 forbid_active 控制打断 → interrupted）；
- 准备回合（r=0）施法 + 神谕连携（k=70%，决策 D-04）。
每局 = 1 准备回合(r=0) + 最多 8 正常回合；平局残血续战，最多 7 局。
遍历顺序规则见 docs/mechanics/determinism.md。
"""

from typing import Any

from battle import formulas, statuses as st, traits as tr
from battle.errors import BattleCoreError, SetupError
from battle.events import (
    PHASE_ACTION,
    PHASE_DUEL,
    PHASE_GAME_END,
    PHASE_GAME_START,
    PHASE_ROUND_END,
    PHASE_ROUND_START,
    PHASE_SERIES_END,
    PHASE_SERIES_START,
    EventWriter,
)
from battle.heroes import HeroState, build_hero_state, troops_delta, troops_snapshot
from battle.pseudo_random import PseudoRandomBook
from battle.rng import BPS, DeterministicRNG
from battle.setup import ATTR_NAMES, BattleSetup
from battle.skills import (
    REGISTRY as SKILL_REGISTRY,
    TIMING_ACTIVE,
    TIMING_PREPARE,
    TIMING_PURSUIT,
    Skill,
)
from battle.statuses import StatusDef, StatusInstance

MAX_GAMES = 7
ROUNDS_PER_GAME = 8
BASIC_ATTACK_RATE_BPS = 10000  # 普攻 = 系数 1.0 的兵刃伤害（任务书 5.2）

# 伤害类型（Phase 3 双公式，见 docs/mechanics/damage.md）：
#   physical 兵刃：核心 = 360 + 武力 - 统率
#   magic   谋略：核心 = 360 + 智力 - ½统率 - ½智力
#   true    真伤：核心 = 360 + 武力 - 100（固定防御基准）
DAMAGE_TYPES = ("physical", "magic", "true")
TRUE_DAMAGE_DEFENSE_BASE = 100

# 单挑规则（决策 D-03）
DUEL_FORCE_THRESHOLD = 90          # 双方均有武力 > 90 才触发
DUEL_REJECT_PER_DIFF_BPS = 800     # 拒绝率 = 武力差 × 8%
DUEL_REJECT_MAX_BPS = 8000         # 封顶 80%（差 10 以上保留 20% 接受）
DUEL_WIN_BASE_BPS = 5000           # 胜率 = 50% + 差 × 5%
DUEL_WIN_PER_DIFF_BPS = 500
DUEL_PENALTY = 10                  # 负者四维 -10（scope=game，仅第 1 局）

# 连携（Phase 3 改版）：释放率 = 副将自带战法自身 trigger_rate_bps（普通随机，
# 不走伪随机补偿、不影响该战法伪随机记账）；形式上等于该副将获得一次在准备
# 阶段正常释放自带战法的机会（准备型免准备）。见 docs/mechanics/assist.md。


class SeriesEngine:
    def __init__(self, setup: BattleSetup, seed: int, *, audit: bool = False) -> None:
        self.setup = setup
        self.rng = DeterministicRNG(seed, audit=audit)
        self.writer = EventWriter(setup.battle_id)
        # 调试侧信道：技能触发判定/跳过明细（不进战报 JSON，textlog all 档打印）
        self.debug_rolls: list[dict[str, Any]] = []
        # 全局确定性遍历序：队伍按 team_id 字典序，队内按站位序
        self.teams = sorted(setup.teams, key=lambda team: team.team_id)
        self.heroes: dict[str, HeroState] = {}
        self.hero_order: list[str] = []
        for team in self.teams:
            for hero in sorted(team.heroes, key=lambda h: h.position):
                for skill_id in hero.skills:
                    if skill_id not in SKILL_REGISTRY:
                        raise SetupError("未注册的战法", hero_id=hero.hero_id, skill_id=skill_id)
                if hero.trait_id and hero.trait_id not in tr.REGISTRY:
                    raise SetupError("未注册的性格", hero_id=hero.hero_id, trait_id=hero.trait_id)
                state = build_hero_state(hero, team)
                self.heroes[hero.hero_id] = state
                self.hero_order.append(hero.hero_id)
        self._hero_rank = {hero_id: i for i, hero_id in enumerate(self.hero_order)}
        self.game_results: list[dict[str, Any]] = []

        # ---- 战时（单局）记账：每局开始整体重置 ----
        self.statuses: dict[str, list[StatusInstance]] = {}
        self._status_instance_counter = 0
        self.pseudo_random = PseudoRandomBook()
        self._game_attr_reverts: list[tuple[str, str, int]] = []  # (hero_id, attr, delta)
        self._game_winner: str | None = None
        # 准备型战法登记：hero_id -> {"skill_id", "remaining"}（一人同时至多一个准备中）
        self._preparing: dict[str, dict[str, Any]] = {}
        # 犹豫延迟行动登记：hero_id -> [{"kind": "skill"/"basic", "skill_id", "remaining"}]
        self._delayed_actions: dict[str, list[dict[str, Any]]] = {}
        # 选人记录暂存：select_enemy_by_hit_rate 产生，随最近的宣告/结算事件带出
        # （payload 可选字段 target_select，契约加法式演进；见 mechanics/targeting.md）
        self._pending_target_selects: list[dict[str, Any]] = []
        # 受击率选人记录暂存：select_enemy_by_hit_rate 产生，由下一个承载事件
        # （normal_attack / skill_trigger / damage）作为 target_select 可选字段带出
        self._pending_target_selects: list[dict[str, Any]] = []
        # 性格系统（Phase 3）：回合级旗标（每回合开始清空）与额外行动队列
        self._trait_flags: dict[str, set[str]] = {}
        self._extra_action_queue: list[str] = []
        # Phase 3 记账：当前回合号 / 本回合已行动者 / 本局阵亡数 / 战法施放计数 /
        # 最近一次伤害结算结果（skills 读取暴击结果用，如怒火突刺）
        self.current_round = 0
        self._acted_this_round: set[str] = set()
        self.defeat_count = 0
        self._skill_cast_counts: dict[tuple[str, str], int] = {}
        self.last_damage_result: dict[str, Any] = {}

    # ------------------------------------------------------------------ 查询

    def hero_by_id(self, hero_id: str) -> HeroState:
        return self.heroes[hero_id]

    def hero_statuses(self, hero_id: str) -> list[StatusInstance]:
        return self.statuses.setdefault(hero_id, [])

    def effective_attr(self, hero: HeroState, attr: str) -> int:
        """有效属性 = max(0, (基础 + 性格平加 + 状态平加) × (1 + 状态百分比))。
        性格加成（Phase 3）与状态平加同层。"""
        owned = self.hero_statuses(hero.hero_id)
        trait_bonus = 0
        trait = tr.of(hero)
        if trait is not None:
            trait_bonus = trait.attr_bonus(self, hero, attr)
        after_flat = getattr(hero, attr) + trait_bonus + st.sum_modifier(owned, f"{attr}_delta")
        percent_bps = st.sum_modifier(owned, f"{attr}_bps")
        return max(0, after_flat * (BPS + percent_bps) // BPS)

    # ------------------------------------------------------------------ 性格（Phase 3）

    def set_trait_flag(self, hero_id: str, key: str) -> None:
        self._trait_flags.setdefault(hero_id, set()).add(key)

    def trait_flag(self, hero_id: str, key: str) -> bool:
        return key in self._trait_flags.get(hero_id, set())

    def trait_flags(self, hero_id: str) -> set[str]:
        return self._trait_flags.get(hero_id, set())

    def total_crit_rate(self, hero: HeroState, damage_type: str = "physical") -> int:
        """武将当前总暴击率（面板+状态，clamp 0~100%）。觅踵/致命一矢判定用。"""
        return min(
            max(
                0,
                hero.crit_rate_bps
                + self.modifier(hero, "crit_rate_bps")
                + self.modifier(hero, f"{damage_type}_crit_rate_bps"),
            ),
            formulas.CRIT_RATE_MAX_BPS,
        )

    def dispel(self, target: HeroState, *, count: int | None, parent_seq: int) -> int:
        """驱散目标负面状态（debuff/control，instance_id 升序即施加序）。
        count=None 驱散全部；返回实际驱散数量。"""
        removed = 0
        for status in list(self.hero_statuses(target.hero_id)):
            if not status.definition.is_negative():
                continue
            self.remove_status(status, reason="dispelled", parent_seq=parent_seq)
            removed += 1
            if count is not None and removed >= count:
                break
        return removed

    def drain_multiplier(self, hero: HeroState, parent_seq: int) -> int:
        """吸取属性效果倍率（哈迪斯威权 20% 翻倍等）。"""
        trait = tr.of(hero)
        if trait is None:
            return 1
        return trait.attr_drain_multiplier(self, hero, parent_seq)

    def notify_petrify_out(self, source: HeroState, parent_seq: int) -> None:
        """石化施加成功后回调来源性格（美杜莎孤怨照影）。"""
        trait = tr.of(source)
        if trait is not None:
            trait.on_petrify_out(self, source, parent_seq)

    def modifier(self, hero: HeroState, key: str) -> int:
        return st.sum_modifier(self.hero_statuses(hero.hero_id), key)

    def is_forbidden(self, hero: HeroState, key: str) -> bool:
        return st.any_forbid(self.hero_statuses(hero.hero_id), key)

    def find_status(self, hero_id: str, status_id: str) -> StatusInstance | None:
        for status in self.hero_statuses(hero_id):
            if status.status_id == status_id:
                return status
        return None

    def game_over(self) -> bool:
        """本局是否已分胜负（响应钩子/连锁应据此提前退出）。"""
        return self._game_winner is not None

    def _alive_enemies(self, hero: HeroState) -> list[HeroState]:
        return [
            self.heroes[hero_id]
            for hero_id in self.hero_order
            if self.heroes[hero_id].team_id != hero.team_id
            and self.heroes[hero_id].is_alive()
        ]

    def _alive_allies(self, hero: HeroState) -> list[HeroState]:
        return [
            self.heroes[hero_id]
            for hero_id in self.hero_order
            if self.heroes[hero_id].team_id == hero.team_id
            and self.heroes[hero_id].is_alive()
        ]

    def alive_enemies(self, hero: HeroState) -> list[HeroState]:
        return self._alive_enemies(hero)

    def alive_allies(self, hero: HeroState) -> list[HeroState]:
        return self._alive_allies(hero)

    def select_enemy_by_hit_rate(self, attacker: HeroState, *, reason: str,
                                 exclude_ids: tuple[str, ...] = ()) -> HeroState | None:
        """按受击点数加权随机选敌（候选序 = hero_order 内的 (站位, hero_id) 序）。

        exclude_ids 供多段/连锁类战法排除已选目标。无候选时返回 None
        （B3 起连锁场景允许无目标，调用方自行跳过）。
        魅惑（charm_targeting）：候选改为敌我全体（除自己），敌我不分（Phase 3）。
        性格干预（Phase 3）：force_basic_target（怒涛/好战/逐苹）直接指定目标；
        prefer_target（狡黠后排/鲁莽嘲讽）缩小候选集后再按受击率 roll。
        """
        trait = tr.of(attacker)
        if trait is not None:
            forced = trait.force_basic_target(self, attacker, reason)
            if forced is not None and forced.is_alive() and forced.hero_id not in exclude_ids:
                self._pending_target_selects.append({
                    "reason": reason,
                    "candidates": [{"hero_id": forced.hero_id, "hit_bps": BPS}],
                    "selected_id": forced.hero_id,
                })
                return forced
        # 锁定最低兵力目标（月影狩猎，Phase 3）：跳过受击率 roll，确定性选取
        if st.any_forbid(self.hero_statuses(attacker.hero_id), "lock_lowest_target"):
            enemies = [h for h in self._alive_enemies(attacker) if h.hero_id not in exclude_ids]
            if enemies:
                selected = self._lowest_troops_ratio(enemies)
                self._pending_target_selects.append({
                    "reason": reason,
                    "candidates": [{"hero_id": selected.hero_id, "hit_bps": BPS}],
                    "selected_id": selected.hero_id,
                })
                return selected
            return None
        if st.any_forbid(self.hero_statuses(attacker.hero_id), "charm_targeting"):
            pool = [
                self.heroes[hero_id]
                for hero_id in self.hero_order
                if hero_id != attacker.hero_id and self.heroes[hero_id].is_alive()
            ]
        else:
            pool = self._alive_enemies(attacker)
        candidates = [h for h in pool if h.hero_id not in exclude_ids]
        if not candidates:
            return None
        if trait is not None and len(candidates) > 1:
            preferred = trait.prefer_target(self, attacker, candidates, reason)
            if preferred:
                candidates = preferred
        if len(candidates) == 1:
            selected = candidates[0]
            self._pending_target_selects.append({
                "reason": reason,
                "candidates": [{"hero_id": selected.hero_id,
                                "hit_bps": selected.hit_points_bps()}],
                "selected_id": selected.hero_id,
            })
            return selected
        weights = [hero.hit_points_bps() for hero in candidates]
        index = self.rng.rand_weighted_index(weights, "target_select", reason)
        selected = candidates[index]
        # 选人过程事件化（动态受击点数 + 命中者），由最近的宣告/结算事件带出
        self._pending_target_selects.append({
            "reason": reason,
            "candidates": [
                {"hero_id": hero.hero_id, "hit_bps": weight}
                for hero, weight in zip(candidates, weights)
            ],
            "selected_id": selected.hero_id,
        })
        return selected

    def _drain_target_selects(self) -> list[dict[str, Any]]:
        records = self._pending_target_selects
        self._pending_target_selects = []
        return records

    @staticmethod
    def _lowest_troops_ratio(pool: list[HeroState]) -> HeroState:
        best = pool[0]
        best_ratio = best.troops * BPS // best.max_troops
        for hero in pool[1:]:
            ratio = hero.troops * BPS // hero.max_troops
            if ratio < best_ratio:
                best, best_ratio = hero, ratio
        return best

    def select_ally_lowest_troops(self, actor: HeroState) -> HeroState:
        """己方兵力比例（troops/max，bps）最低者；并列取遍历序靠前者。"""
        return self._lowest_troops_ratio(self._alive_allies(actor))

    def select_heal_target_lowest(self, actor: HeroState) -> HeroState:
        """治疗选目标（兵力比例最低友军）；性格·仁心 20% 翻面：改治疗**对面**
        兵力比例最低者（Phase 3，翻面前播台词）。敌方全灭时回落己方。"""
        trait = tr.of(actor)
        if trait is not None and trait.flip_heal_lowest(self, actor, 0):
            enemies = self._alive_enemies(actor)
            if enemies:
                return self._lowest_troops_ratio(enemies)
        return self.select_ally_lowest_troops(actor)

    # ------------------------------------------------------------------ 主流程

    def run(self) -> dict[str, Any]:
        winner_team_id: str | None = None
        game_no = 0

        for game_no in range(1, MAX_GAMES + 1):
            self.writer.begin_game()
            if game_no == 1:
                self.writer.set_time(1, 0, PHASE_SERIES_START, 0)
                self.writer.emit("battle_start", {"total_max_games": MAX_GAMES})
            winner_team_id, reason, end_round = self._run_game(game_no)
            self.game_results.append(
                {
                    "game_no": game_no,
                    "winner_team_id": winner_team_id,
                    "reason": reason,
                    "end_round": end_round,
                    "troops": [
                        troops_delta(self.heroes[hero_id], troops_snapshot(self.heroes[hero_id]))
                        for hero_id in self.hero_order
                    ],
                }
            )
            if winner_team_id is not None:
                break
            # 平局续战：残血带入下一局；战时状态已在 _end_game 清空

        series_reason = "main_hero_defeated" if winner_team_id is not None else "series_limit"
        last = self.game_results[-1]
        self.writer.set_time(last["game_no"], last["end_round"], PHASE_SERIES_END, 0)
        self.writer.emit(
            "battle_end",
            {
                "winner_team_id": winner_team_id,
                "total_games": len(self.game_results),
                "reason": series_reason,
            },
        )
        return {
            "winner_team_id": winner_team_id,
            "reason": series_reason,
            "total_games": len(self.game_results),
        }

    # ------------------------------------------------------------------ 单局

    def _reset_game_state(self) -> None:
        """局边界：战时状态/伪随机记账/本局属性修改/延迟行动/准备中战法全部清空回滚
        （任务书 5.1；犹豫延迟中的行动随局清空 = 决策 D-02 边界 4）。"""
        for hero_id, attr, delta in reversed(self._game_attr_reverts):
            hero = self.heroes[hero_id]
            setattr(hero, attr, getattr(hero, attr) - delta)
        self._game_attr_reverts = []
        self.statuses = {}
        self._status_instance_counter = 0
        self.pseudo_random = PseudoRandomBook()
        self._game_winner = None
        self._preparing = {}
        self._delayed_actions = {}
        self._pending_target_selects = []
        self._trait_flags = {}
        self._extra_action_queue = []
        self.current_round = 0
        self._acted_this_round = set()
        self.defeat_count = 0
        self._skill_cast_counts = {}
        self.last_damage_result = {}

    def _run_game(self, game_no: int) -> tuple[str | None, str, int]:
        self._reset_game_state()
        self.writer.set_time(game_no, 0, PHASE_GAME_START, 0)
        self.writer.emit(
            "game_start",
            {
                "game_no": game_no,
                "troops": [
                    troops_delta(self.heroes[hero_id], troops_snapshot(self.heroes[hero_id]))
                    for hero_id in self.hero_order
                ],
            },
        )
        # 单挑：仅第 1 局开局、所有战法执行前（决策 D-03）
        if game_no == 1:
            self._run_duel(game_no)

        end_round = 0

        # 准备回合 r=0：神谕 / 被动入场战法的时窗 + 神谕连携
        self.writer.set_time(game_no, 0, PHASE_ROUND_START, 0)
        self.writer.emit("round_start", {"round_no": 0})
        self._run_prepare_round(game_no)
        self.writer.set_time(game_no, 0, PHASE_ROUND_END, 0)
        self.writer.emit("round_end", {"round_no": 0})

        if self._game_winner is None:
            for round_no in range(1, ROUNDS_PER_GAME + 1):
                end_round = round_no
                self._run_round(game_no, round_no)
                if self._game_winner is not None:
                    break

        winner = self._game_winner
        reason = "main_hero_defeated" if winner is not None else "round_limit"

        self.writer.set_time(game_no, end_round, PHASE_GAME_END, 0)
        self.writer.emit(
            "game_end",
            {
                "game_no": game_no,
                "winner_team_id": winner,
                "reason": reason,
                "end_round": end_round,
                "troops": [
                    troops_delta(self.heroes[hero_id], troops_snapshot(self.heroes[hero_id]))
                    for hero_id in self.hero_order
                ],
            },
        )
        # game_end 语义清空战时状态与本局属性修改（不逐条发事件，契约 §19）
        self._reset_game_state()
        return winner, reason, end_round

    # ------------------------------------------------------------------ 单挑（B3）

    def _duel_champion(self, team_id: str) -> HeroState | None:
        """队内武力 > 90 的最高武力者；并列取站位靠前（决策 D-08）。无则 None。"""
        best: HeroState | None = None
        for hero_id in self.hero_order:
            hero = self.heroes[hero_id]
            if hero.team_id != team_id or not hero.is_alive():
                continue
            force = self.effective_attr(hero, "force")
            if force <= DUEL_FORCE_THRESHOLD:
                continue
            if best is None or force > self.effective_attr(best, "force"):
                best = hero
        return best

    def _run_duel(self, game_no: int) -> None:
        champion_a = self._duel_champion(self.teams[0].team_id)
        champion_b = self._duel_champion(self.teams[1].team_id)
        if champion_a is None or champion_b is None:
            return
        force_a = self.effective_attr(champion_a, "force")
        force_b = self.effective_attr(champion_b, "force")
        # 高武力方叫阵；同武力按队伍序破平（A 先，决策 D-08）
        if force_a >= force_b:
            challenger, defender = champion_a, champion_b
            diff = force_a - force_b
        else:
            challenger, defender = champion_b, champion_a
            diff = force_b - force_a

        self.writer.set_time(game_no, 0, PHASE_DUEL, 0)
        challenge_seq = self.writer.emit(
            "duel_challenge",
            {
                "challenger_id": challenger.hero_id,
                "defender_id": defender.hero_id,
                "challenger_force": self.effective_attr(challenger, "force"),
                "defender_force": self.effective_attr(defender, "force"),
            },
        )

        reject_rate = min(diff * DUEL_REJECT_PER_DIFF_BPS, DUEL_REJECT_MAX_BPS)
        rejected = False
        if reject_rate > 0:
            rejected = self.rng.rand_bps("duel_reject", defender.hero_id) < reject_rate
        if rejected:
            self.writer.emit(
                "duel_result", {"accepted": False}, parent_seq=challenge_seq
            )
            return

        win_rate = DUEL_WIN_BASE_BPS + diff * DUEL_WIN_PER_DIFF_BPS
        if win_rate >= BPS:
            challenger_wins = True
        else:
            challenger_wins = self.rng.rand_bps("duel_win", challenger.hero_id) < win_rate
        winner, loser = (challenger, defender) if challenger_wins else (defender, challenger)
        result_seq = self.writer.emit(
            "duel_result",
            {"accepted": True, "winner_id": winner.hero_id, "loser_id": loser.hero_id},
            parent_seq=challenge_seq,
        )
        # 负者四维立即 -10（scope=game：惩罚只存在第 1 局，随局末回滚）
        self.modify_attr(
            loser,
            [(attr, -DUEL_PENALTY) for attr in ATTR_NAMES],
            scope="game",
            parent_seq=result_seq,
        )

    # ------------------------------------------------------------------ 准备回合（B3）

    def _run_prepare_round(self, game_no: int) -> None:
        """r=0：按行动顺序执行各武将 timing=prepare 的战法（神谕/被动入场）；
        主将神谕释放后触发副将连携（决策 D-04）。无准备施法者时不消耗 RNG。"""
        casters = [
            hero_id
            for hero_id in self.hero_order
            if self.heroes[hero_id].is_alive()
            and any(
                SKILL_REGISTRY[s].timing == TIMING_PREPARE for s in self.heroes[hero_id].skills
            )
        ]
        if not casters:
            return
        order = [h for h in self._build_action_order(0) if h in casters]
        slot = 0
        for hero_id in order:
            hero = self.heroes[hero_id]
            if not hero.is_alive():
                continue
            self.writer.set_time(game_no, 0, PHASE_ACTION, slot)
            slot += 1
            for skill_id in hero.skills:
                skill = SKILL_REGISTRY[skill_id]
                if skill.timing != TIMING_PREPARE:
                    continue
                if not self._roll_skill_trigger(hero, skill):
                    continue
                targets = skill.select_targets(self, hero)
                trigger_seq = self._emit_skill_trigger(hero, skill, "cast", targets)
                skill.execute(self, hero, targets, trigger_seq)
                if self._game_winner is not None:
                    return
                if skill.is_oracle and hero.is_main:
                    self._run_assist(hero, trigger_seq)
                    if self._game_winner is not None:
                        return

    def _run_assist(self, main_hero: HeroState, oracle_seq: int) -> None:
        """连携（Phase 3 改版）：主将神谕后，两副将自带战法（装配位 0）若为主动，
        各按**该战法自身释放率**（trigger_rate_bps）roll 一次是否立即释放
        （kind=assist，不占用其当回合正常释放机会）。普通随机、不走伪随机补偿、
        不影响该战法伪随机记账。准备型主动无需准备直接释放。必发战法（≥100%）
        不消耗 RNG。"""
        for ally in self._alive_allies(main_hero):
            if ally.is_main or not ally.skills:
                continue
            skill = SKILL_REGISTRY[ally.skills[0]]
            if skill.timing != TIMING_ACTIVE:
                continue
            if skill.trigger_rate_bps < BPS:
                roll = self.rng.rand_bps("assist", ally.hero_id)
                self._log_debug_roll(
                    ally, skill.skill_id, "assist",
                    base=skill.trigger_rate_bps, current=skill.trigger_rate_bps,
                    roll=roll, allowed=roll < skill.trigger_rate_bps, guaranteed=False,
                )
                if roll >= skill.trigger_rate_bps:
                    continue
            targets = skill.select_targets(self, ally)
            trigger_seq = self._emit_skill_trigger(
                ally, skill, "assist", targets, parent_seq=oracle_seq, new_group=True
            )
            skill.execute(self, ally, targets, trigger_seq)
            if self._game_winner is not None:
                return

    # ------------------------------------------------------------------ 单回合

    def _run_round(self, game_no: int, round_no: int) -> None:
        self.current_round = round_no
        self._acted_this_round = set()
        self.writer.set_time(game_no, round_no, PHASE_ROUND_START, 0)
        round_start_seq = self.writer.emit("round_start", {"round_no": round_no})

        # 回合计数器清零（雷霆/追加伤害等「每回合最多 N 次」记账）
        for hero_id in self.hero_order:
            for status in self.hero_statuses(hero_id):
                status.round_counters.clear()

        # 性格回合级旗标清空 + on_round_start 触发（分神/鲁莽/怒涛/畏战…hero_order 序）
        self._trait_flags = {}
        for hero_id in self.hero_order:
            hero = self.heroes[hero_id]
            if not hero.is_alive():
                continue
            trait = tr.of(hero)
            if trait is not None:
                trait.on_round_start(self, hero, round_start_seq)
        if self._game_winner is not None:
            return

        # 伤兵自然损耗：在场武将伤兵池 30% 转阵亡（troops 不变）
        for hero_id in self.hero_order:
            hero = self.heroes[hero_id]
            if not hero.is_alive() or hero.wounded_troop <= 0:
                continue
            converted = formulas.wounded_decay(hero.wounded_troop)
            if converted <= 0:
                continue
            before = troops_snapshot(hero)
            hero.wounded_troop -= converted
            hero.dead_troop += converted
            self.writer.emit(
                "troops_change",
                {"reason": "wounded_decay", "troops": troops_delta(hero, before)},
                parent_seq=round_start_seq,
            )

        # DoT/HoT 周期结算（round_start 组下，可致死）
        self._tick_periodic_statuses(round_start_seq)
        if self._game_winner is not None:
            return

        # on_round_start 状态钩子（木马奇谋计时/浪涌格挡/疾走增伤…全局定序）
        self._dispatch_round_hooks("on_round_start", round_start_seq, round_no=round_no)
        if self._game_winner is not None:
            return

        action_order = self._build_action_order(round_no)
        # 性格·顺延（畏战/算计过深）：postpone 旗标者移到回合末（相对序不变）
        postponed = [h for h in action_order if self.trait_flag(h, "postpone")]
        if postponed:
            action_order = [h for h in action_order if h not in postponed] + postponed
        slot = 0
        for hero_id in action_order:
            hero = self.heroes[hero_id]
            if not hero.is_alive():
                continue  # 本回合内先阵亡则跳过
            self.writer.set_time(game_no, round_no, PHASE_ACTION, slot)
            self._run_action_window(hero, slot)
            slot += 1
            if self._game_winner is not None:
                return
            # 性格·好战额外行动：阵亡触发排队，当前窗口结束后立即执行
            while self._extra_action_queue:
                extra_id = self._extra_action_queue.pop(0)
                extra = self.heroes[extra_id]
                if not extra.is_alive():
                    continue
                self.writer.set_time(game_no, round_no, PHASE_ACTION, slot)
                self._run_action_window(extra, slot)
                slot += 1
                if self._game_winner is not None:
                    return

        self.writer.set_time(game_no, round_no, PHASE_ROUND_END, 0)
        round_end_seq = self.writer.emit("round_end", {"round_no": round_no})

        # on_round_end 状态钩子（冬春轮转/蛇杖收尾治疗…）
        self._dispatch_round_hooks("on_round_end", round_end_seq, round_no=round_no)

    def _tick_periodic_statuses(self, round_start_seq: int) -> None:
        """DoT/HoT tick：hero_order 序 × 状态施加序（instance_id 升序）。"""
        for hero_id in self.hero_order:
            hero = self.heroes[hero_id]
            if not hero.is_alive():
                continue
            for status in list(self.hero_statuses(hero_id)):
                definition = status.definition
                if definition.dot_rate_bps <= 0 and definition.hot_rate_bps <= 0:
                    continue
                if not hero.is_alive():
                    break  # 前一个 DoT 已致死
                source = self.heroes[status.source_id]
                tick_seq = self.writer.emit(
                    "status_tick",
                    {"status": status.ref(), "source_id": status.source_id},
                    parent_seq=round_start_seq,
                )
                if definition.dot_rate_bps > 0:
                    self.deal_damage(
                        source, hero, damage_type="magic",
                        rate_bps=definition.dot_rate_bps, parent_seq=tick_seq,
                        can_crit=False, kind="dot", can_mitigate=False,
                    )
                if definition.hot_rate_bps > 0:
                    self.heal(
                        source, hero, rate_bps=definition.hot_rate_bps,
                        parent_seq=tick_seq, can_crit=False,
                    )
                if self._game_winner is not None:
                    return

    def _dispatch_round_hooks(self, hook_name: str, parent_seq: int, *, round_no: int) -> None:
        """回合级状态钩子分发：(response_priority, 持有者 hero_order 序, instance_id) 全局定序。
        handler(engine, status, parent_seq, round_no)。"""
        entries: list[tuple[int, int, int, StatusInstance]] = []
        for hero_id in self.hero_order:
            if not self.heroes[hero_id].is_alive():
                continue
            for status in self.hero_statuses(hero_id):
                if getattr(status.definition, hook_name) is not None:
                    entries.append((
                        status.definition.response_priority,
                        self._hero_rank[hero_id],
                        status.instance_id, status,
                    ))
        entries.sort(key=lambda e: (e[0], e[1], e[2]))
        for _, _, _, status in entries:
            if self._game_winner is not None:
                return
            owner = self.heroes[status.owner_id]
            if not owner.is_alive() or status not in self.hero_statuses(status.owner_id):
                continue
            getattr(status.definition, hook_name)(self, status, parent_seq, round_no)

    # ------------------------------------------------------------------ 行动窗口

    def _run_action_window(self, hero: HeroState, slot: int) -> None:
        """一个武将的完整行动窗口，Phase 3 序（docs/mechanics/index.md）：
        状态计次到期（含犹豫，统一前移）→ on_action_start 钩子 → 延迟行动补结算
        → 犹豫延迟判定 → 准备释放（免犹豫判定）→ 主动战法 → 普攻（含连击/追击）。"""
        expired = self._tick_action_durations(hero)
        forbid_basic = self.is_forbidden(hero, "forbid_basic")
        forbid_active = self.is_forbidden(hero, "forbid_active")
        has_active = any(
            SKILL_REGISTRY[s].timing == TIMING_ACTIVE for s in hero.skills
        )

        preparing = self._preparing.get(hero.hero_id)
        release_due = preparing is not None and preparing["remaining"] <= 1
        delayed_due = any(
            entry["remaining"] <= 1 for entry in self._delayed_actions.get(hero.hero_id, [])
        )
        skipped = (
            forbid_basic
            and (forbid_active or not has_active)
            and not (release_due and not forbid_active)
            and not delayed_due
        )

        payload: dict[str, Any] = {"actor_id": hero.hero_id, "order_no": slot + 1}
        if skipped:
            payload["skipped"] = True
        action_seq = self.writer.emit("action_start", payload)
        self._acted_this_round.add(hero.hero_id)

        for status in expired:
            self.writer.emit(
                "status_remove",
                {"status": status.ref(), "reason": "expired"},
                parent_seq=action_seq,
            )

        # on_action_start 响应钩子（幽影蔽体刷新/冥祭献统/扰心标记…可施加犹豫，
        # 影响本窗口的延迟判定）
        self._dispatch_action_start(hero, action_seq)
        if self._game_winner is not None or not hero.is_alive():
            return

        # 犹豫延迟行动补结算：不受犹豫本回合到期影响（Phase 3 §二——寄存行动
        # 照常释放，仅新行动不再进入犹豫判定）
        self._settle_delayed_actions(hero)
        if self._game_winner is not None or not hero.is_alive():
            return

        # 犹豫延迟判定（Phase 3：与准备释放互换顺序，先判定）：对本窗口
        # 「普攻 + 主动」整体 roll 一次；延后固定 1 回合（N → N+1 窗口最前释放）。
        # 犹豫计次已统一前移到 action_start（_tick_action_durations），本回合开始
        # 到期即移除，则此处 find 不到 → 不判定。全禁（无事可延）不 roll。
        forbid_basic = self.is_forbidden(hero, "forbid_basic")
        forbid_active = self.is_forbidden(hero, "forbid_active")
        delay_rounds = 0
        hesitation = self.find_status(hero.hero_id, "hesitation")
        trait = tr.of(hero)
        hesitation_immune = trait is not None and trait.hesitation_immune(self, hero)
        if (
            hesitation is not None
            and not hesitation_immune
            and not (forbid_basic and (forbid_active or not has_active))
        ):
            rate = hesitation.definition.payload.get("delay_rate_bps", 5000)
            if self.rng.rand_bps("hesitation", hero.hero_id) < rate:
                delay_rounds = 1

        # 准备型战法释放（在犹豫判定之后，Phase 3 §二互换；release 免犹豫判定 D-15）
        self._settle_preparing(hero)
        if self._game_winner is not None or not hero.is_alive():
            return

        # 主动战法：装配顺序逐个触发判定（伪随机补偿，key 一局内真累计）。
        # 准备中（concentrating）不再发起新主动（决策 D-16）。
        if forbid_active and any(
            SKILL_REGISTRY[s].timing == TIMING_ACTIVE for s in hero.skills
        ):
            self._log_debug_roll(hero, "*", "skip", reason="禁主动（缴械/噤声等）")
        elif hero.hero_id in self._preparing:
            self._log_debug_roll(
                hero, self._preparing[hero.hero_id]["skill_id"], "skip", reason="准备中"
            )
        if not forbid_active and hero.hero_id not in self._preparing:
            for slot_idx, skill_id in enumerate(hero.skills):
                skill = SKILL_REGISTRY[skill_id]
                if skill.timing != TIMING_ACTIVE:
                    continue
                # 性格·号角走音：本回合无法释放自带战法（装配位 0）
                if slot_idx == 0 and self.trait_flag(hero.hero_id, "own_skill_disabled"):
                    self._log_debug_roll(hero, skill_id, "skip", reason="号角走音禁自带战法")
                    continue
                if not self._roll_skill_trigger(hero, skill):
                    continue
                if delay_rounds > 0:
                    self._emit_skill_trigger(
                        hero, skill, "delayed", [], delay_rounds=delay_rounds
                    )
                    self._delayed_actions.setdefault(hero.hero_id, []).append(
                        {"kind": "skill", "skill_id": skill_id, "remaining": delay_rounds}
                    )
                    continue
                if skill.prepare_rounds > 0:
                    self._emit_skill_trigger(hero, skill, "prepare", [])
                    self._preparing[hero.hero_id] = {
                        "skill_id": skill_id,
                        "remaining": skill.prepare_rounds,
                    }
                    break  # 进入准备后本窗口不再判定后续主动
                targets = skill.select_targets(self, hero)
                trigger_seq = self._emit_skill_trigger(hero, skill, "cast", targets)
                skill.execute(self, hero, targets, trigger_seq)
                if self._game_winner is not None or not hero.is_alive():
                    return

        # 普攻（含连击与追击）
        if not self.is_forbidden(hero, "forbid_basic"):
            if delay_rounds > 0:
                self._emit_skill_trigger(
                    hero, _BASIC_ATTACK_STUB, "delayed", [], delay_rounds=delay_rounds
                )
                self._delayed_actions.setdefault(hero.hero_id, []).append(
                    {"kind": "basic", "skill_id": "basic_attack", "remaining": delay_rounds}
                )
            else:
                self._perform_basic_attack(hero)
                if self._game_winner is not None:
                    return

    def _settle_delayed_actions(self, hero: HeroState) -> None:
        """补结算到期延迟行动（D-02 第 3 条）。生效时点被禁的部分作废（静默）；
        目标在结算时点重新选择（原目标阵亡自然换新）。"""
        entries = self._delayed_actions.get(hero.hero_id)
        if not entries:
            return
        due: list[dict[str, Any]] = []
        remaining: list[dict[str, Any]] = []
        for entry in entries:
            entry["remaining"] -= 1
            (due if entry["remaining"] <= 0 else remaining).append(entry)
        self._delayed_actions[hero.hero_id] = remaining
        for entry in due:
            if self._game_winner is not None or not hero.is_alive():
                return
            if entry["kind"] == "basic":
                if self.is_forbidden(hero, "forbid_basic"):
                    continue  # 冥锁/石化/缴械 → 普攻部分作废
                self._perform_basic_attack(hero)
                continue
            if self.is_forbidden(hero, "forbid_active"):
                continue  # 缄默/冥锁/石化 → 主动部分作废
            skill = SKILL_REGISTRY[entry["skill_id"]]
            if skill.prepare_rounds > 0:
                # 延迟的准备型战法：生效 = 进入准备
                self._emit_skill_trigger(hero, skill, "prepare", [])
                self._preparing[hero.hero_id] = {
                    "skill_id": skill.skill_id,
                    "remaining": skill.prepare_rounds,
                }
                continue
            targets = skill.select_targets(self, hero)
            trigger_seq = self._emit_skill_trigger(hero, skill, "release", targets)
            skill.execute(self, hero, targets, trigger_seq)

    def _settle_preparing(self, hero: HeroState) -> None:
        """准备型战法计数与释放。被打断的登记已在 apply_status 中移除。"""
        preparing = self._preparing.get(hero.hero_id)
        if preparing is None:
            return
        preparing["remaining"] -= 1
        if preparing["remaining"] > 0:
            return
        del self._preparing[hero.hero_id]
        if self.is_forbidden(hero, "forbid_active"):
            return  # 防御性：正常路径下 forbid_active 施加时已打断
        skill = SKILL_REGISTRY[preparing["skill_id"]]
        targets = skill.select_targets(self, hero)
        trigger_seq = self._emit_skill_trigger(hero, skill, "release", targets)
        skill.execute(self, hero, targets, trigger_seq)

    def acted_before_all_enemies(self, hero: HeroState) -> bool:
        """本回合该武将是否先于所有敌军行动（疾风女猎，Phase 3）。"""
        return not any(
            other in self._acted_this_round
            for other in self.hero_order
            if self.heroes[other].team_id != hero.team_id
        )

    def skill_cast_count(self, hero: HeroState, skill_id: str) -> int:
        return self._skill_cast_counts.get((hero.hero_id, skill_id), 0)

    def note_skill_cast(self, hero: HeroState, skill_id: str) -> None:
        key = (hero.hero_id, skill_id)
        self._skill_cast_counts[key] = self._skill_cast_counts.get(key, 0) + 1

    def _roll_skill_trigger(self, hero: HeroState, skill: Skill) -> bool:
        rate = skill.trigger_rate_for(self, hero)  # 海嗣号角等动态衰减率（Phase 3）
        sink: list[dict[str, Any]] = []
        allowed = self.pseudo_random.roll(
            self.rng,
            (hero.hero_id, skill.skill_id),
            rate,
            skill.pseudo_random,
            source="skill_trigger",
            reason=f"{hero.hero_id}:{skill.skill_id}",
            debug_sink=sink,
        )
        self._log_debug_roll(hero, skill.skill_id, "trigger", **sink[0])
        return allowed

    def _log_debug_roll(self, hero: HeroState, skill_id: str, kind: str, **info) -> None:
        """记一条判定/跳过明细到调试侧信道（anchor_seq=最近事件，textlog 据此插位）。"""
        self.debug_rolls.append({
            "anchor_seq": self.writer.last_seq,
            "hero_id": hero.hero_id,
            "skill_id": skill_id,
            "kind": kind,  # trigger / assist / skip
            **info,
        })

    def _emit_skill_trigger(
        self,
        hero: HeroState,
        skill: Skill,
        kind: str,
        targets: list[HeroState],
        *,
        parent_seq: int = 0,
        new_group: bool = False,
        delay_rounds: int = 0,
        interrupted_by: dict[str, Any] | None = None,
    ) -> int:
        payload: dict[str, Any] = {
            "actor_id": hero.hero_id,
            "skill_id": skill.skill_id,
            "kind": kind,
            "target_ids": [target.hero_id for target in targets],
        }
        if delay_rounds:
            payload["delay_rounds"] = delay_rounds
        if interrupted_by is not None:
            payload["interrupted_by"] = interrupted_by
        selects = self._drain_target_selects()
        if selects:
            payload["target_select"] = selects  # select_targets 期间的受击率选人记录
        hint = {"intensity": skill.hint_intensity} if skill.hint_intensity else None
        return self.writer.emit(
            "skill_trigger", payload, parent_seq=parent_seq, new_group=new_group, hint=hint
        )

    def _tick_action_durations(self, hero: HeroState) -> list[StatusInstance]:
        """行动窗口计次：count+1，超过持续则摘除并返回（事件由调用方挂 action_start 下）。

        「持续 1 回合」= 状态在目标下一次行动窗口仍生效（计次 1 ≤ 1），
        再下一个窗口（计次 2 > 1）到期移除，与旧 core BEFORE_ACTION 语义一致。
        Phase 3：犹豫与其他状态统一在此计次（不再有窗口末特例）。
        """
        owned = self.hero_statuses(hero.hero_id)
        expired: list[StatusInstance] = []
        for status in list(owned):
            if status.definition.duration_rounds == st.PERMANENT:
                continue
            status.action_tick_count += 1
            if status.action_tick_count > status.definition.duration_rounds:
                owned.remove(status)
                expired.append(status)
        return expired

    # ------------------------------------------------------------------ 行动顺序

    def _team_queue(self, team_id: str) -> list[str]:
        """队内行动队列：存活武将按 (先攻降序, 有效速度降序, 站位, hero_id) 排序。
        先攻（first_strike，Phase 3 神使印记/凯歌）：持有者必定高于无先攻者。"""
        members = [
            hero_id
            for hero_id in self.hero_order
            if self.heroes[hero_id].team_id == team_id and self.heroes[hero_id].is_alive()
        ]
        return sorted(
            members,
            key=lambda hero_id: (
                0 if st.any_forbid(self.hero_statuses(hero_id), "first_strike") else 1,
                -self.effective_attr(self.heroes[hero_id], "speed"),
                self.heroes[hero_id].position,
                hero_id,
            ),
        )

    def _build_action_order(self, round_no: int) -> list[str]:
        """跨队合并：逐 slot 比较两队队首，按速度差概率 roll 先手（决策 D-09：普通随机）。"""
        queues = [self._team_queue(team.team_id) for team in self.teams]
        order: list[str] = []
        while queues[0] and queues[1]:
            hero_a = self.heroes[queues[0][0]]
            hero_b = self.heroes[queues[1][0]]
            # 先攻（Phase 3）：跨队比较时先攻持有者必定先手（不 roll）
            fs_a = st.any_forbid(self.hero_statuses(hero_a.hero_id), "first_strike")
            fs_b = st.any_forbid(self.hero_statuses(hero_b.hero_id), "first_strike")
            if fs_a != fs_b:
                order.append(queues[0].pop(0) if fs_a else queues[1].pop(0))
                continue
            prob_bps = formulas.calc_speed_first_probability_bps(
                self.effective_attr(hero_a, "speed") - self.effective_attr(hero_b, "speed")
            )
            if prob_bps >= 10000:
                a_first = True
            elif prob_bps <= 0:
                a_first = False
            else:
                roll = self.rng.rand_bps("action_order", f"r{round_no}:slot{len(order)}")
                a_first = roll < prob_bps
            order.append(queues[0].pop(0) if a_first else queues[1].pop(0))
        order.extend(queues[0] or queues[1])
        return order

    # ------------------------------------------------------------------ 普攻（连击 + 追击）

    def _perform_basic_attack(self, attacker: HeroState) -> None:
        """普攻：连击率 ≥100% 或 roll 中则打两次；每击独立选目标、独立触发追击。
        RNG 消费序：连击 roll →（每击）目标抽取 → 伤害内部 roll → 追击 roll。"""
        strikes = 1
        combo_rate = self.modifier(attacker, "combo_rate_bps")
        if combo_rate >= BPS:
            strikes = 2
        elif combo_rate > 0:
            if self.rng.rand_bps("combo", attacker.hero_id) < combo_rate:
                strikes = 2

        for strike_no in range(1, strikes + 1):
            if self._game_winner is not None or not attacker.is_alive():
                return
            if self.is_forbidden(attacker, "forbid_basic"):
                return  # 第一击的反伤/连锁可能施加控制
            target = self.select_enemy_by_hit_rate(
                attacker, reason=f"basic:{attacker.hero_id}:{strike_no}"
            )
            if target is None:
                return
            attack_payload: dict[str, Any] = {
                "actor_id": attacker.hero_id,
                "target_ids": [target.hero_id],
                "strike_no": strike_no,
            }
            selects = self._drain_target_selects()
            if selects:
                attack_payload["target_select"] = selects
            attack_seq = self.writer.emit("normal_attack", attack_payload)
            damage_seq = self.deal_damage(
                attacker, target, damage_type="physical",
                rate_bps=BASIC_ATTACK_RATE_BPS, parent_seq=attack_seq, kind="basic",
            )
            # 追击：普攻命中后时机；每击独立判定（决策清单遗留问题 §四）
            self._dispatch_pursuit(attacker, target, damage_seq)

    def _dispatch_pursuit(self, attacker: HeroState, target: HeroState, damage_seq: int) -> None:
        """普攻命中后分发追击战法（装配顺序）。目标已阵亡则无追击（不 roll，
        决策 D-17）；追击是新播放组（组根 parent 指回普攻 damage，契约 §3.2）。
        禁普攻即无追击（任务书 5.4：普攻反打中被石化/缴械/冥锁则追击不触发）。"""
        if self._game_winner is not None or not attacker.is_alive():
            return
        if self.is_forbidden(attacker, "forbid_pursuit") or self.is_forbidden(
            attacker, "forbid_basic"
        ):
            return
        for skill_id in attacker.skills:
            skill = SKILL_REGISTRY[skill_id]
            if skill.timing != TIMING_PURSUIT:
                continue
            if not target.is_alive() or not attacker.is_alive():
                return
            if not self._roll_skill_trigger(attacker, skill):
                continue
            trigger_seq = self._emit_skill_trigger(
                attacker, skill, "cast", [target], parent_seq=damage_seq, new_group=True
            )
            skill.execute(self, attacker, [target], trigger_seq)
            if self._game_winner is not None:
                return

    # ------------------------------------------------------------------ 响应钩子分发（B3）

    def _dispatch_action_start(self, hero: HeroState, action_seq: int) -> None:
        """行动窗口开始钩子：持有者自己的状态按 (priority, instance_id) 序响应。"""
        entries = sorted(
            (
                status
                for status in self.hero_statuses(hero.hero_id)
                if status.definition.on_action_start is not None
            ),
            key=lambda s: (s.definition.response_priority, s.instance_id),
        )
        for status in entries:
            if self._game_winner is not None or not hero.is_alive():
                return
            if status not in self.hero_statuses(hero.hero_id):
                continue  # 前一个响应移除了它
            status.definition.on_action_start(self, status, action_seq)

    def _dispatch_damage_hooks(self, ctx: dict[str, Any]) -> None:
        """伤害结算后钩子：来源方 on_damage_dealt + 受击方 on_damage_taken 合并，
        按 (response_priority, 持有者 hero_order 序, instance_id) 全局定序。
        受击方已阵亡则不再响应（5.5）。"""
        if self._game_winner is not None:
            return
        source: HeroState = ctx["source"]
        target: HeroState = ctx["target"]
        entries: list[tuple[int, int, int, StatusInstance, str]] = []
        if source.is_alive():
            for status in self.hero_statuses(source.hero_id):
                if status.definition.on_damage_dealt is not None:
                    entries.append((
                        status.definition.response_priority,
                        self._hero_rank[source.hero_id],
                        status.instance_id, status, "dealt",
                    ))
        if target.is_alive():
            for status in self.hero_statuses(target.hero_id):
                if status.definition.on_damage_taken is not None:
                    entries.append((
                        status.definition.response_priority,
                        self._hero_rank[target.hero_id],
                        status.instance_id, status, "taken",
                    ))
        entries.sort(key=lambda e: (e[0], e[1], e[2]))
        for _, _, _, status, side in entries:
            if self._game_winner is not None:
                return
            owner = self.heroes[status.owner_id]
            if not owner.is_alive() or status not in self.hero_statuses(status.owner_id):
                continue  # 响应链中途阵亡/被移除
            handler = (
                status.definition.on_damage_dealt
                if side == "dealt"
                else status.definition.on_damage_taken
            )
            handler(self, status, ctx)

    # ==================================================================
    # 效果原语（任务书 4.4）：战法/状态只通过这五个入口产生作用
    # ==================================================================

    def _calc_core_damage(
        self, source: HeroState, target: HeroState, damage_type: str, ignore_defense: bool
    ) -> int:
        """核心项（Phase 3 双公式，min=1 截断在 formulas 内完成）。"""
        if damage_type == "physical":
            force = self.effective_attr(source, "force")
            command = 0 if ignore_defense else self.effective_attr(target, "command")
            return formulas.calc_core_physical(force, command)
        if damage_type == "magic":
            intelligence = self.effective_attr(source, "intelligence")
            if ignore_defense:
                return formulas.calc_core_magic(intelligence, 0, 0)
            return formulas.calc_core_magic(
                intelligence,
                self.effective_attr(target, "command"),
                self.effective_attr(target, "intelligence"),
            )
        if damage_type == "true":
            force = self.effective_attr(source, "force")
            base = 0 if ignore_defense else TRUE_DAMAGE_DEFENSE_BASE
            return formulas.calc_core_physical(force, base)
        raise BattleCoreError("未知伤害类型", damage_type=damage_type)

    def _check_mitigation(
        self, source: HeroState, target: HeroState, kind: str
    ) -> tuple[str | None, StatusInstance | None]:
        """伤害落账前查询目标特殊状态（Phase 3 §二：格挡/闪避/反弹）。

        判定顺序（v3.2 定）：**按状态施加到英雄身上的顺序逐实例判定**
        （instance_id 升序 = 施加序；同一英雄同一时点由技能安装格子顺序执行，
        天然保证格子序即施加序）。单实例内能力序固定：次数型格挡（block_charges
        直接消耗）→ 闪避（evade_bps roll）→ 几率型格挡（block_rate_bps roll）
        → 反弹（reflect_rate_bps roll）。任一实例 roll 中即短路返回
        (mitigation 标签, 该实例)；标签 "evade"/"block"：伤害置 0 落账并事件化，
        "reflect"：伤害置 0 且本应受伤害由 deal_damage 反弹给攻击者。
        均不算受到实际伤害，不触发任何受击响应。实例带 mitigation_gate 时先查
        闸门（圣盾受匠心旁骛压制）。震荡等 special 伤害与 DoT 不参与判定
        （调用方控制）。
        """
        for status in list(self.hero_statuses(target.hero_id)):
            gate = status.definition.mitigation_gate
            if gate is not None and not gate(self, status):
                continue
            charges = status.counters.get("block_charges", 0)
            if charges > 0:
                status.counters["block_charges"] = charges - 1
                if status.counters["block_charges"] <= 0 and status.definition.payload.get(
                    "remove_when_exhausted", False
                ):
                    self.remove_status(status, reason="exhausted", parent_seq=0)
                return "block", status
            evade_rate = st.instance_modifier(status, "evade_bps")
            if evade_rate > 0:
                if self.rng.rand_bps("evade", target.hero_id) < min(evade_rate, BPS):
                    return "evade", status
            block_rate = st.instance_modifier(status, "block_rate_bps")
            if block_rate > 0:
                if self.rng.rand_bps("block", target.hero_id) < min(block_rate, BPS):
                    return "block", status
            reflect_rate = st.instance_modifier(status, "reflect_rate_bps")
            if reflect_rate > 0:
                if self.rng.rand_bps("reflect", target.hero_id) < min(reflect_rate, BPS):
                    return "reflect", status
        return None, None

    def grant_block(self, target: HeroState, charges: int, *, source: HeroState,
                    parent_seq: int) -> None:
        """赋予次数型格挡（复用格挡口径）：同 id 状态存在则叠计数，否则新建。
        性格·执拗（坚忍负触发）：本回合无法获得格挡 → 静默拒绝。"""
        if self.trait_flag(target.hero_id, "block_denied"):
            return
        existing = self.find_status(target.hero_id, "block")
        if existing is not None:
            existing.counters["block_charges"] = (
                existing.counters.get("block_charges", 0) + charges
            )
            self.writer.emit(
                "status_refresh",
                {
                    "status": existing.ref(),
                    "source_id": source.hero_id,
                    "stacks": existing.stacks,
                    "duration_rounds": existing.definition.duration_rounds,
                },
                parent_seq=parent_seq,
            )
            return
        instance = self.apply_status(source, target, st.block(), parent_seq=parent_seq)
        if instance is not None:
            instance.counters["block_charges"] = charges

    def deal_damage(
        self,
        source: HeroState,
        target: HeroState,
        *,
        damage_type: str,
        rate_bps: int = BPS,
        parent_seq: int,
        can_crit: bool = True,
        fixed_extra_damage: int = 0,
        extra_damage_up_bps: int = 0,
        ignore_troop_coef: bool = False,
        ignore_defense: bool = False,
        fixed_amount: int | None = None,
        kind: str = "skill",
        dispatch: bool = True,
        is_special: bool = False,
        can_mitigate: bool = True,
    ) -> int:
        """伤害原语：核心公式 → 格挡/闪避前置查询 → 暴击 roll → 随机系数 roll
        → 主公式 → 落池 → 事件 → 阵亡处理 → 吸血 → 响应钩子分发。

        - extra_damage_up_bps：额外增伤独立乘区（主动/追击战法单独加成等，Phase 3 §二）；
          与状态修正键 extra_damage_up_bps 聚合。
        - ignore_defense：无视防御属性（如阿喀琉斯追加伤害无视统帅）——核心项
          计算时对方防御属性直接按 0 计。
        - fixed_amount：直接指定伤害量（如三叉戟震荡 = 原伤害 50%），不走主公式、
          不暴击、不消耗 RNG（仍受目标当前兵力截断，最低 1）。
        - is_special：特殊伤害（震荡/圣盾反制等）：发送 damage 事件供客户端播放
          （payload.damage_class="special"），但**不触发任何产生伤害效果的响应**
          （短路响应钩子分发与吸血），Phase 3 §二。
        - can_mitigate：是否参与格挡/闪避/反弹判定（DoT/special/固定量默认不参与，
          调用方置 False）。roll 中时伤害 0 落账，
          payload.mitigation="block"/"evade"/"reflect"。
        - kind：伤害语义标签（basic/skill/pursuit/dot/lightning/trident/fury/trial…），
          供响应钩子识别来源、防递归；不进事件流。
        - dispatch=False 时不分发响应钩子（防无限连锁的内部结算用）。
        返回 damage 事件 seq。RNG 消费顺序固定：减免逐实例判定（施加序；
        实例内 次数格挡→闪避→几率格挡→反弹）→ 暴击 → 随机系数。
        目标已退出战斗时不结算不发事件（5.5 鲁棒性，返回 0）。
        """
        if not target.is_alive():
            return 0

        mitigation: str | None = None
        mitigation_status: StatusInstance | None = None
        if can_mitigate and not is_special and fixed_amount is None:
            mitigation, mitigation_status = self._check_mitigation(source, target, kind)

        is_crit = False
        if mitigation is not None and mitigation != "reflect":
            damage = 0
        elif fixed_amount is not None:
            damage = min(max(formulas.MIN_DAMAGE, fixed_amount), target.troops)
        else:
            core_damage = self._calc_core_damage(source, target, damage_type, ignore_defense)

            source_trait = tr.of(source)
            target_trait = tr.of(target)
            damage_up = self.modifier(source, "damage_up_bps") + self.modifier(
                source, f"{damage_type}_damage_up_bps"
            )
            if source_trait is not None:  # 性格·记仇/鲁莽临时增伤
                damage_up += source_trait.damage_out_bonus(self, source, target, kind)
            damage_reduce = self.modifier(target, "damage_reduce_bps") + self.modifier(
                target, f"{damage_type}_damage_reduce_bps"
            )
            if target_trait is not None:  # 性格·魅惑减伤
                damage_reduce += target_trait.damage_in_reduce(self, target)
            extra_up = extra_damage_up_bps + self.modifier(source, "extra_damage_up_bps")
            vulnerable = self.modifier(target, "vulnerable_bps") + self.modifier(
                target, f"{damage_type}_vulnerable_bps"
            )

            # on_pre_damage_dealt 钩子（觅踵/死亡凝望/致命一矢…改写增伤或必暴）
            pre_ctx = {
                "source": source, "target": target, "damage_type": damage_type,
                "kind": kind, "damage_up_bonus": 0, "extra_up_bonus": 0,
                "forced_crit": False,
            }
            for status in sorted(
                (s for s in self.hero_statuses(source.hero_id)
                 if s.definition.on_pre_damage_dealt is not None),
                key=lambda s: (s.definition.response_priority, s.instance_id),
            ):
                status.definition.on_pre_damage_dealt(self, status, pre_ctx)
            damage_up += pre_ctx["damage_up_bonus"]
            extra_up += pre_ctx["extra_up_bonus"]

            crit_multiplier_bps = BPS
            if can_crit:
                crit_rate = min(
                    max(
                        0,
                        source.crit_rate_bps
                        + self.modifier(source, "crit_rate_bps")
                        + self.modifier(source, f"{damage_type}_crit_rate_bps"),
                    ),
                    formulas.CRIT_RATE_MAX_BPS,
                )
                forced_crit = pre_ctx["forced_crit"] or self._consume_forced_crit(source)
                # 性格·踵之弱：受击方 roll，中则该次攻击必定暴击（帕里斯觅踵联动键）
                if not forced_crit and target_trait is not None:
                    forced_crit = target_trait.forced_crit_on_taken(self, target, parent_seq)
                if forced_crit:
                    is_crit = True
                    crit_multiplier_bps = formulas.CRIT_DAMAGE_MULTIPLIER_BPS
                elif crit_rate > 0:
                    roll = self.rng.rand_bps("crit", f"{damage_type}:{source.hero_id}")
                    if roll < crit_rate:
                        is_crit = True
                        crit_multiplier_bps = formulas.CRIT_DAMAGE_MULTIPLIER_BPS
                if is_crit:
                    crit_damage_up = self.modifier(source, "crit_damage_up_bps")
                    if source_trait is not None:  # 性格·巧射/冷酷暴伤加成
                        crit_damage_up += source_trait.crit_damage_bonus(self, source)
                    if crit_damage_up > 0:
                        crit_multiplier_bps += crit_damage_up

            random_offset = self.rng.rand_index(
                1001, "random_coef", f"{damage_type}:{source.hero_id}"
            )
            damage = formulas.calc_damage(
                core_damage=core_damage,
                attacker_current_troops=source.troops,
                target_current_troops=target.troops,
                skill_rate_bps=rate_bps,
                damage_up_bps=damage_up,
                damage_reduce_bps=damage_reduce,
                extra_damage_up_bps=extra_up,
                vulnerable_bps=vulnerable,
                random_coef_bps=formulas.RANDOM_COEF_MIN_BPS + random_offset,
                crit_multiplier_bps=crit_multiplier_bps,
                fixed_extra_damage=fixed_extra_damage,
                ignore_troop_coef=ignore_troop_coef,
            )

        # 反弹：完整走一遍主公式得到"本应受伤害"（暴击/随机系数照常 roll，
        # 保持 RNG 流确定），受击方落账置 0，金额记下反弹给攻击者。
        reflected_amount = 0
        if mitigation == "reflect":
            reflected_amount = damage
            damage = 0
            is_crit = False  # 暴击已计入反弹金额，0 结算事件不再标暴击

        before = troops_snapshot(target)
        dead, wounded = formulas.split_damage(damage)
        target.troops -= damage
        target.dead_troop += dead
        target.wounded_troop += wounded
        source.total_damage += damage

        damage_payload: dict[str, Any] = {
            "source_id": source.hero_id,
            "target_id": target.hero_id,
            "damage_type": damage_type,
            "amount": damage,
            "is_crit": is_crit,
            "troops": troops_delta(target, before),
        }
        if mitigation is not None:
            damage_payload["mitigation"] = mitigation  # 契约 1.2.0 可选字段
        if is_special:
            damage_payload["damage_class"] = "special"  # 震荡等：播放但不触发响应
        # 状态响应钩子内的选人（试炼反打/三叉戟震荡等）随其伤害事件带出
        selects = self._drain_target_selects()
        if selects:
            damage_payload["target_select"] = selects
        damage_seq = self.writer.emit("damage", damage_payload, parent_seq=parent_seq)
        self.last_damage_result = {
            "amount": damage, "is_crit": is_crit, "mitigation": mitigation,
            "target_id": target.hero_id, "damage_seq": damage_seq,
        }

        if target.troops <= 0:
            self._handle_defeat(target, killer=source, parent_seq=damage_seq)

        # 格挡/闪避/反弹成功：0 结算，不算实际伤害 → 不吸血、不分发响应
        if mitigation is not None:
            # 反弹（圣盾 v3.2）：本应受伤害原样反弹给攻击者。特殊伤害口径：
            # 播放但不触发响应/吸血/减免，不连锁（反弹伤害不可再被反弹）。
            if mitigation == "reflect" and reflected_amount > 0 and source.is_alive():
                tick_seq = self.writer.emit(
                    "status_tick",
                    {
                        "status": mitigation_status.ref(),
                        "source_id": mitigation_status.source_id,
                    },
                    parent_seq=damage_seq,
                    new_group=True,
                )
                self.deal_damage(
                    target, source, damage_type=damage_type,
                    fixed_amount=reflected_amount, parent_seq=tick_seq,
                    kind="reflect", can_crit=False,
                    is_special=True, can_mitigate=False,
                )
            return damage_seq

        # 记仇记账：目标记住最后伤害过自己的敌军（0 伤害的格挡/闪避不算）
        if damage > 0 and source.team_id != target.team_id:
            target.last_damaged_by = source.hero_id

        # 吸血：造成伤害转自疗（不走治疗乘区、不暴击、不消耗 RNG）；special 不吸血
        if not is_special:
            lifesteal = self.modifier(source, "lifesteal_bps") + self.modifier(
                source, f"{damage_type}_lifesteal_bps"
            )
            if kind == "basic":  # 性格·贪食/暴食普攻吸血
                source_trait2 = tr.of(source)
                if source_trait2 is not None:
                    lifesteal += source_trait2.basic_lifesteal(self, source)
            if lifesteal > 0 and source.is_alive():
                amount = damage * lifesteal // BPS
                if amount > 0:
                    self.heal(
                        source, source, parent_seq=damage_seq, can_crit=False,
                        fixed_base=amount, apply_modifiers=False,
                    )

        if dispatch and not is_special:
            self._dispatch_damage_hooks(
                {
                    "source": source,
                    "target": target,
                    "damage_type": damage_type,
                    "amount": damage,
                    "is_crit": is_crit,
                    "damage_seq": damage_seq,
                    "kind": kind,
                }
            )
        return damage_seq

    def _consume_forced_crit(self, source: HeroState) -> bool:
        """必定暴击标记（胜利羽翼「暴击机会」等）：消耗最早实例的 1 次计数。"""
        for status in self.hero_statuses(source.hero_id):
            charges = status.counters.get("forced_crit_charges", 0)
            if charges > 0:
                status.counters["forced_crit_charges"] = charges - 1
                return True
        return False

    def heal(
        self,
        source: HeroState,
        target: HeroState,
        *,
        rate_bps: int = 0,
        parent_seq: int,
        can_crit: bool = True,
        fixed_extra_heal: int = 0,
        fixed_base: int | None = None,
        apply_modifiers: bool = True,
    ) -> int:
        """治疗原语：只回伤兵、不复活、不超上限。实际量为 0 时不发事件（契约省流量规则）。

        - fixed_base：基础治疗量改为固定值（如蛇杖庇护 = 1% 上限 + 1×智力、
          血誓 = 伤害 10%），不走主公式的 max_troops×5% 基数。
        - apply_modifiers=False：跳过治疗乘区/随机/暴击（吸血、血誓等 raw 转化），
          不消耗 RNG。
        返回 heal 事件 seq（未发事件返回 0）。RNG 消费顺序固定：暴击 → 随机系数。
        已退出战斗者不可被治疗（不复活，5.5）。
        """
        if not target.is_alive():
            return 0

        is_crit = False
        if not apply_modifiers:
            heal = max(0, (fixed_base or 0) + fixed_extra_heal)
        else:
            crit_multiplier_bps = BPS
            if can_crit:
                crit_rate = min(
                    max(0, source.heal_crit_rate_bps + self.modifier(source, "heal_crit_rate_bps")),
                    formulas.CRIT_RATE_MAX_BPS,
                )
                if self._consume_forced_crit(source):
                    is_crit = True
                    crit_multiplier_bps = formulas.CRIT_HEAL_MULTIPLIER_BPS
                elif crit_rate > 0:
                    roll = self.rng.rand_bps("crit", f"heal:{source.hero_id}")
                    if roll < crit_rate:
                        is_crit = True
                        crit_multiplier_bps = formulas.CRIT_HEAL_MULTIPLIER_BPS

            random_offset = self.rng.rand_index(1001, "random_coef", f"heal:{source.hero_id}")
            random_coef_bps = formulas.RANDOM_COEF_MIN_BPS + random_offset
            heal_up_total = self.modifier(source, "heal_up_bps")
            source_trait = tr.of(source)
            if source_trait is not None:  # 性格·仁心/师者/柔波治疗加成
                heal_up_total += source_trait.heal_up_bonus(self, source)
            if fixed_base is not None:
                heal = formulas.apply_heal_modifiers(
                    fixed_base,
                    heal_up_bps=heal_up_total,
                    heal_received_up_bps=self.modifier(target, "heal_received_up_bps"),
                    heal_reduce_bps=self.modifier(target, "heal_reduce_bps"),
                    random_coef_bps=random_coef_bps,
                    crit_multiplier_bps=crit_multiplier_bps,
                )
                heal += fixed_extra_heal
            else:
                heal = formulas.calc_heal(
                    healer_max_troops=source.max_troops,
                    heal_attr=self.effective_attr(source, "intelligence"),
                    heal_rate_bps=rate_bps,
                    heal_up_bps=heal_up_total,
                    heal_received_up_bps=self.modifier(target, "heal_received_up_bps"),
                    heal_reduce_bps=self.modifier(target, "heal_reduce_bps"),
                    random_coef_bps=random_coef_bps,
                    crit_multiplier_bps=crit_multiplier_bps,
                    fixed_extra_heal=fixed_extra_heal,
                )

        actual = formulas.constrain_heal(
            heal,
            wounded_troop=target.wounded_troop,
            max_troops=target.max_troops,
            current_troops=target.troops,
        )
        if actual <= 0:
            return 0  # 无状态变化不发事件

        before = troops_snapshot(target)
        target.troops += actual
        target.wounded_troop -= actual
        source.total_heal += actual
        return self.writer.emit(
            "heal",
            {
                "source_id": source.hero_id,
                "target_id": target.hero_id,
                "amount": actual,
                "is_crit": is_crit,
                "troops": troops_delta(target, before),
            },
            parent_seq=parent_seq,
        )

    def apply_status(
        self,
        source: HeroState,
        target: HeroState,
        definition: StatusDef,
        *,
        parent_seq: int,
    ) -> StatusInstance | None:
        """施加状态原语。

        已存在同 id 状态时：可叠加 → 层数+1 并重置计次（status_refresh）；
        可刷新 → 重置计次（status_refresh）；负面默认不可刷新不可叠加 → 静默拒绝
        （不发事件，契约省流量规则 2），返回 None。
        B3：施加 forbid_active 类控制时，若目标有准备中战法 → 额外产生打断
        （skill_trigger kind=interrupted，与状态事件同组，任务书 5.3）。
        """
        if not target.is_alive():
            return None
        # 石化免疫（珀尔修斯镜盾，Phase 3）：静默拒绝
        if definition.status_id == "petrify" and st.any_forbid(
            self.hero_statuses(target.hero_id), "petrify_immune"
        ):
            return None
        owned = self.hero_statuses(target.hero_id)
        existing = next((s for s in owned if s.status_id == definition.status_id), None)
        result: StatusInstance | None
        if existing is not None:
            can_stack = definition.allows_stack() and existing.stacks < definition.max_stacks
            if not can_stack and not definition.allows_refresh():
                return None  # 静默拒绝
            if can_stack:
                existing.stacks += 1
            existing.action_tick_count = 0
            self.writer.emit(
                "status_refresh",
                {
                    "status": existing.ref(),
                    "source_id": source.hero_id,
                    "stacks": existing.stacks,
                    "duration_rounds": definition.duration_rounds,
                },
                parent_seq=parent_seq,
            )
            existing.source_id = source.hero_id  # 刷新后来源以最新施加者计
            result = existing
        else:
            self._status_instance_counter += 1
            instance = StatusInstance(
                instance_id=self._status_instance_counter,
                definition=definition,
                owner_id=target.hero_id,
                source_id=source.hero_id,
            )
            owned.append(instance)
            self.writer.emit(
                "status_apply",
                {
                    "status": instance.ref(),
                    "source_id": source.hero_id,
                    "stacks": instance.stacks,
                    "duration_rounds": definition.duration_rounds,
                },
                parent_seq=parent_seq,
            )
            if definition.on_apply is not None:
                definition.on_apply(self, instance)
            result = instance

        # 性格·孤怨照影：美杜莎石化别人成功后 8% 自身石化（防递归：对自己施加不回调）
        if definition.status_id == "petrify" and source.hero_id != target.hero_id:
            self.notify_petrify_out(source, parent_seq)

        # on_control_taken 钩子（圣盾反制控制，Phase 3）：目标被敌方施加控制类状态后
        if (
            definition.kind == st.CONTROL
            and source.team_id != target.team_id
            and target.is_alive()
        ):
            for status in sorted(
                (s for s in self.hero_statuses(target.hero_id)
                 if s.definition.on_control_taken is not None),
                key=lambda s: (s.definition.response_priority, s.instance_id),
            ):
                if self._game_winner is not None:
                    break
                status.definition.on_control_taken(
                    self, status, {"source": source, "control": definition,
                                   "parent_seq": parent_seq}
                )

        # 控制打断：缄默/冥锁/石化施加时目标在准备中 → 打断（两个事件两种特效）
        if definition.modifiers.get("forbid_active") and target.hero_id in self._preparing:
            preparing = self._preparing.pop(target.hero_id)
            skill = SKILL_REGISTRY[preparing["skill_id"]]
            self._emit_skill_trigger(
                target, skill, "interrupted", [],
                parent_seq=parent_seq, interrupted_by=result.ref(),
            )
        return result

    def remove_status(
        self, instance: StatusInstance, *, reason: str, parent_seq: int
    ) -> None:
        """移除状态原语（驱散/来源阵亡等；到期由行动窗口计次统一处理）。"""
        owned = self.hero_statuses(instance.owner_id)
        if instance not in owned:
            return
        owned.remove(instance)
        self.writer.emit(
            "status_remove",
            {"status": instance.ref(), "reason": reason},
            parent_seq=parent_seq,
        )

    def modify_attr(
        self,
        target: HeroState,
        changes: list[tuple[str, int]],
        *,
        scope: str,
        parent_seq: int,
    ) -> int:
        """属性修改原语（基础值直改）。scope=game 的修改在局末自动回滚（不发事件）。

        临时修正（scope=temporary）必须通过状态 modifiers 承载，不走本入口。
        """
        if scope not in ("game", "series"):
            raise BattleCoreError("modify_attr 仅支持 game/series", scope=scope)
        payload_changes = []
        for attr, delta in changes:
            if attr not in ATTR_NAMES:
                raise BattleCoreError("未知属性", attr=attr)
            before = getattr(target, attr)
            after = max(0, before + delta)
            actual_delta = after - before
            setattr(target, attr, after)
            if scope == "game" and actual_delta != 0:
                self._game_attr_reverts.append((target.hero_id, attr, actual_delta))
            payload_changes.append({"attr": attr, "before": before, "after": after})
        return self.writer.emit(
            "attr_change",
            {"hero_id": target.hero_id, "scope": scope, "changes": payload_changes},
            parent_seq=parent_seq,
        )

    def adjust_status_attr(
        self,
        status: StatusInstance,
        attr: str,
        delta: int,
        *,
        parent_seq: int,
    ) -> int:
        """状态承载的四维动态修正（冥祭献统/试炼/凝视吸取等累计型）。

        修正写入实例 dynamic_modifiers（不乘层数），随状态移除自动消失
        （来源阵亡清理即自然返还）；事件化为 attr_change(scope=temporary,
        source_status)，before/after 为**有效属性**视角。delta=0 不发事件。
        """
        if attr not in ATTR_NAMES:
            raise BattleCoreError("未知属性", attr=attr)
        if delta == 0:
            return 0
        owner = self.heroes[status.owner_id]
        before = self.effective_attr(owner, attr)
        key = f"{attr}_delta"
        status.dynamic_modifiers[key] = status.dynamic_modifiers.get(key, 0) + delta
        after = self.effective_attr(owner, attr)
        return self.writer.emit(
            "attr_change",
            {
                "hero_id": owner.hero_id,
                "scope": "temporary",
                "source_status": status.ref(),
                "changes": [{"attr": attr, "before": before, "after": after}],
            },
            parent_seq=parent_seq,
        )

    # ------------------------------------------------------------------ 阵亡清理

    def _handle_defeat(self, target: HeroState, *, killer: HeroState, parent_seq: int) -> None:
        """阵亡即退出（任务书 5.5）：不再行动、不可为目标、其施加的状态全部事件化清理；
        延迟中的犹豫行动与准备中战法随之作废（静默，D-02 边界 1）。"""
        target.defeated = True
        killer.kills += 1
        self.defeat_count += 1
        defeat_seq = self.writer.emit(
            "hero_defeated",
            {
                "hero_id": target.hero_id,
                "killer_id": killer.hero_id,
                "is_main_hero": target.is_main,
            },
            parent_seq=parent_seq,
        )
        # 其施加给其他武将的状态立即删除（清理事件化，挂 hero_defeated 下）
        for hero_id in self.hero_order:
            if hero_id == target.hero_id:
                continue
            owned = self.hero_statuses(hero_id)
            for status in [s for s in owned if s.source_id == target.hero_id]:
                owned.remove(status)
                self.writer.emit(
                    "status_remove",
                    {"status": status.ref(), "reason": "source_defeated"},
                    parent_seq=defeat_seq,
                )
        # 自身携带的状态随退出静默清空（武将已离场，无播放意义）
        self.statuses[target.hero_id] = []
        self._preparing.pop(target.hero_id, None)
        self._delayed_actions.pop(target.hero_id, None)
        if target.is_main and self._game_winner is None:
            self._game_winner = killer.team_id  # 仅主将阵亡判定该局失败（任务书 5.1）

        # 性格·阵亡触发（Phase 3）：求胜（击杀方全队）/ 渡魂船费类战法走状态钩子，
        # 好战额外行动（任意阵亡）——hero_order 序分发
        if self._game_winner is None:
            for hero_id in self.hero_order:
                hero = self.heroes[hero_id]
                if not hero.is_alive():
                    continue
                trait = tr.of(hero)
                if trait is None:
                    continue
                trait.on_kill(self, hero, killer, target, defeat_seq)
                if trait.on_any_defeat(self, hero, target, defeat_seq):
                    self._extra_action_queue.append(hero.hero_id)
                if self._game_winner is not None:
                    return
            # 状态钩子 on_hero_defeated（渡魂船费/胜利羽翼击杀返还…全局定序）
            entries: list[tuple[int, int, int, StatusInstance]] = []
            for hero_id in self.hero_order:
                if not self.heroes[hero_id].is_alive():
                    continue
                for status in self.hero_statuses(hero_id):
                    if status.definition.on_hero_defeated is not None:
                        entries.append((
                            status.definition.response_priority,
                            self._hero_rank[hero_id], status.instance_id, status,
                        ))
            entries.sort(key=lambda e: (e[0], e[1], e[2]))
            for _, _, _, status in entries:
                if self._game_winner is not None:
                    return
                owner = self.heroes[status.owner_id]
                if not owner.is_alive() or status not in self.hero_statuses(status.owner_id):
                    continue
                status.definition.on_hero_defeated(
                    self, status, {"victim": target, "killer": killer,
                                   "defeat_seq": defeat_seq}
                )


class _BasicAttackStub(Skill):
    """普攻的 skill_trigger 事件占位（仅用于犹豫延迟宣告，skill_id=basic_attack）。"""

    def execute(self, engine, actor, targets, trigger_seq):  # pragma: no cover
        raise BattleCoreError("普攻占位战法不可执行")


_BASIC_ATTACK_STUB = _BasicAttackStub(skill_id="basic_attack")
