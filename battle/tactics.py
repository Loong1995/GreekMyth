from __future__ import annotations

"""经理人战术系统（Phase 4 P4-C，机制文档 docs/mechanics/manager_tactics.md）。

- 战术 = 整体倾向 + 额外加成，全部走现有状态系统表达，不动结算机制。
- **注册表驱动**（phase4_plan 通则）：新战术 = 写一个 TacticDef 注册进
  TACTIC_REGISTRY，引擎主流程零改动。
- 配置入口（随 setup.metadata 序列化进战报 → replay 天然闭环）：
    metadata["tactics"] = {
        "preset": {"<team_id>": {"tactic_id": str, "params": {...}} | None, ...},
        "changes": [  # 按下达顺序；round = 生效回合（最早 2）；每队最多 2 条
            {"team_id": str, "round": int, "tactic_id": str, "params": {...}},
        ],
    }
- 生效模型：每回合 round_start 头部按 setup 队伍序结算各队当前生效战术并
  施加当回合状态（duration=1 逐回合刷新，战术被替换后旧状态自然到期）；
  变更生效回合额外发 `tactic_applied` 事件（schema 1.4.1 加法新增）。
- 逐回合重算（服务器侧）：确定性下「取第 N+1 回合快照续算」≡「同 seed +
  变更序列从头重模拟」——两者逐字节等价（回合 1..N 战术输入相同）。
  实现采用后者（毫秒级模拟成本），见 with_change() 与 tests。

首发三战术（§二4 定案）：
- focus_fire 集火目标：指定敌方受击点数 ×2（hit_weight_up_bps=+10000，
  仍走加权随机与保残兵递减，非强制锁定）；target_id 缺省 = 无偏置。
- protect 保护目标：我方指定单位减伤 8% + 每回合小额持续治疗（主将为源）。
- stance 攻守倾向：level -2~+2，全队造成伤害 ±3%/级、受到伤害 ∓3%/级。
"""

from dataclasses import dataclass
from typing import Any, Callable

from battle.errors import SetupError
from battle.setup import BattleSetup
from battle.statuses import SPECIAL, StatusDef

MAX_CHANGES_PER_TEAM = 2
MIN_CHANGE_ROUND = 2  # 第 1 回合必然走预设


# ---------------------------------------------------------------- 战术状态定义

FOCUS_STATUS = StatusDef(
    status_id="tactic_focus", kind=SPECIAL, duration_rounds=1,
    modifiers={"hit_weight_up_bps": 10000},  # 受击点数 ×2（bps 可在此调）
)

PROTECT_STATUS = StatusDef(
    status_id="tactic_protect", kind=SPECIAL, duration_rounds=1,
    modifiers={"damage_reduce_bps": 800},
    hot_rate_bps=400,  # 每回合按来源（主将）智力结算小额治疗
)


def _stance_status(level: int) -> StatusDef:
    """攻守倾向状态（按档位生成；level 已被 validate 限定 -2~+2 且非 0）。"""
    return StatusDef(
        status_id="tactic_stance", kind=SPECIAL, duration_rounds=1,
        modifiers={
            "damage_up_bps": level * 300,      # +3%/级（负 = 收缩输出）
            "damage_reduce_bps": -level * 300,  # 攻势付出承伤代价，守势反之
        },
    )


# ---------------------------------------------------------------- 注册表

@dataclass(frozen=True, slots=True)
class TacticDef:
    tactic_id: str
    name: str
    validate: Callable[[dict, BattleSetup, str], None]   # (params, setup, team_id)
    on_round_start: Callable  # (engine, team_id, params, parent_seq)


def _validate_focus(params: dict, setup: BattleSetup, team_id: str) -> None:
    target = params.get("target_id")
    if target is None:
        return  # 「不指定」= 无偏置
    enemy_ids = {h.hero_id for t in setup.teams if t.team_id != team_id for h in t.heroes}
    if target not in enemy_ids:
        raise SetupError("集火目标必须是敌方武将", team_id=team_id, target_id=target)


def _apply_focus(engine, team_id: str, params: dict, parent_seq: int) -> None:
    target_id = params.get("target_id")
    if target_id is None:
        return
    target = engine.heroes[target_id]
    if not target.is_alive():
        return  # 目标已阵亡：本回合无偏置（客户端 UI 提示改选由 C 批后续做）
    source = engine.heroes[engine.main_hero_of(team_id)]
    engine.apply_status(source, target, FOCUS_STATUS, parent_seq=parent_seq)


def _validate_protect(params: dict, setup: BattleSetup, team_id: str) -> None:
    target = params.get("target_id")
    ally_ids = {h.hero_id for t in setup.teams if t.team_id == team_id for h in t.heroes}
    if target not in ally_ids:
        raise SetupError("保护目标必须是我方武将", team_id=team_id, target_id=target)


def _apply_protect(engine, team_id: str, params: dict, parent_seq: int) -> None:
    target = engine.heroes[params["target_id"]]
    if not target.is_alive():
        return
    source = engine.heroes[engine.main_hero_of(team_id)]
    engine.apply_status(source, target, PROTECT_STATUS, parent_seq=parent_seq)


def _validate_stance(params: dict, setup: BattleSetup, team_id: str) -> None:
    level = params.get("level")
    if not isinstance(level, int) or not -2 <= level <= 2:
        raise SetupError("攻守倾向 level 必须为 -2~+2 整数", team_id=team_id, level=level)


def _apply_stance(engine, team_id: str, params: dict, parent_seq: int) -> None:
    level = params["level"]
    if level == 0:
        return
    status = _stance_status(level)
    source = engine.heroes[engine.main_hero_of(team_id)]
    for hero_id in engine.hero_order:  # hero_order 序（确定性）
        hero = engine.heroes[hero_id]
        if hero.team_id == team_id and hero.is_alive():
            engine.apply_status(source, hero, status, parent_seq=parent_seq)


TACTIC_REGISTRY: dict[str, TacticDef] = {
    t.tactic_id: t
    for t in (
        TacticDef("focus_fire", "集火目标", _validate_focus, _apply_focus),
        TacticDef("protect", "保护目标", _validate_protect, _apply_protect),
        TacticDef("stance", "攻守倾向", _validate_stance, _apply_stance),
    )
}


# ---------------------------------------------------------------- 配置校验/查询

def validate_tactics(setup: BattleSetup) -> None:
    """metadata["tactics"] 结构与业务规则校验（validate_setup 调用）。"""
    config = setup.metadata.get("tactics")
    if config is None:
        return
    team_ids = {t.team_id for t in setup.teams}
    for team_id, entry in config.get("preset", {}).items():
        if team_id not in team_ids:
            raise SetupError("预设战术 team_id 未知", team_id=team_id)
        _validate_entry(entry, setup, team_id)
    per_team: dict[str, int] = {}
    for change in config.get("changes", ()):
        team_id = change.get("team_id")
        if team_id not in team_ids:
            raise SetupError("战术变更 team_id 未知", team_id=team_id)
        round_no = change.get("round")
        if not isinstance(round_no, int) or round_no < MIN_CHANGE_ROUND:
            raise SetupError("战术变更最早第 2 回合生效", team_id=team_id, round=round_no)
        per_team[team_id] = per_team.get(team_id, 0) + 1
        if per_team[team_id] > MAX_CHANGES_PER_TEAM:
            raise SetupError("一局每方最多变更 2 次战术", team_id=team_id)
        _validate_entry(change, setup, team_id)


def _validate_entry(entry: dict | None, setup: BattleSetup, team_id: str) -> None:
    if entry is None:
        return
    tactic_id = entry.get("tactic_id")
    if tactic_id not in TACTIC_REGISTRY:
        raise SetupError("未注册的战术", team_id=team_id, tactic_id=tactic_id)
    TACTIC_REGISTRY[tactic_id].validate(entry.get("params", {}), setup, team_id)


def active_tactic(setup: BattleSetup, team_id: str, round_no: int) -> dict | None:
    """第 round_no 回合该队生效的战术条目（changes 按下达顺序后者覆盖前者）。"""
    config = setup.metadata.get("tactics")
    if config is None:
        return None
    entry = config.get("preset", {}).get(team_id)
    for change in config.get("changes", ()):
        if change.get("team_id") == team_id and change.get("round") <= round_no:
            entry = change
    return entry


def change_effective_this_round(setup: BattleSetup, team_id: str, round_no: int) -> dict | None:
    """本回合恰好生效的变更（tactic_applied 事件用）；同队同回合取最后一条。"""
    config = setup.metadata.get("tactics")
    if config is None:
        return None
    hit = None
    for change in config.get("changes", ()):
        if change.get("team_id") == team_id and change.get("round") == round_no:
            hit = change
    return hit


def with_change(setup: BattleSetup, change: dict) -> BattleSetup:
    """服务器「改变战术」入口：追加一条变更返回新 setup（供从头重模拟）。

    确定性保证：新 setup 重模拟的第 1..round-1 回合与原战报逐字节一致
    （战术输入在这些回合相同），从 round 回合起为替换段。"""
    old = setup.metadata.get("tactics", {})
    metadata = dict(setup.metadata)
    metadata["tactics"] = {
        "preset": dict(old.get("preset", {})),
        "changes": [*old.get("changes", ()), dict(change)],
    }
    new_setup = BattleSetup(battle_id=setup.battle_id, teams=setup.teams,
                            metadata=metadata)
    validate_tactics(new_setup)
    return new_setup
