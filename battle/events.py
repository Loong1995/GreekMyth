from __future__ import annotations

"""事件写入器：seq 分配、逻辑时间、播放分组（parent_seq/group_id）、体积保险丝。

契约：docs/schema/battle_events.md §3。
- seq 系列内全局单调递增，从 1 起，跨局不重置。
- t=(g,r,p,s) 字典序必须与 seq 序一致，写入时强校验（违反=引擎 bug，立即失败）。
- 组根（parent_seq 可指因果来源）group_id=自身 seq；子事件继承父事件的 group_id。
- 每局事件数超过硬上限视为 core 内部错误（无限连锁保险丝），禁止半截战报。
"""

from typing import Any

from battle.errors import BattleCoreError

# 相位枚举（契约 §3.1）
PHASE_SERIES_START = 0
PHASE_GAME_START = 1
PHASE_DUEL = 2
PHASE_ROUND_START = 3
PHASE_ACTION = 4
PHASE_ROUND_END = 5
PHASE_GAME_END = 6
PHASE_SERIES_END = 7

MAX_EVENTS_PER_GAME = 20000


class EventWriter:
    __slots__ = ("_seq", "_time", "_games", "_current", "_group_of", "battle_id")

    def __init__(self, battle_id: str) -> None:
        self.battle_id = battle_id
        self._seq = 0
        self._time: tuple[int, int, int, int] = (1, 0, PHASE_SERIES_START, 0)
        self._games: list[list[dict[str, Any]]] = []
        self._current: list[dict[str, Any]] | None = None
        self._group_of: dict[int, int] = {}

    def begin_game(self) -> None:
        self._current = []
        self._games.append(self._current)

    @property
    def last_seq(self) -> int:
        """最近一次已写入事件的 seq（尚未写入任何事件时为 0）。"""
        return self._seq

    def set_time(self, g: int, r: int, p: int, s: int) -> None:
        new_time = (g, r, p, s)
        if new_time < self._time:
            raise BattleCoreError(
                "逻辑时间回退：t 字典序必须与 seq 序一致",
                battle_id=self.battle_id,
                old_t=self._time,
                new_t=new_time,
            )
        self._time = new_time

    def emit(
        self,
        event_type: str,
        payload: dict[str, Any],
        *,
        parent_seq: int = 0,
        new_group: bool = False,
        hint: dict[str, Any] | None = None,
    ) -> int:
        """写入一个事件，返回其 seq。

        parent_seq=0 或 new_group=True → 本事件为组根（group_id=自身）；
        否则继承父事件的 group_id。
        """
        if self._current is None:
            raise BattleCoreError("必须先 begin_game 才能发事件", battle_id=self.battle_id)
        if len(self._current) >= MAX_EVENTS_PER_GAME:
            raise BattleCoreError(
                "单局事件数超过硬上限（疑似无限连锁），战斗失败",
                battle_id=self.battle_id,
                limit=MAX_EVENTS_PER_GAME,
                t=self._time,
            )

        self._seq += 1
        seq = self._seq
        if parent_seq == 0 or new_group:
            group_id = seq
        else:
            group_id = self._group_of.get(parent_seq)
            if group_id is None:
                raise BattleCoreError(
                    "parent_seq 引用了不存在的事件",
                    battle_id=self.battle_id,
                    parent_seq=parent_seq,
                    seq=seq,
                )
        self._group_of[seq] = group_id

        g, r, p, s = self._time
        event: dict[str, Any] = {
            "seq": seq,
            "t": {"g": g, "r": r, "p": p, "s": s},
            "type": event_type,
            "parent_seq": parent_seq,
            "group_id": group_id,
            "payload": payload,
        }
        if hint:
            event["hint"] = hint
        self._current.append(event)
        return seq

    def games_events(self) -> list[list[dict[str, Any]]]:
        return self._games
