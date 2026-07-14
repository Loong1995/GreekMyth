from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from typing import Any

from battlecore.config.config_db import ConfigDB
from battlecore.config.schema import BattleInput, BattleSummary, HeroConfig, TriggerCheckResult
from battlecore.domain.effect import Effect
from battlecore.domain.enums import (
    BattleResultType,
    DamageType,
    EffectType,
    EventType,
    HeroRole,
    SkillCategory,
    StateType,
    TargetPolicy,
    Timing,
    TriggerMode,
    TriggerableType,
)
from battlecore.domain.hero import Hero
from battlecore.domain.skill import Skill, State, Triggerable
from battlecore.engine.damage_calculator import (
    CRIT_DAMAGE_MULTIPLIER_BPS,
    CRIT_HEAL_MULTIPLIER_BPS,
    RANDOM_COEF_MIN_BPS,
    apply_damage as apply_troop_damage,
    apply_heal as apply_troop_heal,
    apply_wounded_to_dead,
    apply_heal_settlement_adjustments,
    calc_damage,
    calc_heal,
    get_effective_crit_rate_bps,
    get_effective_heal_crit_rate_bps,
)
from battlecore.engine.action_order import (
    build_action_order_payload,
    build_round_action_order,
    format_action_order_table,
    format_round_effective_attrs_table,
)
from battlecore.config.chain_reaction_config import TriggerStepConfig
from battlecore.engine.chain_reaction import (
    find_regular_group,
    find_spy_group,
    skill_steps_for_spy_group,
    skills_for_chain_step,
    sort_regular_states_for_dispatch,
    sort_spy_states_for_dispatch,
)
from battlecore.engine.hit_rate import (
    format_hit_points_recalc_formula,
    format_target_pool_hit_rate_weights,
    recalc_hit_points_from_troops,
    calc_realtime_hit_rate_bps,
    format_realtime_hit_rate_formula,
)
from battlecore.event.battle_event import BattleEvent
from battlecore.rng.deterministic_rng import DeterministicRNG


@dataclass(slots=True)
class PseudoRandomState:
    """单个概率点的伪随机累计状态。

    key 由 battleId + casterId + skillId + effectId + targetId + triggerType 组成。
    State 触发的 casterId 取 state 持有者（owner），使每名携带者独立累计成败 streak。
    """

    fail_count: int = 0
    success_streak: int = 0


GLOBAL_TIMINGS = frozenset(
    {
        Timing.BATTLE_START,
        Timing.HIT_RATE_INIT,
        Timing.PREPARE,
        Timing.ROUND_START,
        Timing.ROUND_END,
        Timing.BATTLE_END,
    }
)


@dataclass(slots=True)
class BattleContext:
    battle_id: str
    config_version: str
    seed: int
    max_rounds: int
    config_db: ConfigDB
    rng: DeterministicRNG
    round_no: int = 0
    current_timing: Timing | None = None
    heroes: dict[str, Hero] = field(default_factory=dict)
    teams: dict[str, list[str]] = field(default_factory=dict)
    main_hero_by_team: dict[str, str] = field(default_factory=dict)
    speed_order: list[str] = field(default_factory=list)
    round_action_orders: dict[int, list[str]] = field(default_factory=dict)
    round_merge_decisions: dict[int, list] = field(default_factory=dict)
    event_queue: deque[BattleEvent] = field(default_factory=deque)
    event_stream: list[BattleEvent] = field(default_factory=list)
    human_logs: list[str] = field(default_factory=list)
    battle_finished: bool = False
    battle_result: BattleResultType = BattleResultType.UNFINISHED
    winner_team_id: str | None = None
    finish_reason: str | None = None
    current_actor_id: str | None = None
    skill_timing_index: dict[Timing, list[str]] = field(default_factory=dict)
    regular_state_timing_index: dict[Timing, list[str]] = field(default_factory=dict)
    spy_state_event_index: dict[EventType, list[str]] = field(default_factory=dict)
    skill_instances: dict[str, Skill] = field(default_factory=dict)
    effect_instances: dict[str, Effect] = field(default_factory=dict)
    state_instances: dict[str, State] = field(default_factory=dict)
    _next_event_id: int = 1
    _next_state_seq: int = 1
    max_chain_depth: int = 16
    max_events_per_step: int = 256
    pseudo_random_states: dict[str, PseudoRandomState] = field(default_factory=dict)

    @classmethod
    def build_from_input(cls, battle_input: BattleInput, config_db: ConfigDB) -> BattleContext:
        context = cls(
            battle_id=battle_input.battle_id,
            config_version=battle_input.config_version,
            seed=battle_input.seed,
            max_rounds=battle_input.max_rounds,
            config_db=config_db,
            rng=DeterministicRNG(battle_input.seed),
        )
        for hero_config in battle_input.team_a_heroes + battle_input.team_b_heroes:
            context._add_hero_from_config(hero_config)
        return context

    def _add_hero_from_config(self, config: HeroConfig) -> None:
        hero = Hero(
            instance_id=config.hero_id,
            config_id=config.hero_id,
            name=config.name,
            team_id=config.team_id,
            role=config.role,
            position=config.position,
            max_troops=config.max_troops,
            troops=config.max_troops,
            force=config.force,
            intelligence=config.intelligence,
            command=config.command,
            speed=config.speed,
            crit_rate_bps=config.crit_rate_bps,
            heal_crit_rate_bps=config.heal_crit_rate_bps,
        )
        self.heroes[hero.instance_id] = hero
        self.teams.setdefault(hero.team_id, []).append(hero.instance_id)
        if hero.role == HeroRole.MAIN:
            self.main_hero_by_team[hero.team_id] = hero.instance_id
        for skill_id in config.skill_ids:
            skill_config = self.config_db.skill_configs[skill_id]
            skill = Skill.from_config(f"{hero.instance_id}:{skill_id}", skill_config, hero)
            hero.add_skill(skill)
            self.register_skill(skill)
            for effect_id in self._skill_effect_ids(skill_config):
                effect_config = self.config_db.effect_configs[effect_id]
                effect = Effect.from_config(
                    f"{skill.instance_id}:{effect_id}",
                    effect_config,
                    owner_hero=hero,
                    owner_skill=skill,
                )
                skill.effects.append(effect)
                self.effect_instances[effect.instance_id] = effect

    def _skill_effect_ids(self, skill_config) -> list[str]:
        effect_ids = list(skill_config.effect_ids)
        for extra_key in ("prepare_effect_ids", "release_effect_ids"):
            for effect_id in skill_config.params.get(extra_key, []):
                if effect_id not in effect_ids:
                    effect_ids.append(effect_id)
        return effect_ids

    def prepare_round_action_order(self, round_no: int) -> None:
        order_result = build_round_action_order(self, round_no)
        self.speed_order = order_result.action_order
        self.round_action_orders[round_no] = list(self.speed_order)
        self.round_merge_decisions[round_no] = order_result.merge_decisions
        table_text = format_action_order_table(
            self,
            round_no,
            self.speed_order,
            merge_decisions=order_result.merge_decisions,
        )
        saved_timing = self.current_timing
        self.current_timing = Timing.ROUND_START
        self.log(table_text)
        self.current_timing = saved_timing

    def roll_damage_crit(
        self,
        actor: Hero,
        target: Hero,
        *,
        effect: Effect | None = None,
        state: State | None = None,
        skill: Skill | None = None,
    ) -> TriggerCheckResult:
        effect_id = effect.config_id if effect else state.config_id if state else "direct"
        skill_id = skill.config_id if skill else state.config_id if state else "direct"
        return self.roll_pseudo_random_probability(
            caster_id=actor.instance_id,
            skill_id=skill_id,
            effect_id=effect_id,
            target_id=target.instance_id,
            trigger_type="DAMAGE_CRIT",
            base_rate_bps=get_effective_crit_rate_bps(actor),
            params={},
        )

    def roll_heal_crit(
        self,
        actor: Hero,
        target: Hero,
        *,
        effect: Effect | None = None,
        skill: Skill | None = None,
    ) -> TriggerCheckResult:
        effect_id = effect.config_id if effect else "direct"
        skill_id = skill.config_id if skill else "direct"
        return self.roll_pseudo_random_probability(
            caster_id=actor.instance_id,
            skill_id=skill_id,
            effect_id=effect_id,
            target_id=target.instance_id,
            trigger_type="HEAL_CRIT",
            base_rate_bps=get_effective_heal_crit_rate_bps(actor),
            params={},
        )

    def rebuild_indexes(self) -> None:
        """重建技能 / 状态触发索引。

        三类索引（候选池；最终顺序见 chain_reaction_config + STATE_RESPONSE_REFERENCE.md）：
        - skill_timing_index：timing -> skill ids
        - regular_state_timing_index：timing -> REGULAR 状态 ids
        - spy_state_event_index：event_type -> SPY 状态 ids

        ATTR / DAMAGE_REDUCE 在 trigger_mode=NONE 时不进入触发索引，只供数值模型读取。
        """
        self.skill_timing_index = {timing: [] for timing in Timing}
        self.regular_state_timing_index = {timing: [] for timing in Timing}
        self.spy_state_event_index = {event_type: [] for event_type in EventType}
        for skill in self.skill_instances.values():
            if skill.category == SkillCategory.PURSUIT:
                continue
            if skill.owner.exited or not skill.enabled_flag:
                continue
            for timing in skill.trigger_timings:
                self.skill_timing_index.setdefault(timing, []).append(skill.instance_id)
        for state in self.state_instances.values():
            if not self.is_state_battle_active(state):
                continue
            if state.state_type in (StateType.ATTR, StateType.DAMAGE_REDUCE) and state.trigger_mode == TriggerMode.NONE:
                continue
            if state.trigger_mode == TriggerMode.REGULAR:
                for timing in state.trigger_timings:
                    self.regular_state_timing_index.setdefault(timing, []).append(state.instance_id)
            if state.trigger_mode == TriggerMode.SPY:
                listen_event_types = state.listen_event_types
                for event_type in listen_event_types:
                    enum_event = EventType(event_type)
                    self.spy_state_event_index.setdefault(enum_event, []).append(state.instance_id)

    def is_state_battle_active(self, state: State) -> bool:
        """状态是否仍可在本局生效或响应。

        阵亡武将身上的状态、以及由阵亡武将施加给他人的状态，均视为失效。
        """
        if state.owner.exited:
            return False
        if state.source_actor_id:
            source = self.heroes.get(state.source_actor_id)
            if source is not None and source.exited:
                return False
        return True

    def is_hero_battle_active(self, hero: Hero | None) -> bool:
        return hero is not None and hero.is_alive()

    def resolve_hero_for_state_effect(self, hero_id: str) -> tuple[Hero | None, str | None]:
        """解析 state / effect 目标；无效时返回 (hero_or_none, reason)。"""
        hero = self.heroes.get(hero_id)
        if hero is None:
            return None, "目标不存在"
        if hero.exited:
            return hero, f"{hero.name}已阵亡"
        if hero.troops <= 0:
            return hero, f"{hero.name}兵力为0"
        return hero, None

    def _validate_effect_targets(
        self, targets: list[Hero]
    ) -> tuple[list[Hero], Hero | None, str | None]:
        """将选中的目标分为有效 / 无效；全无效时返回首个无效目标与原因。"""
        valid: list[Hero] = []
        first_invalid: Hero | None = None
        first_reason: str | None = None
        for target in targets:
            _, reason = self.resolve_hero_for_state_effect(target.instance_id)
            if reason is None:
                valid.append(target)
            elif first_invalid is None:
                first_invalid = target
                first_reason = reason
        return valid, first_invalid, first_reason

    def log_effect_skipped_invalid_target(
        self,
        actor: Hero,
        effect: Effect,
        *,
        reason: str,
        target_name: str,
    ) -> None:
        self.log(f"{actor.name} 的效果 {effect.name} 未执行：目标无效（{reason}，目标={target_name}）")

    def log_state_skipped_invalid_target(
        self,
        state: State,
        *,
        reason: str,
        target_name: str = "",
    ) -> None:
        detail = f"，目标={target_name}" if target_name else ""
        self.log(f"{state.owner.name} 的 {state.name} 未执行：目标无效（{reason}{detail}）")

    def register_skill(self, skill: Skill) -> None:
        self.skill_instances[skill.instance_id] = skill

    def register_state(self, state: State) -> None:
        self.state_instances[state.instance_id] = state
        state.owner.add_state(state)
        self.rebuild_indexes()

    def unregister_state(self, state: State) -> None:
        self.state_instances.pop(state.instance_id, None)
        state.owner.remove_state(state.instance_id)
        self.rebuild_indexes()

    def emit_event(
        self,
        event_type: EventType,
        *,
        actor_id: str | None = None,
        target_ids: list[str] | None = None,
        source_type: str | None = None,
        source_id: str | None = None,
        skill_id: str | None = None,
        effect_id: str | None = None,
        state_instance_id: str | None = None,
        rng_index: int | None = None,
        payload: dict[str, Any] | None = None,
        chain_depth: int = 0,
    ) -> BattleEvent:
        event = BattleEvent(
            event_id=self._next_event_id,
            event_type=event_type,
            round_no=self.round_no,
            timing=self.current_timing,
            chain_depth=chain_depth,
            rng_index=rng_index,
            source_type=source_type,
            source_id=source_id,
            actor_id=actor_id,
            target_ids=target_ids or [],
            skill_id=skill_id,
            effect_id=effect_id,
            state_instance_id=state_instance_id,
            payload=payload or {},
        )
        self._next_event_id += 1
        self.event_stream.append(event)
        self.event_queue.append(event)
        return event

    def dispatch_events(self) -> None:
        """派发事件队列，并触发监听型 SPY 状态。

        DAMAGE_SETTLED / HEAL_SETTLED 等事件会进入 event_queue。
        SPY 状态在 should_trigger_by_event 中按来源/目标/持有者/damage 等过滤后触发。

        为避免无限递归：
        - max_events_per_step 限制单步最多处理事件数。
        - max_chain_depth 限制连锁深度。
        - state.responded_event_ids 避免同一状态重复响应同一事件。
        """
        processed_count = 0
        while self.event_queue:
            event = self.event_queue.popleft()
            processed_count += 1
            if processed_count > self.max_events_per_step:
                self.log("事件派发中止：超过单步最大事件数量")
                break
            if event.chain_depth > self.max_chain_depth:
                self.log("事件派发跳过：超过最大连锁深度")
                continue
            self._dispatch_spy_listeners_for_event(event)
            if self.battle_finished:
                break

    def _dispatch_spy_listeners_for_event(self, event: BattleEvent) -> None:
        """SPY 状态 + SpyGroupConfig 内 SKILL 步（如 PURSUIT）的响应入口。

        顺序由 sort_spy_states_for_dispatch 按 chain_reaction_config 决定；
        详见 STATE_RESPONSE_REFERENCE.md §七。
        """
        group = find_spy_group(self.config_db.spy_groups, event.event_type)
        eligible_states: list[State] = []
        for state_id in list(self.spy_state_event_index.get(event.event_type, [])):
            state = self.state_instances.get(state_id)
            if state is None:
                continue
            if state.trigger_mode != TriggerMode.SPY:
                continue
            if event.event_id in state.responded_event_ids:
                continue
            if not self.is_state_battle_active(state):
                continue
            if not state.should_trigger_by_event(self, event):
                continue
            eligible_states.append(state)
        ordered_states = sort_spy_states_for_dispatch(
            eligible_states,
            primary_group=group,
            spy_groups=self.config_db.spy_groups,
            unconfigured_sort=self.config_db.state_unconfigured_sort,
        )
        timing = self.current_timing or Timing.BATTLE_START
        for state in ordered_states:
            state.responded_event_ids.add(event.event_id)
            self.try_trigger_triggerable(state, timing, source_event=event)
            self.check_battle_finish()
            if self.battle_finished:
                return
        if group is None:
            return
        for step in skill_steps_for_spy_group(group):
            self._try_trigger_chain_skill_step(event, step)
            if self.battle_finished:
                return

    def _try_trigger_chain_skill_step(self, event: BattleEvent, step: TriggerStepConfig) -> None:
        if step.skill_category == SkillCategory.PURSUIT:
            if not self.is_basic_damage_settled_signal(event):
                return
            actor = self.heroes.get(event.actor_id or "")
            if actor is None or actor.exited:
                return
            timing = self.current_timing or Timing.BASIC
            for skill in skills_for_chain_step(actor.skills, step):
                if not skill.enabled_flag:
                    continue
                self.try_trigger_triggerable(skill, timing, event)
                if self.battle_finished:
                    break

    def log(self, message: str) -> None:
        timing = self.current_timing.value if self.current_timing else "NONE"
        self.human_logs.append(f"[Battle {self.battle_id}][Round {self.round_no}][{timing}] {message}")

    def get_alive_heroes(self, team_id: str | None = None) -> list[Hero]:
        hero_ids = self.teams.get(team_id, []) if team_id else list(self.heroes)
        return [self.heroes[hero_id] for hero_id in hero_ids if self.heroes[hero_id].is_alive()]

    def _team_hit_rate_allies(self, team_id: str) -> list[Hero]:
        return [
            self.heroes[hero_id]
            for hero_id in self.teams.get(team_id, [])
            if self.heroes[hero_id].is_alive() and not self.heroes[hero_id].exited
        ]

    def _team_hit_points_sum(self, team_id: str) -> int:
        return sum(hero.hit_points_bps for hero in self._team_hit_rate_allies(team_id))

    def _recalc_hero_realtime_hit_rate(self, hero: Hero, team_sum: int) -> None:
        hero.realtime_hit_rate_bps = calc_realtime_hit_rate_bps(hero.hit_points_bps, team_sum)

    def _recalc_team_realtime_hit_rates(self, team_id: str) -> int:
        team_sum = self._team_hit_points_sum(team_id)
        for ally in self._team_hit_rate_allies(team_id):
            self._recalc_hero_realtime_hit_rate(ally, team_sum)
        return team_sum

    def _log_hero_realtime_hit_rate(
        self,
        hero: Hero,
        team_sum: int,
        *,
        prefix: str = "",
        point_change: str | None = None,
    ) -> None:
        formula = format_realtime_hit_rate_formula(hero.hit_points_bps, team_sum, hero.realtime_hit_rate_bps)
        extra = f" {point_change}" if point_change else ""
        self.log(f"{prefix}{hero.name} 实时受击率={hero.realtime_hit_rate_bps} ({formula}){extra}")

    def _init_team_hit_rates(self) -> None:
        for team_id in sorted(self.teams):
            for ally in self._team_hit_rate_allies(team_id):
                ally.initial_hit_points_bps = ally.hit_points_bps
            team_sum = self._recalc_team_realtime_hit_rates(team_id)
            for ally in self._team_hit_rate_allies(team_id):
                self._log_hero_realtime_hit_rate(ally, team_sum, prefix="[受击率·初始化] ")

    def _on_troop_settlement_hit_rate(self, hero: Hero, signal: str) -> None:
        if hero.exited or not hero.is_alive():
            return
        old_points, initial_points, offset, new_points = recalc_hit_points_from_troops(hero)
        team_sum = self._recalc_team_realtime_hit_rates(hero.team_id)
        lost_troops = hero.max_troops - hero.troops
        troop_ratio = f"损失{lost_troops}/{hero.max_troops}"
        point_change = (
            f"受击点数 {old_points}->{new_points} "
            f"({format_hit_points_recalc_formula(initial_points, offset, new_points)}, "
            f"扣减=({troop_ratio})*3000={offset})"
        )
        self._log_hero_realtime_hit_rate(
            hero,
            team_sum,
            prefix=f"[受击率·{signal}] ",
            point_change=point_change,
        )
        for ally in self._team_hit_rate_allies(hero.team_id):
            if ally.instance_id == hero.instance_id:
                continue
            self._log_hero_realtime_hit_rate(ally, team_sum, prefix=f"[受击率·{signal}·同步] ")

    def _on_hero_exited_hit_rate(self, exited_hero: Hero) -> None:
        """阵亡退出后，在剩余场上武将间重新归一实时受击率（退出者移出分母）。"""
        team_id = exited_hero.team_id
        remaining = self._team_hit_rate_allies(team_id)
        if not remaining:
            self.log(f"[受击率·HERO_EXITED_SETTLED] {exited_hero.name} 退出，本方已无在场武将")
            return
        team_sum = self._recalc_team_realtime_hit_rates(team_id)
        self.log(
            f"[受击率·HERO_EXITED_SETTLED] {exited_hero.name} 退出，"
            f"归一分母改为 {team_sum}（场上 {len(remaining)} 人）"
        )
        for ally in remaining:
            self._log_hero_realtime_hit_rate(ally, team_sum, prefix="[受击率·HERO_EXITED_SETTLED] ")

    def get_enemy_team_id(self, team_id: str) -> str:
        for candidate in sorted(self.teams):
            if candidate != team_id:
                return candidate
        raise ValueError(f"team {team_id} has no enemy team")

    def is_basic_damage_settled_signal(self, source_event: BattleEvent | None) -> bool:
        if source_event is None or source_event.event_type != EventType.DAMAGE_SETTLED:
            return False
        if int(source_event.payload.get("damage", 0)) <= 0:
            return False
        skill_config = self.config_db.skill_configs.get(source_event.skill_id or "")
        return skill_config is not None and skill_config.category == SkillCategory.BASIC

    def roll_trigger_probability(
        self,
        triggerable: Triggerable,
        source_event: BattleEvent | None = None,
    ) -> TriggerCheckResult:
        """对 Skill / State 触发做伪随机概率判定。

        这里保留面板概率的不确定性，但用 fail_count 逐步补偿连续失败，
        用 success_streak 抑制连续成功爆发。所有状态写在 BattleContext 内，
        因此服务端结算可复现，事件日志也能完整审计。
        """
        if isinstance(triggerable, Skill):
            params = triggerable.params
            caster_id = triggerable.owner.instance_id
            skill_id = triggerable.config_id
            effect_id = "*"
            target_id = "*"
            trigger_type = "SKILL_TRIGGER"
        else:
            params = triggerable.payload
            # State 概率按持有者独立累计，不按神谕施加者共享。
            caster_id = triggerable.owner.instance_id
            skill_id = triggerable.source_skill_id or triggerable.config_id
            effect_id = "*"
            target_id = triggerable.owner.instance_id
            if source_event and source_event.target_ids:
                target_id = source_event.target_ids[0]
            trigger_type = "STATE_TRIGGER"
        return self.roll_pseudo_random_probability(
            caster_id=caster_id,
            skill_id=skill_id,
            effect_id=effect_id,
            target_id=target_id,
            trigger_type=trigger_type,
            base_rate_bps=triggerable.probability_bps,
            params=params,
        )

    def roll_effect_probability(self, effect: Effect, actor: Hero, targets: list[Hero]) -> TriggerCheckResult:
        """对 Effect 概率做伪随机判定。

        Effect 的 key 带 targetId；当前配置里的概率 Effect 都是单目标。
        若未来出现多目标独立判定，应把 Effect 拆成多个原子 Effect，或在这里扩展为逐目标结算。
        """
        target_id = targets[0].instance_id if targets else "*"
        return self.roll_pseudo_random_probability(
            caster_id=actor.instance_id,
            skill_id=effect.owner_skill.config_id if effect.owner_skill else "*",
            effect_id=effect.config_id,
            target_id=target_id,
            trigger_type="EFFECT_TRIGGER",
            base_rate_bps=effect.probability_bps,
            params=effect.params,
            effect=effect,
        )

    def roll_pseudo_random_probability(
        self,
        *,
        caster_id: str,
        skill_id: str,
        effect_id: str,
        target_id: str,
        trigger_type: str,
        base_rate_bps: int,
        params: dict[str, Any],
        effect: Effect | None = None,
    ) -> TriggerCheckResult:
        if base_rate_bps >= 10000:
            return TriggerCheckResult(
                allowed=True,
                reason="ALWAYS_TRIGGER",
                roll_bps=None,
                threshold_bps=10000,
                rng_index=None,
                pseudo_random_key=None,
                base_rate_bps=base_rate_bps,
                current_rate_bps=10000,
            )
        pr_params = self._pseudo_random_params(base_rate_bps, params, effect)
        key = "|".join([self.battle_id, caster_id, skill_id, effect_id, target_id, trigger_type])
        state = self.pseudo_random_states.setdefault(key, PseudoRandomState())
        current_rate = self._clamp_bps(
            base_rate_bps
            + state.fail_count * pr_params["bonus_per_fail_bps"]
            - state.success_streak * pr_params["penalty_per_success_bps"],
            pr_params["min_rate_bps"],
            pr_params["max_rate_bps"],
        )
        guarantee_count = pr_params["guarantee_count"]
        guarantee_triggered = guarantee_count > 0 and state.fail_count >= guarantee_count
        rng_index: int | None = None
        roll_bps: int | None = None
        allowed = False
        reason = "PROBABILITY_FAIL"
        if guarantee_triggered:
            allowed = True
            reason = "GUARANTEE_TRIGGER"
        else:
            rng_index, roll_bps = self.rng.rand_bps(key, "pseudo_random_probability")
            allowed = roll_bps < current_rate
            reason = "OK" if allowed else "PROBABILITY_FAIL"

        before_fail_count = state.fail_count
        before_success_streak = state.success_streak
        if allowed:
            state.fail_count = 0
            state.success_streak += 1
        else:
            state.fail_count += 1
            state.success_streak = 0

        return TriggerCheckResult(
            allowed=allowed,
            reason=reason,
            roll_bps=roll_bps,
            threshold_bps=current_rate,
            rng_index=rng_index,
            pseudo_random_key=key,
            base_rate_bps=base_rate_bps,
            current_rate_bps=current_rate,
            fail_count=before_fail_count,
            success_streak=before_success_streak,
            guarantee_triggered=guarantee_triggered,
        )

    def _pseudo_random_params(
        self,
        base_rate_bps: int,
        params: dict[str, Any],
        effect: Effect | None = None,
    ) -> dict[str, int]:
        configured = params.get("pseudo_random", {})
        if not isinstance(configured, dict):
            configured = {}

        if effect and effect.effect_type == EffectType.CONTROL_APPLY:
            defaults = {
                "bonus_per_fail_bps": 700,
                "penalty_per_success_bps": 1200,
                "min_rate_bps": 1000,
                "max_rate_bps": 6500,
                "guarantee_count": 6,
            }
        elif params.get("heal_max_troop_bps") is not None and params.get("heal_source_intelligence_bps") is not None:
            defaults = {
                "bonus_per_fail_bps": 800,
                "penalty_per_success_bps": 600,
                "min_rate_bps": 2000,
                "max_rate_bps": 7000,
                "guarantee_count": 5,
            }
        elif base_rate_bps == 3500:
            defaults = {
                "bonus_per_fail_bps": 1200,
                "penalty_per_success_bps": 800,
                "min_rate_bps": 1500,
                "max_rate_bps": 7500,
                "guarantee_count": 4,
            }
        else:
            defaults = {
                "bonus_per_fail_bps": 0,
                "penalty_per_success_bps": 0,
                "min_rate_bps": 0,
                "max_rate_bps": 10000,
                "guarantee_count": 0,
            }
        for key in list(defaults):
            if key in configured:
                defaults[key] = int(configured[key])
        return defaults

    @staticmethod
    def _clamp_bps(value: int, min_value: int, max_value: int) -> int:
        return max(min_value, min(max_value, value))

    def select_targets(
        self,
        actor: Hero,
        target_policy: TargetPolicy,
        target_count: int,
        runtime_cache: dict[str, Any] | None = None,
    ) -> list[Hero]:
        runtime_cache = runtime_cache or {}
        enemy_team_id = self.get_enemy_team_id(actor.team_id)
        exclude_target_ids = set(runtime_cache.get("exclude_target_ids", []))
        candidates: list[Hero]
        rng_index: int | None = None
        if target_policy == TargetPolicy.SELF:
            candidates = [actor] if actor.is_alive() else []
            targets = candidates[:target_count]
        elif target_policy in (TargetPolicy.ALLY, TargetPolicy.RANDOM_ALLY):
            candidates = [hero for hero in self.get_alive_heroes(actor.team_id) if hero.instance_id not in exclude_target_ids]
            targets, rng_index = self._select_random(candidates, target_count, actor, target_policy)
        elif target_policy == TargetPolicy.ALLY_ALL:
            targets = [hero for hero in self.get_alive_heroes(actor.team_id) if hero.instance_id not in exclude_target_ids][:target_count]
        elif target_policy == TargetPolicy.ALLY_LOWEST_TROOPS:
            targets = self._select_by_troop_ratio([hero for hero in self.get_alive_heroes(actor.team_id) if hero.instance_id not in exclude_target_ids], target_count, low=True)
        elif target_policy == TargetPolicy.ALLY_HIGHEST_TROOPS:
            targets = self._select_by_troop_ratio([hero for hero in self.get_alive_heroes(actor.team_id) if hero.instance_id not in exclude_target_ids], target_count, low=False)
        elif target_policy in (TargetPolicy.ENEMY, TargetPolicy.RANDOM_ENEMY):
            candidates = [hero for hero in self.get_alive_heroes(enemy_team_id) if hero.instance_id not in exclude_target_ids]
            targets, rng_index = self._select_random(
                candidates, target_count, actor, target_policy, weight_by_hit_rate=True
            )
        elif target_policy == TargetPolicy.ENEMY_ALL:
            targets = [hero for hero in self.get_alive_heroes(enemy_team_id) if hero.instance_id not in exclude_target_ids][:target_count]
        elif target_policy == TargetPolicy.ENEMY_LOWEST_TROOPS:
            targets = self._select_by_troop_ratio([hero for hero in self.get_alive_heroes(enemy_team_id) if hero.instance_id not in exclude_target_ids], target_count, low=True)
        elif target_policy == TargetPolicy.ENEMY_HIGHEST_TROOPS:
            targets = self._select_by_troop_ratio([hero for hero in self.get_alive_heroes(enemy_team_id) if hero.instance_id not in exclude_target_ids], target_count, low=False)
        elif target_policy == TargetPolicy.SAME_AS_PREVIOUS_EFFECT:
            targets = list(runtime_cache.get("previous_effect_targets", []))[:target_count]
            if not targets:
                targets = self.select_targets(actor, TargetPolicy.RANDOM_ENEMY, target_count, runtime_cache)
        elif target_policy == TargetPolicy.SAME_AS_SOURCE_EVENT:
            targets = []
            for target_id in runtime_cache.get("source_event_target_ids", [])[:target_count]:
                target = self.heroes.get(target_id)
                if target is not None:
                    targets.append(target)
        else:
            targets = []

        self.emit_event(
            EventType.TARGET_SELECTED,
            actor_id=actor.instance_id,
            target_ids=[target.instance_id for target in targets],
            source_type="TARGET_POLICY",
            source_id=target_policy.value,
            rng_index=rng_index,
            payload={"target_policy": target_policy.value, "target_count": target_count},
        )
        return targets

    def _select_random(
        self,
        candidates: list[Hero],
        target_count: int,
        actor: Hero,
        target_policy: TargetPolicy,
        *,
        weight_by_hit_rate: bool = False,
    ) -> tuple[list[Hero], int | None]:
        if not candidates:
            return [], None
        ordered = sorted(candidates, key=lambda hero: (hero.position, hero.instance_id))
        selected_targets: list[Hero] = []
        last_rng_index: int | None = None
        pool = list(ordered)
        for pick_index in range(min(target_count, len(pool))):
            if weight_by_hit_rate:
                weights = [hero.realtime_hit_rate_bps for hero in pool]
                rng_index, selected = self.rng.rand_weighted_index(
                    weights,
                    actor.instance_id,
                    f"select_{target_policy.value}_{pick_index}",
                )
                chosen = pool[selected]
                weight_desc = format_target_pool_hit_rate_weights(pool)
                pick_label = f"第{pick_index + 1}选" if target_count > 1 else "选中"
                self.log(
                    f"[选人·{target_policy.value}] {actor.name} {pick_label} "
                    f"候选权重: {weight_desc} → {chosen.name}"
                )
            else:
                rng_index, selected = self.rng.rand_index(
                    len(pool), actor.instance_id, f"select_{target_policy.value}"
                )
            last_rng_index = rng_index
            selected_targets.append(pool.pop(selected))
        return selected_targets, last_rng_index

    @staticmethod
    def _select_by_troop_ratio(candidates: list[Hero], target_count: int, *, low: bool) -> list[Hero]:
        ordered = sorted(
            candidates,
            key=lambda hero: (
                hero.troops * 10000 // hero.max_troops,
                hero.position,
                hero.instance_id,
            ),
            reverse=not low,
        )
        return ordered[:target_count]

    def run_battle(self) -> BattleSummary:
        # 主循环伪代码见 DESIGN_V2.md 第四节，实现须与文档保持同步。
        self.emit_event(EventType.BATTLE_STARTED, payload={"config_version": self.config_version, "seed": self.seed})
        self.dispatch_events()
        self.rebuild_indexes()
        self.run_timing(Timing.BATTLE_START)
        self.run_timing(Timing.HIT_RATE_INIT)
        self.run_timing(Timing.PREPARE)
        self.check_battle_finish()

        for round_no in range(1, self.max_rounds + 1):
            if self.battle_finished:
                break
            self.round_no = round_no
            self.reset_round_counters()
            self.prepare_round_action_order(round_no)
            round_payload = build_action_order_payload(
                self,
                round_no,
                self.speed_order,
                merge_decisions=self.round_merge_decisions.get(round_no),
            )
            self.emit_event(EventType.ROUND_STARTED, payload=round_payload)
            self.dispatch_events()
            self.run_timing(Timing.ROUND_START)
            if self.battle_finished:
                break
            for hero_id in list(self.speed_order):
                if self.battle_finished:
                    break
                actor = self.heroes[hero_id]
                if actor.exited or not actor.is_alive():
                    continue
                self.current_actor_id = actor.instance_id
                for timing in (
                    Timing.BEFORE_ACTION,
                    Timing.ACTIVE,
                    Timing.BASIC,
                    Timing.AFTER_ACTION,
                ):
                    if self.battle_finished or actor.exited or not actor.is_alive():
                        break
                    self.run_timing(timing, actor)
                    self.check_battle_finish()
            if self.battle_finished:
                break
            self.run_timing(Timing.ROUND_END)
            self.tick_states(Timing.ROUND_END)
            self.emit_event(EventType.ROUND_ENDED)
            self.dispatch_events()
            self.check_battle_finish()

        if not self.battle_finished:
            self.finish_by_remaining_troops()
        self.run_timing(Timing.BATTLE_END)
        self.emit_event(
            EventType.BATTLE_FINISHED,
            payload={
                "result": self.battle_result.value,
                "winner_team_id": self.winner_team_id,
                "finish_reason": self.finish_reason,
            },
        )
        self.dispatch_events()
        return self.summary()

    def run_timing(self, timing: Timing, actor: Hero | None = None) -> None:
        if self.battle_finished and timing != Timing.BATTLE_END:
            return
        self.current_timing = timing
        self.emit_event(EventType.TIMING_STARTED, actor_id=actor.instance_id if actor else None)
        self.dispatch_events()
        if timing == Timing.ROUND_START:
            self._apply_wounded_to_dead_at_round_start()
        if timing == Timing.HIT_RATE_INIT:
            self._init_team_hit_rates()
        if timing == Timing.BEFORE_ACTION and actor is not None:
            self.tick_states_before_actor_action(actor)
            self.dispatch_events()
            if self.check_battle_finish():
                return
        regular_group = find_regular_group(self.config_db.regular_groups, timing)
        eligible_regular_states: list[State] = []
        for state_id in list(self.regular_state_timing_index.get(timing, [])):
            state = self.state_instances.get(state_id)
            if state is None or not self.is_state_battle_active(state):
                continue
            if actor is not None and state.owner.instance_id != actor.instance_id:
                continue
            eligible_regular_states.append(state)
        ordered_regular_states = sort_regular_states_for_dispatch(
            eligible_regular_states,
            primary_group=regular_group,
            regular_groups=self.config_db.regular_groups,
            unconfigured_sort=self.config_db.state_unconfigured_sort,
        )
        for state in ordered_regular_states:
            self.try_trigger_triggerable(state, timing)
            self.dispatch_events()
            if self.check_battle_finish():
                break
        if not self.battle_finished or timing == Timing.BATTLE_END:
            handled_preparation_skill_ids: set[str] = set()
            if timing == Timing.ACTIVE and actor is not None:
                handled_preparation_skill_ids = self._advance_all_active_preparing(actor)
                self.dispatch_events()
                if self.check_battle_finish():
                    return
            for skill_id in list(self.skill_timing_index.get(timing, [])):
                skill = self.skill_instances[skill_id]
                if skill.owner.exited or not skill.enabled_flag:
                    continue
                if actor is not None and skill.owner.instance_id != actor.instance_id:
                    continue
                if actor is None and timing not in GLOBAL_TIMINGS:
                    continue
                if timing == Timing.ACTIVE and self._should_skip_preparation_skill_trigger(
                    skill, handled_preparation_skill_ids
                ):
                    continue
                self.try_trigger_triggerable(skill, timing)
                self.dispatch_events()
                if self.check_battle_finish():
                    break
        if timing == Timing.ROUND_START:
            self._log_round_start_effective_attrs()
        self.emit_event(EventType.TIMING_ENDED, actor_id=actor.instance_id if actor else None)
        self.dispatch_events()

    def try_trigger_triggerable(
        self, triggerable: Triggerable, timing: Timing, source_event: BattleEvent | None = None
    ) -> bool:
        check = triggerable.can_trigger_at(self, timing, source_event)
        self.emit_event(
            EventType.PRE_TRIGGER_CHECK,
            actor_id=triggerable.owner.instance_id,
            source_type=triggerable.triggerable_type.value,
            source_id=triggerable.config_id,
            skill_id=triggerable.config_id if isinstance(triggerable, Skill) else None,
            state_instance_id=triggerable.instance_id if isinstance(triggerable, State) else None,
            payload={"allowed": check.allowed, "reason": check.reason},
        )
        if not check.allowed:
            triggerable.record_trigger_fail(self, timing, check, source_event)
            self.emit_event(
                EventType.TRIGGER_FAIL,
                actor_id=triggerable.owner.instance_id,
                source_type=triggerable.triggerable_type.value,
                source_id=triggerable.config_id,
                skill_id=triggerable.config_id if isinstance(triggerable, Skill) else None,
                state_instance_id=triggerable.instance_id if isinstance(triggerable, State) else None,
                payload={
                    "reason": check.reason,
                    "failure_kind": self._trigger_failure_kind(check.reason),
                    "failed_timing": timing.value,
                },
            )
            self.log(f"{triggerable.owner.name} 触发 {triggerable.name} 失败，原因={check.reason}")
            return False
        if isinstance(triggerable, State) and source_event is not None:
            skip = triggerable.invalid_target_reason(self, source_event)
            if skip is not None:
                self.log_state_skipped_invalid_target(triggerable, **skip)
                return False
        probability = triggerable.roll_probability(self, source_event=source_event)
        if not probability.allowed:
            triggerable.record_trigger_fail(self, timing, probability, source_event)
            self.emit_event(
                EventType.TRIGGER_FAIL,
                actor_id=triggerable.owner.instance_id,
                source_type=triggerable.triggerable_type.value,
                source_id=triggerable.config_id,
                skill_id=triggerable.config_id if isinstance(triggerable, Skill) else None,
                state_instance_id=triggerable.instance_id if isinstance(triggerable, State) else None,
                rng_index=probability.rng_index,
                payload={
                    "reason": probability.reason,
                    "roll_bps": probability.roll_bps,
                    "threshold_bps": probability.threshold_bps,
                    "failure_kind": "PROBABILITY",
                    "failed_timing": timing.value,
                    **self._probability_payload(probability),
                },
            )
            self.log(
                f"{triggerable.owner.name} 触发 {triggerable.name} 失败，"
                f"{self._format_probability(probability, success=False)}"
            )
            return False
        if isinstance(triggerable, Skill) and triggerable.is_preparation_active():
            return self._try_trigger_preparation_active(triggerable, timing, probability, source_event)
        triggerable.record_trigger_success(self, timing, source_event)
        if isinstance(triggerable, Skill):
            self.emit_skill_signal(triggerable, "BEFORE", source_event=source_event)
            if self.check_battle_finish():
                return False
        self.emit_event(
            EventType.TRIGGER_SUCCESS,
            actor_id=triggerable.owner.instance_id,
            source_type=triggerable.triggerable_type.value,
            source_id=triggerable.config_id,
            skill_id=triggerable.config_id if isinstance(triggerable, Skill) else None,
            state_instance_id=triggerable.instance_id if isinstance(triggerable, State) else None,
            rng_index=probability.rng_index,
            payload={
                "roll_bps": probability.roll_bps,
                "threshold_bps": probability.threshold_bps,
                **self._probability_payload(probability),
            },
        )
        self.log(
            f"{triggerable.owner.name} 触发 {triggerable.name} 成功，"
            f"{self._format_probability(probability, success=True)}"
        )
        if isinstance(triggerable, Skill):
            self.emit_skill_signal(triggerable, "ON", source_event=source_event)
            if self.check_battle_finish():
                return False
            self.execute_skill(triggerable, source_event)
            self.emit_skill_signal(triggerable, "AFTER", source_event=source_event)
        else:
            triggerable.execute(self, source_event)
        self.emit_post_trigger(triggerable)
        self.check_battle_finish()
        return True

    def emit_post_trigger(
        self,
        triggerable: Triggerable,
        *,
        trigger_phase: str | None = None,
        effective: bool = True,
    ) -> None:
        """发出一次触发后收尾事件 POST_TRIGGER。

        即时战法：effective=True，无 trigger_phase，表示本次触发流水线已完整结束。

        准备型主动战法分两段：
        - 进入准备：trigger_phase=PREPARE，effective=False（仅表示 prepare effects 跑完，战法尚未算「发动完成」）。
        - 释放：trigger_phase=RELEASE，effective=True（与即时战法等价，连锁监听应只认这一次）。

        监听方若只关心「战法完整发动结束」，应检查 effective=True；
        若区分准备段/释放段，再结合 trigger_phase。
        """
        payload: dict[str, Any] = {"effective": effective}
        if trigger_phase is not None:
            payload["trigger_phase"] = trigger_phase
        skill_id = triggerable.config_id if isinstance(triggerable, Skill) else None
        self.emit_event(
            EventType.POST_TRIGGER,
            actor_id=triggerable.owner.instance_id,
            source_type=triggerable.triggerable_type.value,
            source_id=triggerable.config_id,
            skill_id=skill_id,
            payload=payload,
        )

    def _try_trigger_preparation_active(
        self,
        skill: Skill,
        timing: Timing,
        probability: TriggerCheckResult,
        source_event: BattleEvent | None,
    ) -> bool:
        """准备型主动战法：概率成功后的「进入准备」段。

        事件与连锁监听约定（相对即时战法）：
        - BEFORE_ACTIVE_SIGNAL：仅发 trigger_phase=PREPARE；常规「发动前」被动应只响应此段。
        - TRIGGER_SUCCESS：payload.phase=PREPARE（含 roll 细节）；概率成功监听默认认此事件。
        - 不发 ACTIVE_SIGNAL / AFTER_ACTIVE_SIGNAL（释放段才有）。
        - POST_TRIGGER：effective=False，表示 prepare effects 已结束，但战法未完整发动。
        - 不 record_trigger_success；success_count 仅在释放段增加。
        """
        prepare_rounds = int(skill.params.get("prepare_rounds", 1))
        state_config = self.config_db.state_configs.get(skill.prepare_state_config_id())
        state_name = state_config.name if state_config else "准备"
        self.emit_skill_signal(skill, "BEFORE", source_event, trigger_phase="PREPARE")
        if self.check_battle_finish():
            return False
        self.emit_event(
            EventType.TRIGGER_SUCCESS,
            actor_id=skill.owner.instance_id,
            source_type=TriggerableType.SKILL.value,
            source_id=skill.config_id,
            skill_id=skill.config_id,
            rng_index=probability.rng_index,
            payload={
                "phase": "PREPARE",
                "roll_bps": probability.roll_bps,
                "threshold_bps": probability.threshold_bps,
                **self._probability_payload(probability),
            },
        )
        self.log(
            f"{skill.owner.name} 的战法 {skill.name} 概率成功，进入【{state_name}】（0/{prepare_rounds}），"
            f"{self._format_probability(probability, success=True)}"
        )
        self.execute_skill(skill, source_event, effect_ids=skill.prepare_effect_ids())
        preparing = Skill.find_active_preparing_state(skill.owner, skill.config_id)
        if preparing is not None:
            preparing.payload["prepare_ticks"] = 0
            preparing.payload["prepare_rounds"] = prepare_rounds
            preparing.payload["source_skill_id"] = skill.config_id
        self.emit_post_trigger(skill, trigger_phase="PREPARE", effective=False)
        self.check_battle_finish()
        return True

    def _should_skip_preparation_skill_trigger(
        self, skill: Skill, handled_preparation_skill_ids: set[str]
    ) -> bool:
        """准备型战法在 ACTIVE 的占用规则。

        - 身上已有该战法准备 state：本轮 ACTIVE 只由 _advance_all_active_preparing 推进/释放，
          不再走 try_trigger_triggerable（无 PRE_TRIGGER / roll / TRIGGER_FAIL）。
        - 本轮刚完成推进或释放：该战法时间片已用完，同一 ACTIVE 内不得再次进入准备。
        """
        if not skill.is_preparation_active():
            return False
        if skill.config_id in handled_preparation_skill_ids:
            return True
        return Skill.find_active_preparing_state(skill.owner, skill.config_id) is not None

    def _advance_all_active_preparing(self, actor: Hero) -> set[str]:
        """推进该英雄身上所有准备型战法的独立吟诵状态，并在进度满时释放。

        每个准备战法拥有独立 state（payload.source_skill_id 区分），互不阻塞其他战法的 ACTIVE 判定。
        返回本轮 ACTIVE 已占用时间片的准备战法 skill_id 集合（含仅 tick 与已释放）。
        """
        handled_skill_ids: set[str] = set()
        preparing_states = sorted(
            list(Skill.iter_active_preparing_states(actor)),
            key=lambda state: str(state.payload.get("source_skill_id", "")),
        )
        for preparing in preparing_states:
            source_skill_id = self._advance_one_active_preparing(actor, preparing)
            if source_skill_id:
                handled_skill_ids.add(source_skill_id)
            if self.battle_finished:
                break
        return handled_skill_ids

    def _advance_one_active_preparing(self, actor: Hero, preparing: State) -> str:
        source_skill_id = str(preparing.payload.get("source_skill_id", ""))
        prepare_rounds = int(preparing.payload.get("prepare_rounds", 1))
        ticks = int(preparing.payload.get("prepare_ticks", 0)) + 1
        preparing.payload["prepare_ticks"] = ticks
        skill_config = self.config_db.skill_configs.get(source_skill_id)
        skill_name = skill_config.name if skill_config else source_skill_id
        self.log(
            f"{actor.name} 的战法 {skill_name}【{preparing.name}】进度 {ticks}/{prepare_rounds}"
        )

        if ticks < prepare_rounds:
            return source_skill_id

        skill = self._find_skill_instance(actor.instance_id, source_skill_id)
        self.remove_state(actor, preparing.instance_id, "PREPARE_COMPLETE")
        if skill is None:
            self.log(f"{actor.name} 的准备战法无法释放，缺少技能实例 {source_skill_id}")
            return source_skill_id
        self._release_preparation_skill(skill)
        return source_skill_id

    def _release_preparation_skill(self, skill: Skill) -> None:
        """准备型主动战法：吟诵进度满后的「释放」段。

        事件与连锁监听约定：
        - BEFORE / ACTIVE / AFTER 信号均带 trigger_phase=RELEASE。
        - TRIGGER_SUCCESS：payload.phase=RELEASE（无 roll，非概率点）。
        - POST_TRIGGER：effective=True，表示战法完整发动结束；常规监听只认这一次。
        """
        timing = self.current_timing or Timing.ACTIVE
        skill.record_trigger_success(self, timing, None)
        self.emit_skill_signal(skill, "BEFORE", trigger_phase="RELEASE")
        if self.check_battle_finish():
            return
        self.emit_event(
            EventType.TRIGGER_SUCCESS,
            actor_id=skill.owner.instance_id,
            source_type=TriggerableType.SKILL.value,
            source_id=skill.config_id,
            skill_id=skill.config_id,
            payload={"phase": "RELEASE"},
        )
        self.log(f"{skill.owner.name} 的战法 {skill.name} 准备完成，释放")
        self.emit_skill_signal(skill, "ON", trigger_phase="RELEASE")
        if self.check_battle_finish():
            return
        self.execute_skill(skill, None, effect_ids=skill.release_effect_ids())
        self.emit_skill_signal(skill, "AFTER", trigger_phase="RELEASE")
        self.emit_post_trigger(skill, trigger_phase="RELEASE", effective=True)
        self.check_battle_finish()

    def _find_skill_instance(self, owner_id: str, skill_config_id: str) -> Skill | None:
        for skill in self.skill_instances.values():
            if skill.owner.instance_id == owner_id and skill.config_id == skill_config_id:
                return skill
        return None

    def _interrupt_active_preparing(self, target: Hero, *, reason: str) -> None:
        """控制打断：移除目标身上所有独立准备 state（各战法互不影响判定，但同受 forbid_active）。"""
        for state in list(Skill.iter_active_preparing_states(target)):
            source_skill_id = str(state.payload.get("source_skill_id", ""))
            skill_config = self.config_db.skill_configs.get(source_skill_id)
            skill_name = skill_config.name if skill_config else state.name
            self.remove_state(target, state.instance_id, reason)
            self.log(f"{target.name} 的战法 {skill_name} 已被打断")

    def emit_skill_signal(
        self,
        skill: Skill,
        phase: str,
        source_event: BattleEvent | None = None,
        *,
        trigger_phase: str | None = None,
    ) -> BattleEvent | None:
        """发射战法连锁信号（BEFORE_* / *_SIGNAL / AFTER_*）。

        phase 为信号子阶段：BEFORE / ON / AFTER。
        trigger_phase 仅准备型主动战法使用：
        - PREPARE：进入准备段；常规监听 BEFORE_ACTIVE_SIGNAL 时只响应 trigger_phase=PREPARE。
        - RELEASE：释放段；ACTIVE_SIGNAL / AFTER_ACTIVE_SIGNAL 仅在此阶段出现，监听方只响应 RELEASE。
        即时战法不传 trigger_phase。
        """
        event_type = self._skill_signal_event_type(skill.category, phase)
        if event_type is None:
            return None
        payload: dict[str, Any] = {
            "phase": phase,
            "skill_category": skill.category.value,
            "skill_instance_id": skill.instance_id,
            "source_event_id": source_event.event_id if source_event else None,
        }
        if trigger_phase is not None:
            payload["trigger_phase"] = trigger_phase
        event = self.emit_event(
            event_type,
            actor_id=skill.owner.instance_id,
            source_type=TriggerableType.SKILL.value,
            source_id=skill.config_id,
            skill_id=skill.config_id,
            payload=payload,
        )
        self.dispatch_events()
        return event

    @staticmethod
    def _skill_signal_event_type(category: SkillCategory, phase: str) -> EventType | None:
        signal_map: dict[tuple[SkillCategory, str], EventType] = {
            (SkillCategory.BASIC, "BEFORE"): EventType.BEFORE_BASIC_SIGNAL,
            (SkillCategory.BASIC, "ON"): EventType.BASIC_SIGNAL,
            (SkillCategory.BASIC, "AFTER"): EventType.AFTER_BASIC_SIGNAL,
            (SkillCategory.ACTIVE, "BEFORE"): EventType.BEFORE_ACTIVE_SIGNAL,
            (SkillCategory.ACTIVE, "ON"): EventType.ACTIVE_SIGNAL,
            (SkillCategory.ACTIVE, "AFTER"): EventType.AFTER_ACTIVE_SIGNAL,
            (SkillCategory.PURSUIT, "BEFORE"): EventType.BEFORE_PURSUIT_SIGNAL,
            (SkillCategory.PURSUIT, "ON"): EventType.PURSUIT_SIGNAL,
            (SkillCategory.PURSUIT, "AFTER"): EventType.AFTER_PURSUIT_SIGNAL,
        }
        return signal_map.get((category, phase))

    @staticmethod
    def _trigger_failure_kind(reason: str) -> str:
        if reason.startswith("CONTROL_"):
            return "CONTROL"
        if reason == "PROBABILITY_FAIL":
            return "PROBABILITY"
        return "RULE"

    @staticmethod
    def _probability_payload(result: TriggerCheckResult) -> dict[str, Any]:
        return {
            "reason": result.reason,
            "pseudo_random_key": result.pseudo_random_key,
            "base_rate_bps": result.base_rate_bps,
            "current_rate_bps": result.current_rate_bps,
            "fail_count": result.fail_count,
            "success_streak": result.success_streak,
            "guarantee_triggered": result.guarantee_triggered,
        }

    @staticmethod
    def _format_probability(result: TriggerCheckResult, *, success: bool) -> str:
        if result.reason == "ALWAYS_TRIGGER":
            return "reason=ALWAYS_TRIGGER"
        if result.guarantee_triggered:
            verdict = "reason=GUARANTEE_TRIGGER"
        else:
            operator = "<" if success else ">="
            verdict = f"roll={result.roll_bps} {operator} {result.current_rate_bps}"
        return (
            f"概率 {verdict}，base={result.base_rate_bps}，current={result.current_rate_bps}，"
            f"failCount={result.fail_count}，successStreak={result.success_streak}，"
            f"key={result.pseudo_random_key}"
        )

    def roll_random_coef_bps(self, source: str, reason: str) -> tuple[int, int]:
        """生成 0.95 到 1.05 的确定性随机系数。

        伤害 / 治疗公式需要轻微浮动，但 BattleCore 必须可复现。
        因此这里不使用 Python random，而是复用 DeterministicRNG。
        返回值是 (rng_index, random_coef_bps)，其中 10000 表示 1.0。
        """
        rng_index, offset = self.rng.rand_index(1001, source, reason)
        return rng_index, RANDOM_COEF_MIN_BPS + offset

    def execute_skill(
        self,
        skill: Skill,
        source_event: BattleEvent | None = None,
        *,
        effect_ids: list[str] | None = None,
    ) -> None:
        actor = skill.choose_actor(self)
        skill.execution_seq += 1
        skill_execution_id = f"{skill.instance_id}#{skill.execution_seq}"
        runtime_cache: dict[str, Any] = {
            "effect_targets_by_id": {},
            "effect_targets_by_config_id": {},
            "effect_targets_by_alias": {},
        }
        if source_event and source_event.target_ids:
            runtime_cache["source_event_target_ids"] = list(source_event.target_ids)
        effects = skill.effects
        if effect_ids is not None:
            allowed = set(effect_ids)
            effects = [effect for effect in skill.effects if effect.config_id in allowed]
        for effect in effects:
            if self.battle_finished:
                break
            record: dict[str, Any] = {
                "skill_execution_id": skill_execution_id,
                "round_no": self.round_no,
                "timing": self.current_timing.value if self.current_timing else None,
                "effect_instance_id": effect.instance_id,
                "effect_id": effect.config_id,
                "effect_name": effect.name,
                "status": "PENDING",
                "reason": None,
                "selected_target_ids": [],
                "executed_target_ids": [],
                "roll_bps": None,
                "threshold_bps": None,
                "rng_index": None,
                "pseudo_random_key": None,
                "base_rate_bps": None,
                "current_rate_bps": None,
                "fail_count": 0,
                "success_streak": 0,
                "guarantee_triggered": False,
            }
            skill.effect_execution_records.append(record)
            self.emit_event(
                EventType.PRE_EFFECT_CHECK,
                actor_id=actor.instance_id,
                source_type=TriggerableType.SKILL.value,
                source_id=skill.config_id,
                skill_id=skill.config_id,
                effect_id=effect.config_id,
            )
            enabled = effect.enabled(self)
            if not enabled.allowed:
                effect.record_fail(enabled.reason)
                record.update({"status": "FAILED", "reason": enabled.reason})
                self.log(f"{actor.name} 的效果 {effect.name} 未执行，原因={enabled.reason}")
                self.emit_event(
                    EventType.EFFECT_CHECK_FAIL,
                    actor_id=actor.instance_id,
                    effect_id=effect.config_id,
                    payload={"reason": enabled.reason},
                )
                continue
            targets = self.resolve_effect_targets(actor, effect, runtime_cache)
            record["selected_target_ids"] = [target.instance_id for target in targets]
            if not targets:
                effect.record_fail("NO_TARGET")
                record.update({"status": "FAILED", "reason": "NO_TARGET"})
                self.log(f"{actor.name} 的效果 {effect.name} 未执行，原因=没有可用目标")
                self.emit_event(
                    EventType.EFFECT_CHECK_FAIL,
                    actor_id=actor.instance_id,
                    skill_id=skill.config_id,
                    effect_id=effect.config_id,
                    payload={"reason": "NO_TARGET"},
                )
                continue
            valid_targets, invalid_hero, invalid_reason = self._validate_effect_targets(targets)
            if not valid_targets:
                effect.record_fail("INVALID_TARGET")
                record.update({"status": "FAILED", "reason": "INVALID_TARGET"})
                self.log_effect_skipped_invalid_target(
                    actor,
                    effect,
                    reason=invalid_reason or "目标无效",
                    target_name=invalid_hero.name if invalid_hero is not None else "?",
                )
                self.emit_event(
                    EventType.EFFECT_CHECK_FAIL,
                    actor_id=actor.instance_id,
                    skill_id=skill.config_id,
                    effect_id=effect.config_id,
                    payload={"reason": "INVALID_TARGET", "detail": invalid_reason},
                )
                continue
            targets = valid_targets
            probability = effect.roll_probability(self, actor, targets)
            record.update(
                {
                    "roll_bps": probability.roll_bps,
                    "threshold_bps": probability.threshold_bps,
                    "rng_index": probability.rng_index,
                    "pseudo_random_key": probability.pseudo_random_key,
                    "base_rate_bps": probability.base_rate_bps,
                    "current_rate_bps": probability.current_rate_bps,
                    "fail_count": probability.fail_count,
                    "success_streak": probability.success_streak,
                    "guarantee_triggered": probability.guarantee_triggered,
                }
            )
            if not probability.allowed:
                effect.record_fail(probability.reason)
                record.update({"status": "FAILED", "reason": probability.reason})
                self.log(
                    f"{actor.name} 的效果 {effect.name} 概率失败，"
                    f"{self._format_probability(probability, success=False)}"
                )
                self.emit_event(
                    EventType.EFFECT_CHECK_FAIL,
                    actor_id=actor.instance_id,
                    target_ids=[target.instance_id for target in targets],
                    skill_id=skill.config_id,
                    effect_id=effect.config_id,
                    rng_index=probability.rng_index,
                    payload={
                        "reason": probability.reason,
                        "roll_bps": probability.roll_bps,
                        "threshold_bps": probability.threshold_bps,
                        **self._probability_payload(probability),
                    },
                )
                continue
            effect.record_success()
            record.update({"status": "CHECK_SUCCESS", "reason": "OK"})
            self.log(
                f"{actor.name} 的效果 {effect.name} 概率成功，"
                f"{self._format_probability(probability, success=True)}"
            )
            self.emit_event(
                EventType.EFFECT_CHECK_SUCCESS,
                actor_id=actor.instance_id,
                target_ids=[target.instance_id for target in targets],
                skill_id=skill.config_id,
                effect_id=effect.config_id,
                rng_index=probability.rng_index,
                payload={
                    "roll_bps": probability.roll_bps,
                    "threshold_bps": probability.threshold_bps,
                    **self._probability_payload(probability),
                },
            )
            self.emit_event(EventType.PRE_EFFECT_EXECUTE, actor_id=actor.instance_id, target_ids=[target.instance_id for target in targets], skill_id=skill.config_id, effect_id=effect.config_id)
            self.execute_effect(effect, actor, targets)
            runtime_cache["previous_effect_targets"] = targets
            runtime_cache["effect_targets_by_id"][effect.instance_id] = targets
            runtime_cache["effect_targets_by_config_id"][effect.config_id] = targets
            if alias := effect.params.get("store_targets_as"):
                runtime_cache["effect_targets_by_alias"][str(alias)] = targets
            record.update(
                {
                    "status": "EXECUTED",
                    "executed_target_ids": [target.instance_id for target in targets if not target.exited or target.instance_id in record["selected_target_ids"]],
                    "effect_success_count": effect.success_count,
                    "effect_fail_count": effect.fail_count,
                    "total_damage": effect.total_damage,
                    "total_heal": effect.total_heal,
                    "applied_state_count": effect.applied_state_count,
                }
            )
            self.emit_event(
                EventType.POST_EFFECT_EXECUTE,
                actor_id=actor.instance_id,
                target_ids=[target.instance_id for target in targets],
                skill_id=skill.config_id,
                effect_id=effect.config_id,
            )
            # Effect 是最小原子结算单位。伤害 / 治疗 / 状态施加在这里落地后，
            # 立即派发本 effect 产生的事件，让 SPY 状态在下一段 effect 之前响应。
            # 例如 Gorgon Damage 1 结算后，应先触发蛇杖 / 雷霆，再进入 Ming Lock 1。
            self.dispatch_events()
            self.check_battle_finish()

    def resolve_effect_targets(self, actor: Hero, effect: Effect, runtime_cache: dict[str, Any]) -> list[Hero]:
        alias = effect.params.get("target_from_effect_alias")
        if alias:
            return list(runtime_cache.get("effect_targets_by_alias", {}).get(str(alias), []))[: effect.target_count]

        config_id = effect.params.get("target_from_effect_id")
        if config_id:
            return list(runtime_cache.get("effect_targets_by_config_id", {}).get(str(config_id), []))[
                : effect.target_count
            ]

        excluded: set[str] = set()
        for excluded_alias in effect.params.get("exclude_effect_aliases", []):
            excluded.update(
                target.instance_id
                for target in runtime_cache.get("effect_targets_by_alias", {}).get(str(excluded_alias), [])
            )
        runtime_cache["exclude_target_ids"] = excluded
        try:
            return self.select_targets(actor, effect.target_policy, effect.target_count, runtime_cache)
        finally:
            runtime_cache.pop("exclude_target_ids", None)

    def execute_effect(self, effect: Effect, actor: Hero, targets: list[Hero]) -> None:
        effect.execute(self, actor, targets)

    def apply_damage(
        self,
        actor: Hero,
        target: Hero,
        amount: int,
        damage_type: DamageType,
        skill: Skill | None = None,
        effect: Effect | None = None,
        state: State | None = None,
    ) -> None:
        if self.battle_finished or target.exited:
            return
        final_damage = amount
        random_rng_index: int | None = None
        random_coef_bps: int | None = None
        crit_result: TriggerCheckResult | None = None
        crit_multiplier_bps = 10000
        is_crit = False
        if effect is not None:
            random_rng_index, random_coef_bps = self.roll_random_coef_bps(
                effect.instance_id,
                "damage_random_coef",
            )
            crit_result = self.roll_damage_crit(actor, target, effect=effect, skill=skill)
            is_crit = crit_result.allowed
            crit_multiplier_bps = CRIT_DAMAGE_MULTIPLIER_BPS if is_crit else 10000
            final_damage = calc_damage(
                caster=actor,
                target=target,
                damage_type=damage_type,
                skill_rate_bps=effect.coefficient_bps,
                ignore_troop_coef=bool(effect.params.get("ignore_troop_coef", False)),
                restrain_coef_bps=int(effect.params.get("restrain_coef_bps", 10000)),
                random_coef_bps=random_coef_bps,
                fixed_extra_damage=int(effect.params.get("fixed_extra_damage", 0)),
                crit_multiplier_bps=crit_multiplier_bps,
            )
        elif state is not None and "damage_coefficient_bps" in state.payload:
            random_rng_index, random_coef_bps = self.roll_random_coef_bps(
                state.instance_id,
                "state_damage_random_coef",
            )
            crit_result = self.roll_damage_crit(actor, target, state=state)
            is_crit = crit_result.allowed
            crit_multiplier_bps = CRIT_DAMAGE_MULTIPLIER_BPS if is_crit else 10000
            final_damage = calc_damage(
                caster=actor,
                target=target,
                damage_type=damage_type,
                skill_rate_bps=int(state.payload.get("damage_coefficient_bps", 10000)),
                ignore_troop_coef=bool(state.payload.get("ignore_troop_coef", False)),
                restrain_coef_bps=int(state.payload.get("restrain_coef_bps", 10000)),
                random_coef_bps=random_coef_bps,
                fixed_extra_damage=int(state.payload.get("fixed_extra_damage", 0)),
                crit_multiplier_bps=crit_multiplier_bps,
            )
        old_troops = target.current_troop
        old_dead_troop = target.dead_troop
        old_wounded_troop = target.wounded_troop
        damage_result = apply_troop_damage(target, final_damage)
        actual_damage = damage_result["actual_damage"]
        actor.damage_dealt += actual_damage
        target.damage_taken += actual_damage
        if effect is not None:
            effect.total_damage += actual_damage
        # DAMAGE_APPLIED 表示“伤害已经应用到兵力 / 伤兵 / 阵亡池”。
        # 它是战报和表现层最关心的结果事件。
        # 注意：此时还不表示所有“受伤后监听”都已经处理完。
        self.emit_event(
            EventType.DAMAGE_APPLIED,
            actor_id=actor.instance_id,
            target_ids=[target.instance_id],
            source_type=TriggerableType.SKILL.value if skill else TriggerableType.STATE.value if state else None,
            source_id=skill.config_id if skill else state.config_id if state else None,
            skill_id=skill.config_id if skill else None,
            effect_id=effect.config_id if effect else None,
            state_instance_id=state.instance_id if state else None,
            payload={
                "damage": actual_damage,
                "damage_type": damage_type.value,
                "old_troops": old_troops,
                "new_troops": target.troops,
                "dead": damage_result["dead"],
                "wounded": damage_result["wounded"],
                "old_dead_troop": old_dead_troop,
                "new_dead_troop": target.dead_troop,
                "old_wounded_troop": old_wounded_troop,
                "new_wounded_troop": target.wounded_troop,
                "random_rng_index": random_rng_index,
                "random_coef_bps": random_coef_bps,
                "is_crit": is_crit,
                "crit_multiplier_bps": crit_multiplier_bps if is_crit else None,
                "crit_rate_bps": get_effective_crit_rate_bps(actor),
                "crit_roll_bps": crit_result.roll_bps if crit_result else None,
                "crit_threshold_bps": crit_result.threshold_bps if crit_result else None,
                "state_tags": list(state.tags) if state else [],
            },
        )
        crit_suffix = "，暴击" if is_crit else ""
        self.log(
            f"{actor.name} 对 {target.name} 造成 {actual_damage} 点 {damage_type.value} 伤害{crit_suffix}，"
            f"兵力 {old_troops} -> {target.troops}，"
            f"阵亡 +{damage_result['dead']}，伤兵 +{damage_result['wounded']}"
        )
        if target.troops <= 0 and not target.exited:
            self.mark_hero_exited(
                target,
                reason="TROOPS_ZERO",
                killer=actor,
                defer_battle_finish=actual_damage > 0,
            )
        # DAMAGE_SETTLED 是“伤害结算信号”：每次 apply 后都发，payload.damage 为本次实际伤害（可为 0）。
        # DAMAGE_APPLIED 表达兵力已落地；SETTLED 打开 SPY 连锁窗口，各监听方自行按 damage 过滤。
        settled_event = self.emit_event(
            EventType.DAMAGE_SETTLED,
            actor_id=actor.instance_id,
            target_ids=[target.instance_id],
            source_type=TriggerableType.SKILL.value if skill else TriggerableType.STATE.value if state else None,
            source_id=skill.config_id if skill else state.config_id if state else None,
            skill_id=skill.config_id if skill else None,
            effect_id=effect.config_id if effect else None,
            state_instance_id=state.instance_id if state else None,
            payload={
                "damage": actual_damage,
                "damage_type": damage_type.value,
                "dead": damage_result["dead"],
                "wounded": damage_result["wounded"],
                "old_troops": old_troops,
                "new_troops": target.troops,
                "is_crit": is_crit,
                "state_tags": list(state.tags) if state else [],
            },
        )
        if actual_damage > 0:
            self._on_troop_settlement_hit_rate(target, "DAMAGE_SETTLED")
        self.dispatch_events()
        if target.exited and target.role == HeroRole.MAIN and not self.battle_finished:
            self._finish_for_main_hero_exit(target)

    def apply_heal(
        self,
        actor: Hero,
        target: Hero,
        amount: int,
        skill: Skill | None = None,
        effect: Effect | None = None,
        state: State | None = None,
    ) -> None:
        if self.battle_finished or target.exited:
            return
        actual_heal = amount
        random_rng_index: int | None = None
        random_coef_bps: int | None = None
        crit_result: TriggerCheckResult | None = None
        crit_multiplier_bps = 10000
        is_crit = False
        if effect is not None:
            random_rng_index, random_coef_bps = self.roll_random_coef_bps(
                effect.instance_id,
                "heal_random_coef",
            )
            crit_result = self.roll_heal_crit(actor, target, effect=effect, skill=skill)
            is_crit = crit_result.allowed
            crit_multiplier_bps = CRIT_HEAL_MULTIPLIER_BPS if is_crit else 10000
            # calc_heal 主公式内含治疗增减区段，与 effect 路径互斥。
            actual_heal = calc_heal(
                healer=actor,
                target=target,
                heal_rate_bps=effect.coefficient_bps,
                random_coef_bps=random_coef_bps,
                fixed_extra_heal=int(effect.params.get("fixed_extra_heal", 0)),
                crit_multiplier_bps=crit_multiplier_bps,
            )
        elif state is not None and amount > 0:
            crit_result = self.roll_heal_crit(actor, target, skill=None)
            is_crit = crit_result.allowed
            crit_multiplier_bps = CRIT_HEAL_MULTIPLIER_BPS if is_crit else 10000
            skip_modifiers = bool(state.payload.get("skip_heal_modifiers", False))
            actual_heal = apply_heal_settlement_adjustments(
                actor,
                target,
                amount,
                crit_multiplier_bps=crit_multiplier_bps,
                apply_modifiers=not skip_modifiers,
            )
        old_troops = target.current_troop
        old_wounded_troop = target.wounded_troop
        actual_heal = apply_troop_heal(target, actual_heal)
        actor.heal_done += actual_heal
        target.heal_taken += actual_heal
        if effect is not None:
            effect.total_heal += actual_heal
        # HEAL_APPLIED 表示治疗已经应用到 current_troop / wounded_troop。
        # 它是战报展示恢复量的权威事件。
        self.emit_event(
            EventType.HEAL_APPLIED,
            actor_id=actor.instance_id,
            target_ids=[target.instance_id],
            skill_id=skill.config_id if skill else None,
            effect_id=effect.config_id if effect else None,
            payload={
                "heal": actual_heal,
                "old_troops": old_troops,
                "new_troops": target.troops,
                "old_wounded_troop": old_wounded_troop,
                "new_wounded_troop": target.wounded_troop,
                "random_rng_index": random_rng_index,
                "random_coef_bps": random_coef_bps,
                "is_crit": is_crit,
                "crit_multiplier_bps": crit_multiplier_bps if is_crit else None,
                "heal_crit_rate_bps": get_effective_heal_crit_rate_bps(actor),
                "crit_roll_bps": crit_result.roll_bps if crit_result else None,
                "crit_threshold_bps": crit_result.threshold_bps if crit_result else None,
            },
        )
        crit_suffix = "，治疗暴击" if is_crit else ""
        self.log(
            f"{actor.name} 为 {target.name} 恢复 {actual_heal} 点兵力{crit_suffix}，"
            f"兵力 {old_troops} -> {target.troops}，"
            f"伤兵 {old_wounded_troop} -> {target.wounded_troop}"
        )
        if actual_heal > 0:
            # HEAL_SETTLED 是“治疗结算信号”。
            # 后续如果存在“受到治疗后触发”“治疗溢出转护盾”等 SPY 状态，
            # 应监听这个事件，而不是直接耦合到 apply_heal 的内部流程。
            self.emit_event(
                EventType.HEAL_SETTLED,
                actor_id=actor.instance_id,
                target_ids=[target.instance_id],
                source_type=TriggerableType.SKILL.value if skill else TriggerableType.STATE.value if state else None,
                source_id=skill.config_id if skill else state.config_id if state else None,
                skill_id=skill.config_id if skill else None,
                effect_id=effect.config_id if effect else None,
                state_instance_id=state.instance_id if state else None,
                payload={
                    "heal": actual_heal,
                    "old_troops": old_troops,
                    "new_troops": target.troops,
                    "old_wounded_troop": old_wounded_troop,
                    "new_wounded_troop": target.wounded_troop,
                    "is_crit": is_crit,
                },
            )
            self._on_troop_settlement_hit_rate(target, "HEAL_SETTLED")

    @staticmethod
    def _find_state_by_config_id(target: Hero, state_config_id: str) -> State | None:
        for existing in target.states:
            if existing.state_config_id == state_config_id:
                return existing
        return None

    def accumulate_attr_state_payload(
        self,
        actor: Hero,
        target: Hero,
        state_config_id: str,
        delta: dict[str, int],
        *,
        source_state: State | None = None,
    ) -> State | None:
        """在目标身上查找 ATTR 状态并累加 payload；不存在则先施加。"""
        if target.exited:
            return None
        existing = self._find_state_by_config_id(target, state_config_id)
        if existing is None:
            existing = self.add_state(
                actor,
                target,
                state_config_id,
                source_state=source_state,
            )
            if existing is None:
                return None
        for key, value in delta.items():
            existing.payload[key] = int(existing.payload.get(key, 0)) + int(value)
        parts = "，".join(f"{key}{value:+d}" for key, value in sorted(delta.items()))
        self.log(
            f"{actor.name} 调整 {target.name} 的 {existing.name}：{parts}"
            f"（当前={self._format_state_payload_for_log(existing.payload)}）"
        )
        return existing

    def _refresh_existing_state(
        self,
        existing: State,
        *,
        actor: Hero,
        state_config,
        source_skill: Skill | None,
        duration_override: int | None,
        reset_stack: bool,
    ) -> State:
        existing.duration_rounds = duration_override or state_config.duration_rounds
        existing.remaining_rounds = existing.duration_rounds
        existing.action_tick_count = 0
        if reset_stack:
            existing.stack = 1
        else:
            existing.stack = min(existing.max_stack, existing.stack + 1)
        existing.source_actor_id = actor.instance_id
        existing.source_skill_id = source_skill.config_id if source_skill else None
        return existing

    def add_state(
        self,
        actor: Hero,
        target: Hero,
        state_config_id: str,
        source_skill: Skill | None = None,
        source_effect: Effect | None = None,
        source_state: State | None = None,
        duration_override: int | None = None,
    ) -> State | None:
        state_config = self.config_db.state_configs.get(state_config_id)
        if state_config is None or target.exited:
            return None
        refreshed = False
        state: State | None = None
        if state_config.state_type == StateType.CONTROL:
            existing = self._find_state_by_config_id(target, state_config_id)
            if existing is not None:
                state = self._refresh_existing_state(
                    existing,
                    actor=actor,
                    state_config=state_config,
                    source_skill=source_skill,
                    duration_override=duration_override,
                    reset_stack=True,
                )
                refreshed = True
        else:
            for existing in target.states:
                if existing.state_config_id == state_config_id and existing.source_actor_id == actor.instance_id:
                    state = self._refresh_existing_state(
                        existing,
                        actor=actor,
                        state_config=state_config,
                        source_skill=source_skill,
                        duration_override=duration_override,
                        reset_stack=False,
                    )
                    refreshed = True
                    break
        if state is None:
            state = State.from_config(f"state:{self._next_state_seq}", state_config, target)
            self._next_state_seq += 1
            state.duration_rounds = duration_override or state.duration_rounds
            state.remaining_rounds = state.duration_rounds
            state.action_tick_count = 0
            state.source_actor_id = actor.instance_id
            state.source_skill_id = source_skill.config_id if source_skill else None
            if "shadow_veil" in state.tags:
                state.payload.setdefault("entry_troops", target.troops)
            self.register_state(state)
        if source_effect:
            source_effect.applied_state_count += 1
        state_payload = {
            "state_config_id": state_config_id,
            "duration_rounds": state.duration_rounds,
            "remaining_rounds": state.remaining_rounds,
            "action_tick_count": state.action_tick_count,
            "stack": state.stack,
            "state_type": state.state_type.value,
            "payload": dict(state.payload),
            "refreshed": refreshed,
        }
        if state.state_type == StateType.CONTROL:
            self.emit_event(
                EventType.CONTROL_STATE_APPLIED,
                actor_id=actor.instance_id,
                target_ids=[target.instance_id],
                source_type=TriggerableType.SKILL.value if source_skill else TriggerableType.STATE.value if source_state else None,
                source_id=source_skill.config_id if source_skill else source_state.config_id if source_state else None,
                skill_id=source_skill.config_id if source_skill else None,
                effect_id=source_effect.config_id if source_effect else None,
                state_instance_id=state.instance_id,
                payload=state_payload,
            )
        else:
            self.emit_event(
                EventType.STATE_ADDED,
                actor_id=actor.instance_id,
                target_ids=[target.instance_id],
                skill_id=source_skill.config_id if source_skill else None,
                effect_id=source_effect.config_id if source_effect else None,
                state_instance_id=state.instance_id,
                payload=state_payload,
            )
        detail = ""
        if state.state_type in (StateType.ATTR, StateType.DAMAGE_REDUCE):
            detail = f"，效果={self._format_state_payload_for_log(state.payload)}"
        action = "刷新" if refreshed else "施加"
        self.log(f"{actor.name} 为 {target.name} {action}状态 {state.name}，持续回合={state.duration_rounds}{detail}")
        if (
            not refreshed
            and state.state_type == StateType.CONTROL
            and bool(state.payload.get("forbid_active"))
        ):
            self._interrupt_active_preparing(target, reason="CONTROL_INTERRUPT")
        return state

    @staticmethod
    def _format_state_payload_for_log(payload: dict[str, Any]) -> str:
        if not payload:
            return "无"
        return "，".join(f"{key}={value}" for key, value in sorted(payload.items()))

    def remove_state(self, target: Hero, state_instance_id: str, reason: str) -> None:
        state = self.state_instances.get(state_instance_id)
        if state is None:
            return
        self.unregister_state(state)
        self.emit_event(
            EventType.STATE_REMOVED,
            target_ids=[target.instance_id],
            state_instance_id=state_instance_id,
            payload={"reason": reason},
        )
        self.log(f"{target.name} 的状态 {state.name} 被移除，原因={reason}")

    def modify_attr(self, actor: Hero, target: Hero, attr: str, delta: int | None = None, value: int | None = None, source: str | None = None) -> None:
        old_value = int(getattr(target, attr))
        new_value = value if value is not None else old_value + int(delta or 0)
        setattr(target, attr, new_value)
        self.emit_event(EventType.ATTR_CHANGED, actor_id=actor.instance_id, target_ids=[target.instance_id], payload={"attr": attr, "old_value": old_value, "new_value": new_value, "source": source})

    def mark_hero_exited(
        self,
        hero: Hero,
        reason: str,
        killer: Hero | None = None,
        *,
        defer_battle_finish: bool = False,
    ) -> None:
        if hero.exited:
            return
        hero.exited = True
        hero.exit_round = self.round_no
        hero.exit_reason = reason
        hero.troops = max(0, hero.troops)
        self._purge_hero_battle_presence(hero)
        if killer is not None:
            killer.kills += 1
            killer.exited_enemies += 1
        self.emit_event(
            EventType.HERO_EXITED,
            actor_id=killer.instance_id if killer else None,
            target_ids=[hero.instance_id],
            payload={"reason": reason, "killer_id": killer.instance_id if killer else None},
        )
        self.log(f"{hero.name} 兵力归零，退出战斗")
        self.emit_event(
            EventType.HERO_EXITED_SETTLED,
            actor_id=killer.instance_id if killer else None,
            target_ids=[hero.instance_id],
            payload={"reason": reason, "killer_id": killer.instance_id if killer else None},
        )
        self._on_hero_exited_hit_rate(hero)
        if hero.role == HeroRole.MAIN:
            self.emit_event(EventType.MAIN_HERO_EXITED, target_ids=[hero.instance_id], payload={"team_id": hero.team_id})
            if not defer_battle_finish:
                self._finish_for_main_hero_exit(hero)
        else:
            self.check_battle_finish()

    def _finish_for_main_hero_exit(self, hero: Hero) -> None:
        winner = self.get_enemy_team_id(hero.team_id)
        self.finish_battle(self._result_for_winner(winner), winner, "MAIN_HERO_EXITED")

    def check_battle_finish(self) -> bool:
        if self.battle_finished:
            return True
        defeated: list[str] = []
        for team_id, hero_ids in self.teams.items():
            main_hero = self.heroes[self.main_hero_by_team[team_id]]
            if main_hero.exited or all(self.heroes[hero_id].exited for hero_id in hero_ids):
                defeated.append(team_id)
        if len(defeated) == len(self.teams):
            self.finish_battle(BattleResultType.DRAW, None, "BOTH_TEAMS_DEFEATED")
        elif len(defeated) == 1:
            loser = defeated[0]
            main_hero = self.heroes[self.main_hero_by_team[loser]]
            reason = "MAIN_HERO_EXITED" if main_hero.exited else "TEAM_DEFEATED"
            self.emit_event(EventType.TEAM_DEFEATED, target_ids=self.teams[loser], payload={"team_id": loser})
            winner = self.get_enemy_team_id(loser)
            self.finish_battle(self._result_for_winner(winner), winner, reason)
        return self.battle_finished

    def finish_battle(self, result: BattleResultType, winner_team_id: str | None, reason: str) -> None:
        if self.battle_finished:
            return
        self.battle_finished = True
        self.battle_result = result
        self.winner_team_id = winner_team_id
        self.finish_reason = reason
        self.log(f"战斗结束，结果={result.value}，胜者={winner_team_id}，原因={reason}")

    def finish_by_remaining_troops(self) -> None:
        totals = {team_id: sum(self.heroes[hero_id].troops for hero_id in hero_ids) for team_id, hero_ids in self.teams.items()}
        ordered = sorted(totals.items())
        if ordered[0][1] == ordered[1][1]:
            self.finish_battle(BattleResultType.DRAW, None, "MAX_ROUNDS_DRAW")
            return
        winner = max(ordered, key=lambda item: item[1])[0]
        self.finish_battle(self._result_for_winner(winner), winner, "MAX_ROUNDS_REMAINING_TROOPS")

    def tick_states(self, timing: Timing) -> None:
        """处理显式按 ROUND_END 结算的状态。

        当前 BattleCore 的默认持续回合规则是：
        - 状态挂到目标身上后，等目标自己的 BEFORE_ACTION 到来时计数 +1。
        - 当计数大于配置持续回合数，状态过期移除。

        因此普通控制状态、属性型 CONST 状态都不应该在 ROUND_END 统一扣回合。
        这里保留 ROUND_END tick，是为了兼容未来类似“回合结束自然衰减”的特殊状态。
        只有 payload.duration_tick_mode == "ROUND_END" 的状态才会在 State.tick_duration 中减少回合。
        """
        for state in list(self.state_instances.values()):
            state.tick_duration(self, timing)
            if state.is_expired():
                self.remove_state(state.owner, state.instance_id, "EXPIRED")

    def _retain_state_on_hero_exit(self, state: State, exited_hero_id: str) -> bool:
        """阵亡清理时不移除的状态。"""
        return False

    def _remove_hero_from_action_orders(self, hero_id: str) -> None:
        if hero_id in self.speed_order:
            self.speed_order = [hid for hid in self.speed_order if hid != hero_id]
        for round_no, order in list(self.round_action_orders.items()):
            self.round_action_orders[round_no] = [hid for hid in order if hid != hero_id]

    def _purge_hero_battle_presence(self, hero: Hero) -> None:
        """阵亡后移出该武将在本局的一切状态与触发索引，并禁用其战法。"""
        hero_id = hero.instance_id
        to_remove: list[tuple[Hero, str]] = []
        seen_state_ids: set[str] = set()
        for state in list(self.state_instances.values()):
            if self._retain_state_on_hero_exit(state, hero_id):
                continue
            should_remove = (
                state.owner.instance_id == hero_id
                or state.source_actor_id == hero_id
            )
            if should_remove and state.instance_id not in seen_state_ids:
                seen_state_ids.add(state.instance_id)
                to_remove.append((state.owner, state.instance_id))
        for target, state_instance_id in to_remove:
            self.remove_state(target, state_instance_id, "HERO_EXITED")
        for skill in self.skill_instances.values():
            if skill.owner.instance_id == hero_id:
                skill.set_enabled(False)
        self._remove_hero_from_action_orders(hero_id)
        if self.current_actor_id == hero_id:
            self.current_actor_id = None
        self.rebuild_indexes()

    def _apply_wounded_to_dead_at_round_start(self) -> None:
        """回合开始时，在场武将统一将伤兵池的 30% 转为死兵。"""
        for hero in self.heroes.values():
            if hero.exited or not hero.is_alive():
                continue
            result = apply_wounded_to_dead(hero)
            if result["converted"] <= 0:
                continue
            self.log(
                f"{hero.name} 回合开始伤兵转死兵 {result['converted']}，"
                f"伤兵 {result['old_wounded_troop']} -> {result['new_wounded_troop']}，"
                f"阵亡 {result['old_dead_troop']} -> {result['new_dead_troop']}"
            )

    def _log_round_start_effective_attrs(self) -> None:
        """回合开始时打印在场武将的有效四维（含 ATTR 状态修正）。"""
        self.log(format_round_effective_attrs_table(self))

    def tick_states_before_actor_action(self, actor: Hero) -> None:
        """在目标自己的 BEFORE_ACTION 处理状态持续时间。

        这是当前项目的默认持续规则：
        - “持续 1 回合”不是到 ROUND_END 就消失。
        - 而是从目标获得状态开始，等轮到该目标行动前计数。
        - 第一次 BEFORE_ACTION 计数为 1，状态仍然生效。
        - 当后续计数大于 duration_rounds 时，状态移除。

        这样控制类状态更符合 SLG 语义：
        目标获得控制后，至少会影响目标下一次行动窗口。
        """
        for state in list(actor.states):
            if state.duration_rounds >= 999:
                continue
            state.action_tick_count += 1
            state.remaining_rounds = max(0, state.duration_rounds - state.action_tick_count + 1)
            self.emit_event(
                EventType.STATE_DURATION_TICKED,
                actor_id=actor.instance_id,
                target_ids=[actor.instance_id],
                state_instance_id=state.instance_id,
                payload={
                    "state_config_id": state.state_config_id,
                    "action_tick_count": state.action_tick_count,
                    "duration_rounds": state.duration_rounds,
                    "remaining_rounds": state.remaining_rounds,
                },
            )
            self.log(
                f"{actor.name} 的状态 {state.name} 持续计数 "
                f"{state.action_tick_count}/{state.duration_rounds}"
            )
            if state.action_tick_count > state.duration_rounds:
                self.remove_state(actor, state.instance_id, "ACTION_DURATION_EXPIRED")

    def reset_round_counters(self) -> None:
        for triggerable in list(self.skill_instances.values()) + list(self.state_instances.values()):
            triggerable.reset_round_counters()

    def _result_for_winner(self, winner_team_id: str) -> BattleResultType:
        if winner_team_id == "team_a":
            return BattleResultType.TEAM_A_WIN
        if winner_team_id == "team_b":
            return BattleResultType.TEAM_B_WIN
        return BattleResultType.UNFINISHED

    def summary(self) -> BattleSummary:
        return BattleSummary(
            battle_id=self.battle_id,
            result=self.battle_result.value,
            winner_team_id=self.winner_team_id,
            rounds=self.round_no,
            finish_reason=self.finish_reason,
            hero_summaries=[self.heroes[hero_id].summary() for hero_id in sorted(self.heroes)],
            skill_summaries=[skill.summary() for skill in self.skill_instances.values()],
            state_summaries=[state.summary() for state in self.state_instances.values()],
            effect_summaries=[effect.summary() for effect in self.effect_instances.values()],
            event_count=len(self.event_stream),
            rng_count=self.rng.index,
        )
