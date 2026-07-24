# 手动配阵测试页（ManualSetupPanel）

> 客户端内配阵 → 调 Python 结算 → 播战报 / 出百场统计。仅编辑器与 PC
> 独立版可用（需仓库目录结构完整 + PATH 有 `python`）。

## 一、用法

1. 新建空场景（相机 + 灯），空物体挂 `ClientBattle/Test/ManualSetupPanel.cs`
   （**不需要** BattleReportTester）。
2. Play 后自动加载武将目录（python 导出，约 1 秒）。
3. 页面布局：**每队 6 站位槽**（1~3 前排、4~6 后排），B 在上 / A 在下镜面；
   播放侧支持交错阵：方圆{1,5,6} / 却月{1,2,6} / 鹤翼{2,4,6}
   （见 [rendering_layout.md](rendering_layout.md) §五）；配阵仍可点满 12 槽调试。
   中缝「对战 1 次 / 对战 100 次」+ 种子。每队最多上阵 **3** 人。
4. 交互：
   - 点空位 → 武将备选池（32 将，同队同模板置灰；满 3 人置灰）；点即上阵。
   - 拖已上阵武将到另一位 → 换位/互换（同队重复或跨队超 3 人会拒绝）。
   - 每将 3 战法格：◆ 自带 + 2 可配空格；点 ＋ → 战法池装配。
   - 点武将卡 / 战法格 → 详情弹窗（更换 / 移除 / 关闭）。
5. **对战 1 次**：正常播放战报；右上角：重播 / 跳到结尾 / 调速 / 高光 / 结算；
   左上角「返回配阵」。
6. **对战 100 次**：标定风格统计表。

## 二、站位与 config

- 槽位下标 → `position` 1~6 写入 config 每位英雄字段（空位跳过）。
- 也可手写 JSON：队级 `positions: [1,4,2]` 与 `heroes` 等长，或每位
  `"position": N`；缺省按出现序 1..n（见 `client_battle_bridge.build_setup_from_config`）。
- 战场播放落点见 `StanceLayout` / [rendering_layout.md](rendering_layout.md) §五。

## 三、数据流（HTTP 首选 · 子进程回退）

```
ManualSetupPanel (OnGUI 配阵)
  → ManualBattleBridge
      ① HTTP → battle_server.py
      ② 子进程回退 → client_battle_bridge.py
  → ManualBattleModels
```

- 服务端：`python battle/tools/battle_server.py`（默认 0.0.0.0:8017）。
- 客户端零结算：数值来自战报/统计 JSON。

## 四、文件

| 文件 | 职责 |
|---|---|
| `Test/ManualSetupPanel.cs` | 配阵页 UI（12 站位槽 / 池子 / 详情 / 统计） |
| `Test/ManualBattleBridge.cs` | HTTP + python 回退；config 带 position |
| `Test/ManualBattleModels.cs` | 目录/统计 JSON 模型 |
| `Units/StanceLayout.cs` | 播放侧 1~6 区域中心与休息点抖动 |
| `battle/tools/battle_server.py` | 常驻结算 HTTP |
| `battle/tools/client_battle_bridge.py` | 目录 / 单场 / 百场 |
