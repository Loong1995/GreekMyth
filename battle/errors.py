from __future__ import annotations

from typing import Any


class BattleCoreError(Exception):
    """core 内部致命错误：战斗必须失败，禁止产出半截战报（任务书 6.3）。

    context 携带排查所需完整上下文（battle_id、seed、逻辑时间、当前状态摘要等）。
    """

    def __init__(self, message: str, **context: Any) -> None:
        self.context = context
        if context:
            detail = ", ".join(f"{key}={value!r}" for key, value in sorted(context.items()))
            message = f"{message} [{detail}]"
        super().__init__(message)


class SetupError(BattleCoreError):
    """battle_setup 校验失败（系统边界输入校验）。"""
