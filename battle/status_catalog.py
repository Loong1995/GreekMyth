from __future__ import annotations

"""status_catalog：状态播放标签目录（schema 1.5.2 顶层可选字段，加法演进）。

与 ``skill_catalog`` 同一思路：**播放语义在定义处声明**，随战报头下发，
客户端播放编译层直读，不在客户端猜「这个状态的触发能不能并成一个播放单元」。

唯一真源是 ``battle.statuses.StatusDef.playback_tags``（定义期自注册进
``STATUS_DEFS``）。本模块只做裁剪：**只导出带标签的状态**，无标签＝默认语义，
不占战报体积。状态到来源战法的归因仍是客户端 StatusPresentationRegistry
的职责，两层各管各的，不在此重复建账。
"""

from typing import Any

from battle.names import status_name
from battle.statuses import STATUS_DEFS


def build_status_catalog() -> dict[str, dict[str, Any]]:
    """带播放标签的状态表（status_id 字典序，确定性输出）。

    与出场阵容无关（标签是静态定义），所有战报同一份；客户端未知标签必须忽略。
    """
    return {
        sid: {"name": status_name(sid), "tags": list(sd.playback_tags)}
        for sid, sd in sorted(STATUS_DEFS.items())
        if sd.playback_tags
    }
