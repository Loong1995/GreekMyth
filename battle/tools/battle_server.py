"""battle_server：长期运行的战斗结算 HTTP 服务。

客户端（手动配阵页/未来正式客户端，含 iOS）通过 HTTP 请求结算，
服务端权威执行 battle core，返回战报/统计 JSON。stdlib 实现零依赖。

用法（仓库根目录）：
    python battle/tools/battle_server.py                 # 默认 0.0.0.0:8017
    python battle/tools/battle_server.py --port 8017 --host 0.0.0.0

接口（均 JSON，UTF-8）：
    GET  /health   → {"ok": true, "core": <battle 版本>}
    GET  /catalog  → 武将/战法目录（同 client_battle_bridge --catalog）
    POST /battle   body {"config": {...}, "seed": 7}      → 完整战报
    POST /stats    body {"config": {...}, "n": 100, "seed": 0} → 百场统计

config 结构同 manual_battle.py --example；跨队同模板自动改名（桥接同源逻辑）。
"""
from __future__ import annotations

import argparse
import json
import sys
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.tools.client_battle_bridge import (
    build_catalog,
    build_setup_from_config,
    run_stats,
)
from battle.version import CORE_VERSION

MAX_STATS_N = 2000  # 单请求场次上限，防误传大数拖死服务


class BattleHandler(BaseHTTPRequestHandler):
    server_version = "GreekMythBattle/1.0"

    # ------------------------------------------------------------ 基础设施

    def _send(self, code: int, payload: str | dict) -> None:
        body = (payload if isinstance(payload, str)
                else json.dumps(payload, ensure_ascii=False)).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        # 客户端可能从任意来源访问（编辑器/真机），放开 CORS 便于调试
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0:
            raise ValueError("空请求体")
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def log_message(self, fmt: str, *args) -> None:  # 精简访问日志
        print(f"[battle_server] {self.address_string()} {fmt % args}")

    # ------------------------------------------------------------ 路由

    def do_GET(self) -> None:
        try:
            if self.path == "/health":
                self._send(200, {"ok": True, "core": CORE_VERSION})
            elif self.path == "/catalog":
                self._send(200, build_catalog())
            else:
                self._send(404, {"error": f"未知路径 {self.path}"})
        except Exception:
            self._send(500, {"error": traceback.format_exc()})

    def do_POST(self) -> None:
        try:
            req = self._read_json()
            if self.path == "/battle":
                setup = build_setup_from_config(req["config"])
                report = simulate(setup, seed=int(req.get("seed", 7)))
                self._send(200, serialize_report(report))
            elif self.path == "/stats":
                n = int(req.get("n", 100))
                if not 1 <= n <= MAX_STATS_N:
                    self._send(400, {"error": f"n 须在 1~{MAX_STATS_N}"})
                    return
                stats = run_stats(req["config"], n=n,
                                  seed_start=int(req.get("seed", 0)))
                self._send(200, stats)
            else:
                self._send(404, {"error": f"未知路径 {self.path}"})
        except (KeyError, ValueError, json.JSONDecodeError) as ex:
            self._send(400, {"error": f"请求格式错误: {ex}"})
        except Exception:
            # core 内部错误不静默：完整栈回给客户端并打到控制台
            tb = traceback.format_exc()
            print(tb, file=sys.stderr)
            self._send(500, {"error": tb})


def main() -> None:
    parser = argparse.ArgumentParser(description="战斗结算 HTTP 服务（长期运行）")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8017)
    args = parser.parse_args()

    server = ThreadingHTTPServer((args.host, args.port), BattleHandler)
    print(f"[battle_server] core {CORE_VERSION} 监听 {args.host}:{args.port}"
          f"（/health /catalog /battle /stats，Ctrl+C 停止）")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[battle_server] 已停止")


if __name__ == "__main__":
    main()
