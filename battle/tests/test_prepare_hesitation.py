"""准备型战法 + 犹豫系统测试（D-02，2026-07-05 二次人工修订）：
prepare/release/interrupted 协议、延迟登记与补结算（固定延后 1 回合，N→N+1）、
重复施加刷新不叠层、行动后计次消耗。

直接运行：python battle/tests/test_prepare_hesitation.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero


def duo_setup(a_skills=(), b_skills=(), battle_id="t_prep") -> BattleSetup:
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=85, intelligence=100, command=200, speed=95,
                          skills=a_skills),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=85, intelligence=90, command=200, speed=80,
                          skills=b_skills),
            )),
        ),
    )


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def skill_triggers(events, skill_id=None, kind=None, actor=None):
    out = []
    for e in events:
        if e["type"] != "skill_trigger":
            continue
        p = e["payload"]
        if skill_id and p["skill_id"] != skill_id:
            continue
        if kind and p["kind"] != kind:
            continue
        if actor and p["actor_id"] != actor:
            continue
        out.append(e)
    return out


def test_prepare_then_release_next_window():
    """prepare 事件无目标不结算；下一窗口 release 且有伤害子事件。"""
    for seed in range(30):
        report = simulate(duo_setup(a_skills=("test_charged_nova",)), seed=seed)
        events = flat_events(report)
        prepares = skill_triggers(events, "test_charged_nova", "prepare")
        releases = skill_triggers(events, "test_charged_nova", "release")
        if not prepares:
            continue
        assert prepares[0]["payload"]["target_ids"] == []
        # prepare 组下不得有 damage
        assert not [e for e in events if e["parent_seq"] == prepares[0]["seq"]
                    and e["type"] == "damage"]
        if releases:
            release = releases[0]
            # release 在 prepare 的下一回合（r+1）
            assert release["t"]["r"] == prepares[0]["t"]["r"] + 1
            damages = [e for e in events if e["parent_seq"] == release["seq"]
                       and e["type"] == "damage"]
            assert damages and damages[0]["payload"]["damage_type"] == "magic"
            return
    raise AssertionError("30 个种子未见 prepare→release 完整链")


def test_silence_interrupts_preparing():
    """准备期间被缄默 → kind=interrupted 事件（带打断来源），不再 release。"""
    for seed in range(80):
        report = simulate(
            duo_setup(a_skills=("test_charged_nova",), b_skills=("test_silence",)),
            seed=seed,
        )
        events = flat_events(report)
        interrupted = skill_triggers(events, "test_charged_nova", "interrupted")
        if not interrupted:
            continue
        event = interrupted[0]
        assert event["payload"]["interrupted_by"]["status_id"] == "silence"
        # 打断后同局内：该 prepare 不产生 release（下一次 prepare 之前无 release）
        game_events = [e for e in events if e["t"]["g"] == event["t"]["g"]]
        later = [e for e in skill_triggers(game_events, "test_charged_nova", "release")
                 if e["seq"] > event["seq"]]
        next_prepare = [e for e in skill_triggers(game_events, "test_charged_nova", "prepare")
                        if e["seq"] > event["seq"]]
        if later:
            assert next_prepare and next_prepare[0]["seq"] < later[0]["seq"], \
                "打断后的 release 必须来自新的 prepare"
        return
    raise AssertionError("80 个种子未见缄默打断准备")


def test_hesitation_delays_then_settles():
    """犹豫延迟：kind=delayed 宣告（固定 delay_rounds=1），下一回合同武将窗口补结算
    （N 回合的行动推迟到 N+1 回合释放）。"""
    for seed in range(80):
        report = simulate(
            duo_setup(a_skills=("test_hesitate",), b_skills=("test_blast",)), seed=seed
        )
        events = flat_events(report)
        delayed = skill_triggers(events, kind="delayed", actor="b1")
        if not delayed:
            continue
        for event in delayed:
            assert event["payload"]["delay_rounds"] == 1, "延后固定 1 回合（二次修订）"
        event = delayed[0]
        if event["payload"]["skill_id"] == "basic_attack":
            # 普攻延迟：下一回合补打（normal_attack 出现在 r+1 的 b1 窗口）
            makeups = [
                e for e in events
                if e["type"] == "normal_attack" and e["payload"]["actor_id"] == "b1"
                and e["t"]["g"] == event["t"]["g"] and e["t"]["r"] == event["t"]["r"] + 1
            ]
            if makeups:
                return
        else:
            makeups = [
                e for e in skill_triggers(events, event["payload"]["skill_id"], "release", "b1")
                if e["t"]["g"] == event["t"]["g"] and e["t"]["r"] == event["t"]["r"] + 1
            ]
            if makeups:
                return
    raise AssertionError("80 个种子未见犹豫延迟-补结算完整链")


def test_hesitation_reapply_refreshes_not_stacks():
    """重复施加犹豫 → 刷新不叠层（stacks 恒为 1），延迟宣告恒为 1 回合，
    已登记的延迟行动不受刷新影响（仍按原定下一回合释放）。"""
    refreshed = False
    for seed in range(200):
        report = simulate(
            duo_setup(a_skills=("test_hesitate", "test_war_cry"), b_skills=()), seed=seed
        )
        events = flat_events(report)
        for event in events:
            if event["type"] in ("status_apply", "status_refresh") and \
                    event["payload"]["status"]["status_id"] == "hesitation":
                assert event["payload"]["stacks"] == 1, "犹豫不可叠层"
                if event["type"] == "status_refresh":
                    refreshed = True
        for event in skill_triggers(events, kind="delayed", actor="b1"):
            assert event["payload"]["delay_rounds"] == 1
        if refreshed:
            return
    raise AssertionError("200 个种子未见犹豫刷新（status_refresh）")


def test_hesitation_expires_by_window_end_ticks():
    """犹豫按行动窗口末计次（D-02 修订）：持续 2 回合 = 覆盖 2 个行动窗口后移除。"""
    for seed in range(40):
        report = simulate(duo_setup(a_skills=("test_hesitate",)), seed=seed)
        events = flat_events(report)
        removes = [e for e in events if e["type"] == "status_remove"
                   and e["payload"]["status"]["status_id"] == "hesitation"
                   and e["payload"]["reason"] == "expired"]
        if not removes:
            continue
        remove_event = removes[0]
        instance_id = remove_event["payload"]["status"]["instance_id"]
        # 犹豫可刷新：计次从最后一次 apply/refresh 起算
        renewals = [
            e for e in events
            if e["type"] in ("status_apply", "status_refresh")
            and e["payload"]["status"].get("instance_id") == instance_id
            and e["seq"] < remove_event["seq"]
        ]
        assert renewals, "移除前必有施加/刷新"
        last_renewal = renewals[-1]
        if last_renewal["t"]["g"] != remove_event["t"]["g"]:
            continue
        # 最后刷新后覆盖之后 2 个行动窗口 → 第 2 个窗口末移除
        span = remove_event["t"]["r"] - last_renewal["t"]["r"]
        assert 1 <= span <= 2, f"seed={seed} 犹豫持续异常 span={span}"
        return
    raise AssertionError("40 个种子未见犹豫到期移除")


def test_delayed_action_voided_on_defeat():
    """施法者延迟期间阵亡 → 延迟行动作废：阵亡后不再有该武将的任何动作事件。"""
    for seed in range(60):
        report = simulate(
            duo_setup(a_skills=("test_hesitate", "test_blast"), b_skills=()), seed=seed
        )
        events = flat_events(report)
        defeats = {e["payload"]["hero_id"]: e["seq"] for e in events
                   if e["type"] == "hero_defeated"}
        if not defeats:
            continue
        for hero_id, defeat_seq in defeats.items():
            for event in events:
                if event["seq"] <= defeat_seq:
                    continue
                if event["type"] in ("normal_attack", "skill_trigger") and \
                        event["payload"].get("actor_id") == hero_id:
                    raise AssertionError(f"seed={seed} 阵亡者 {hero_id} 仍有动作 {event}")
        return
    raise AssertionError("60 个种子无阵亡场景")


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
