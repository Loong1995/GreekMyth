from __future__ import annotations

"""人类可读战报文本格式化：全项目日志输出的唯一出口。

- 战法/状态一律打印中文名（battle/names.py 注册表；未登记 id 原样显示）。
- 两档粒度：
    mode="all"   全量：逐事件打印（伤兵损耗、状态生灭、属性修改等全部细节），
                 并插入技能触发掷点明细（率/roll/成败/保底/跳过原因，
                 来自 report["_debug_rolls"] 调试侧信道，不进战报 JSON）；
    mode="brief" 主干：回合/单挑/出手/战法/伤害/治疗/状态发动/性格台词/阵亡/
                 局末与系列结果，跳过伤兵自然损耗、状态施加与生灭细节、属性修改。
仅影响人类可读文本，不触碰战报 JSON（契约字段仍是 id）。
"""

from typing import Any

from battle.names import skill_name, status_name

MODES = ("brief", "all")

# brief 模式跳过的事件类型（纯细节层）
_BRIEF_SKIP = {
    "troops_change", "status_apply", "status_refresh", "status_remove", "attr_change",
}

_SKILL_VERBS = {
    "cast": "发动", "prepare": "开始准备", "release": "释放",
    "interrupted": "准备被打断", "delayed": "行动被犹豫延后", "assist": "连携发动",
}

_REMOVE_REASONS = {
    "expired": "到期", "dispelled": "被驱散", "source_defeated": "来源阵亡",
}


def _fmt_troops(delta: dict) -> str:
    return (f"兵{delta['troops_before']}->{delta['troops_after']} "
            f"伤{delta['wounded_after']} 亡{delta['dead_after']}")


_SELECT_REASON_LABELS = {
    "basic": "普攻", "skill": "战法", "trident": "海神震荡", "trials": "试炼反打",
}


def _fmt_target_selects(records: list[dict], prefix: str) -> list[str]:
    """选人过程（仅 all 模式）：候选受击点数 + 命中者（mechanics/targeting.md）。"""
    lines = []
    for record in records:
        label = _SELECT_REASON_LABELS.get(record["reason"].split(":", 1)[0],
                                          record["reason"])
        pool = " | ".join(f"{c['hero_id']} {c['hit_bps']}" for c in record["candidates"])
        lines.append(f"{prefix}       ·选人[{label}] 受击点数: {pool}"
                     f" → 选中 {record['selected_id']}")
    return lines


def _fmt_event(event: dict, brief: bool) -> str | None:
    t, p = event["t"], event["payload"]
    prefix = f"  [g{t['g']} r{t['r']}]"
    kind = event["type"]
    if kind == "round_start" and p["round_no"] > 0:
        return f"{prefix} — 回合 {p['round_no']} —"
    if kind == "troops_change":
        d = p["troops"]
        lost = d["wounded_before"] - d["wounded_after"]
        return f"{prefix}   {d['hero_id']} 伤兵损耗 {lost} ({_fmt_troops(d)})"
    if kind == "normal_attack":
        combo = f"（连击第 {p['strike_no']} 击）" if p.get("strike_no", 1) > 1 else ""
        return f"{prefix}   {p['actor_id']} 普攻{combo} -> {p['target_ids'][0]}"
    if kind == "skill_trigger":
        targets = ",".join(p["target_ids"])
        verb = _SKILL_VERBS.get(p["kind"], p["kind"])
        extra = ""
        if p["kind"] == "delayed":
            extra = f"（延后 {p['delay_rounds']} 回合）"
        elif p["kind"] == "interrupted":
            extra = f"（来源〔{status_name(p['interrupted_by']['status_id'])}〕）"
        arrow = f" -> {targets}" if targets else ""
        return f"{prefix}   {p['actor_id']} {verb}【{skill_name(p['skill_id'])}】{extra}{arrow}"
    if kind == "duel_challenge":
        return (f"{prefix} ⚔ 单挑叫阵：{p['challenger_id']}（武{p['challenger_force']}）"
                f"向 {p['defender_id']}（武{p['defender_force']}）")
    if kind == "duel_result":
        if p["accepted"]:
            return f"{prefix} ⚔ 单挑接受：{p['winner_id']} 胜，{p['loser_id']} 负（四维-10）"
        return f"{prefix} ⚔ 单挑被拒绝"
    if kind == "damage":
        crit = " 暴击!" if p["is_crit"] else ""
        tag = {"physical": "", "magic": "[魔法]", "true": "[真实]"}[p["damage_type"]]
        mit = {"block": " 被格挡!", "evade": " 被闪避!", "reflect": " 被反弹!"}.get(
            p.get("mitigation", ""), "")
        detail = f"余兵 {p['troops']['troops_after']}" if brief else _fmt_troops(p["troops"])
        return (f"{prefix}     ↳ {tag}伤害 {p['amount']}{crit}{mit} -> {p['target_id']} "
                f"({detail})")
    if kind == "heal":
        crit = " 暴击!" if p["is_crit"] else ""
        detail = f"余兵 {p['troops']['troops_after']}" if brief else _fmt_troops(p["troops"])
        return (f"{prefix}   {p['source_id']} 治疗 {p['target_id']} "
                f"+{p['amount']}{crit} ({detail})")
    if kind == "status_apply":
        s = p["status"]
        return (f"{prefix}   {p['source_id']} 对 {s['owner_id']} 施加"
                f"〔{status_name(s['status_id'])}〕x{p['stacks']}"
                f"（持续 {p['duration_rounds']} 回合）")
    if kind == "status_refresh":
        s = p["status"]
        return (f"{prefix}   〔{status_name(s['status_id'])}〕刷新/叠层 -> "
                f"x{p['stacks']}（{s['owner_id']}）")
    if kind == "status_tick":
        s = p["status"]
        return (f"{prefix}   〔{status_name(s['status_id'])}〕发动"
                f"（{s['owner_id']}，来源 {p['source_id']}）")
    if kind == "status_remove":
        s = p["status"]
        reason = _REMOVE_REASONS.get(p["reason"], p["reason"])
        return f"{prefix}   〔{status_name(s['status_id'])}〕{reason}移除（{s['owner_id']}）"
    if kind == "attr_change":
        changes = ", ".join(f"{c['attr']} {c['before']}->{c['after']}" for c in p["changes"])
        via = (f"〔{status_name(p['source_status']['status_id'])}〕"
               if p.get("source_status") else p["scope"])
        return f"{prefix}   {p['hero_id']} 属性修改（{via}）: {changes}"
    if kind == "trait_trigger":
        from battle.traits import REGISTRY as _TRAITS
        trait = _TRAITS.get(p["trait_id"])
        trait_label = trait.name if trait is not None else p["trait_id"]
        line = f"「{p['line']}」" if p["line"] else ""
        return (f"{prefix}   ★ {p['hero_id']} 性格〔{trait_label}〕发作"
                f"（{p['effect']}）{line}")
    if kind == "hero_defeated":
        main = "（主将！）" if p["is_main_hero"] else ""
        return f"{prefix}   ×× {p['hero_id']} 兵力归零退出战斗{main}"
    if kind == "game_end":
        snapshot = "; ".join(f"{d['hero_id']} 兵{d['troops_after']}" for d in p["troops"])
        return f"{prefix} 本局结束：{snapshot}"
    return None


def _fmt_debug_roll(entry: dict) -> str:
    """技能触发判定/跳过明细（调试侧信道，仅 all 档打印）。"""
    name = skill_name(entry["skill_id"]) if entry["skill_id"] != "*" else "全部主动"
    who = f"{entry['hero_id']}〔{name}〕"
    if entry["kind"] == "skip":
        return f"        ⊘ {who} 未判定：{entry['reason']}"
    tag = "连携判定" if entry["kind"] == "assist" else "触发判定"
    if entry.get("guaranteed"):
        return f"        ⚄ {who} {tag}：保底成功（累计失败达阈值）"
    if entry.get("roll") is None:
        return f"        ⚄ {who} {tag}：必发（率 ≥100%，不掷点）"
    base, current, roll = entry["base"], entry["current"], entry["roll"]
    rate_str = f"率 {current/100:.0f}%" + (
        f"（基础 {base/100:.0f}%+伪随机补偿）" if current != base else ""
    )
    verdict = "成功" if entry["allowed"] else f"失败（roll {roll} ≥ {current}）"
    if entry["allowed"]:
        verdict = f"成功（roll {roll} < {current}）"
    return f"        ⚄ {who} {tag}：{rate_str} → {verdict}"


def format_report(report: dict[str, Any], mode: str = "all") -> str:
    if mode not in MODES:
        raise ValueError(f"mode 必须是 {MODES} 之一，收到 {mode!r}")
    brief = mode == "brief"
    lines: list[str] = []
    lines.append(f"=== 战报 {report['battle_id']} | seed={report['rng_seed']} | "
                 f"core={report['core_version']} | schema={report['schema_version']} | "
                 f"日志粒度={mode} ===")
    for team in report["teams"]:
        names = ", ".join(
            f"{h['hero_id']}(武{h['force']}/智{h['intelligence']}/统{h['command']}/敏{h['speed']}"
            f" 兵{h['initial_troops']}/{h['max_troops']})"
            + ("[主]" if h["hero_id"] == team["main_hero_id"] else "")
            for h in team["heroes"]
        )
        lines.append(f"  队伍 {team['team_id']}: {names}")

    # 调试掷点明细（不进战报 JSON）：按 anchor_seq（该判定前最近一条事件）插位
    rolls_at: dict[int, list[dict]] = {}
    if not brief:
        for entry in report.get("_debug_rolls", ()):
            rolls_at.setdefault(entry["anchor_seq"], []).append(entry)

    for game in report["games"]:
        result = game["result"]
        outcome = f"胜者={result['winner_team_id']}" if result["winner_team_id"] else "平局"
        lines.append(f"\n---- 第 {game['game_no']} 局（{outcome}, {result['reason']}, "
                     f"至第 {result['end_round']} 回合, 事件 {len(game['events'])} 条）----")
        if game["game_no"] == 1:
            lines.extend(_fmt_debug_roll(e) for e in rolls_at.get(0, ()))
        for event in game["events"]:
            if brief and event["type"] in _BRIEF_SKIP:
                continue
            line = _fmt_event(event, brief)
            if line is not None:
                lines.append(line)
            if not brief and event["payload"].get("target_select"):
                t = event["t"]
                lines.extend(_fmt_target_selects(
                    event["payload"]["target_select"], f"  [g{t['g']} r{t['r']}]"))
            lines.extend(_fmt_debug_roll(e) for e in rolls_at.get(event["seq"], ()))

    result = report["result"]
    outcome = f"胜者={result['winner_team_id']}" if result["winner_team_id"] else "系列平局"
    lines.append(f"\n=== 系列结果：{outcome}（{result['reason']}，共 {result['total_games']} 局）===")
    lines.append("  逐局：" + " | ".join(
        f"第{g['game_no']}局 {g['winner_team_id'] or '平'}" for g in result["game_summaries"]))
    for entry in result["stats"]:
        lines.append(f"  {entry['hero_id']}: 总伤害 {entry['total_damage']}, "
                     f"击杀 {entry['kills']}, 终局兵力 {entry['final_troops']}")
    return "\n".join(lines) + "\n"


def safe_print(text: str) -> None:
    """Windows GBK 控制台下打印含中文/特殊符号文本不抛异常（文件输出以 UTF-8 为准）。"""
    import sys
    try:
        print(text, end="")
    except UnicodeEncodeError:
        encoding = sys.stdout.encoding or "utf-8"
        print(text.encode(encoding, errors="replace").decode(encoding), end="")
