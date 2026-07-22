"""Dump duel admit/reject/win rates for current manual_3v3 lineup."""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.engine import (
    DUEL_REJECT_MAX_BPS,
    DUEL_REJECT_PER_DIFF_BPS,
    DUEL_WIN_BASE_BPS,
    DUEL_WIN_PER_DIFF_BPS,
    SeriesEngine,
)
from battle.rng import BPS
from battle.roster import DEFAULT_LEVEL, ROSTER, hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.tests import test_manual_3v3 as m


def build() -> BattleSetup:
    def one(entry, pos):
        tpl = ROSTER[entry["template"]]
        return hero_setup(
            entry["template"],
            hero_id=entry.get("hero_id", tpl.name),
            position=pos,
            extra_skills=tuple(entry.get("extra_skills", ())),
            level=entry.get("level", DEFAULT_LEVEL),
        )

    a = tuple(one(e, i) for i, e in enumerate(m.TEAM_A))
    b = tuple(one(e, i) for i, e in enumerate(m.TEAM_B))
    return BattleSetup(
        battle_id="manual_3v3",
        teams=(
            TeamSetup(team_id="A", main_hero_id=a[0].hero_id, heroes=a),
            TeamSetup(team_id="B", main_hero_id=b[0].hero_id, heroes=b),
        ),
    )


def main() -> str:
    lines: list[str] = []
    setup = build()
    eng = SeriesEngine(setup, seed=m.SEED)
    lines.append(f"SEED={m.SEED}")
    for tid in ("A", "B"):
        cs = eng._duel_contestants(tid)
        lines.append(f"contestants {tid}:")
        for h in cs:
            lines.append(
                f"  {h.hero_id} tpl={h.template_id} "
                f"F={eng.effective_attr(h, 'force')} "
                f"I={eng.effective_attr(h, 'intelligence')}"
            )

    sa, sb = eng._duel_contestants("A"), eng._duel_contestants("B")
    prelim = eng._duel_build_prelim_pairs(sa, sb)
    lines.append(f"\nprelim pairs ({len(prelim)}):")
    for p in prelim:
        d = p["force_diff"]
        admit = eng._duel_pair_admit_bps(d)
        rej = min(d * DUEL_REJECT_PER_DIFF_BPS, DUEL_REJECT_MAX_BPS)
        win = min(DUEL_WIN_BASE_BPS + d * DUEL_WIN_PER_DIFF_BPS, BPS)
        lines.append(
            f"  {p['hero_a'].hero_id}/{p['hero_a'].template_id} vs "
            f"{p['hero_b'].hero_id}/{p['hero_b'].template_id} "
            f"bond_w={p['bond_weight']} d={d}"
        )
        lines.append(
            f"    admit={admit}bps ({admit / 100:.1f}%)  "
            f"reject={rej}bps ({rej / 100:.1f}%)  "
            f"accept={(BPS - rej) / 100:.1f}%  "
            f"win_hi={win}bps ({win / 100:.1f}%)"
        )
        lines.append(
            f"    P(入池且接受)≈{admit / BPS * (BPS - rej) / BPS:.4f} "
            f"({100 * admit / BPS * (BPS - rej) / BPS:.2f}%)"
        )

    report = simulate(setup, seed=m.SEED)
    evs = [e for g in report["games"] if g["game_no"] == 1 for e in g["events"]]
    chal = next(e for e in evs if e["type"] == "duel_challenge")
    res = next(e for e in evs if e["type"] == "duel_result")
    lines.append("\nthis seed outcome:")
    lines.append(f"  challenge={chal['payload']}")
    lines.append(f"  result={res['payload']}")

    # 分解：入池成功 vs 空池剧本 / 真决斗拒绝 vs 接受
    c = Counter()
    pairs = Counter()
    for seed in range(500):
        e2 = SeriesEngine(setup, seed=seed)
        sa2, sb2 = e2._duel_contestants("A"), e2._duel_contestants("B")
        if not sa2 or not sb2:
            c["no_contestant"] += 1
            continue
        prelim2 = e2._duel_build_prelim_pairs(sa2, sb2)
        pool = []
        for pair in prelim2:
            rate = e2._duel_pair_admit_bps(pair["force_diff"])
            reason = f"{pair['hero_a'].hero_id}:{pair['hero_b'].hero_id}"
            if e2.rng.rand_bps("duel_pair", reason) < rate:
                pool.append(pair)
        if pool:
            chosen = sorted(
                pool,
                key=lambda p: (
                    p["bond_weight"],
                    p["force_diff"],
                    p["hero_a"].hero_id,
                    p["hero_b"].hero_id,
                ),
            )[0]
            c["pool_hit"] += 1
            challenger, defender, diff = e2._duel_pick_roles(
                chosen["hero_a"], chosen["hero_b"]
            )
            rej = min(diff * DUEL_REJECT_PER_DIFF_BPS, DUEL_REJECT_MAX_BPS)
            rejected = False
            if rej > 0:
                rejected = e2.rng.rand_bps("duel_reject", defender.hero_id) < rej
            if rejected:
                c["real_reject"] += 1
            else:
                c["real_accept"] += 1
            pairs[(challenger.hero_id, defender.hero_id, diff)] += 1
        else:
            c["pool_empty_scripted_reject"] += 1
            chosen = prelim2[0]
            ch, df, diff = e2._duel_pick_roles(chosen["hero_a"], chosen["hero_b"])
            pairs[(ch.hero_id, df.hero_id, diff)] += 1

    lines.append("\nseed 0..499 分解（复现入池 RNG）:")
    for k, v in c.most_common():
        lines.append(f"  {k}: {v} ({100 * v / 500:.1f}%)")
    lines.append("pair frequency:")
    for k, n in pairs.most_common(8):
        lines.append(f"  {n:3d}x  {k[0]} vs {k[1]} d={k[2]}")
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    text = main()
    out = Path("battle/out/_duel_prob.txt")
    out.write_text(text, encoding="utf-8")
    print(f"wrote {out}")
