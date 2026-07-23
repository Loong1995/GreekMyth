# 手动配阵测试页（ManualSetupPanel）

> 客户端内配阵 → 调 Python 结算 → 播战报 / 出百场统计。仅编辑器与 PC
> 独立版可用（需仓库目录结构完整 + PATH 有 `python`）。

## 一、用法

1. 新建空场景（相机 + 灯），空物体挂 `ClientBattle/Test/ManualSetupPanel.cs`
   （**不需要** BattleReportTester）。
2. Play 后自动加载武将目录（python 导出，约 1 秒）。
3. 页面布局：6 个武将位横排——左 3 = A 队（蓝），右 3 = B 队（红），
   中间「对战 1 次 / 对战 100 次」按钮 + 种子。
4. 交互：
   - 点空位 → 武将备选池（32 将，同队同模板置灰）；点即上阵。
   - 拖已上阵武将到另一位 → 换位/互换（造成同队重复会自动回滚）。
   - 每将 3 战法格：◆ 自带 + 2 可配空格（显示 ＋）；点 ＋ → 战法池
     （32 个拆解战法，含类型/触发率），选中后点「装配」。
   - 点武将卡 / 战法格 → 详情弹窗（属性/阵营/类型/触发率），带
     「更换 / 移除（卸下）/ 关闭」。
5. **对战 1 次**：正常播放战报；右上角同 Tester：重播 / 跳到结尾 / 调速 /
   高光回放 / 打开结算；左上角「返回配阵」。播完自动弹结算表。
6. **对战 100 次**：弹标定脚本风格统计——平均结束回合、胜率、
   两队死/伤/余均值、各武将各技能平均释放次数与伤害。

## 二、数据流（HTTP 首选 · 子进程回退）

```
ManualSetupPanel (OnGUI 配阵)
  → ManualBattleBridge
      ① HTTP（首选，iOS/真机唯一通道）→ battle_server.py 常驻服务
           GET  /catalog        武将/战法目录
           POST /battle         {config, seed} → 单场战报（Runner 播放）
           POST /stats          {config, n, seed} → 百场统计
      ② 子进程回退（仅编辑器/桌面 + 同机仓库）：HTTP 不通时
           python battle/tools/client_battle_bridge.py …
  → ManualBattleModels（Newtonsoft 反序列化）
```

- **服务端**：`python battle/tools/battle_server.py`（默认 0.0.0.0:8017，
  stdlib 零依赖，ThreadingHTTPServer；/health 探活）。
- 页面左下可改服务地址并「连接」（发 iOS/局域网真机时填服务器 IP）。
- 页脚显示当前结算通道：服务器 / 本机 python（回退）。
- config 结构同 `manual_battle.py --example`；跨队同模板自动改名「XX（敌）」。
- 客户端仍零结算：所有数值来自服务端战报/统计 JSON。
- 战法备选池 = 全注册战法去掉自带/隐藏/标定/basic_attack（32 个拆解战法）。

## 三、独立包与 iOS 准备

- 专用场景 `Assets/Scenes/ManualBattle.unity`（相机 + 灯 + ManualSetupPanel）。
- Windows 独立包：`Builds/ManualBattle/GreekMythManual.exe`
  （Unity 构建，仅含该场景）。同机开着 battle_server 即可玩；
  没开服务且同机有仓库+python 时自动回退子进程。
- iOS：子进程通道被条件编译排除（非 EDITOR/STANDALONE），
  只走 HTTP——出 iOS 包前把面板 ServerUrl 指到可达的服务器地址即可。

## 四、文件

| 文件 | 职责 |
|---|---|
| `Test/ManualSetupPanel.cs` | 配阵页 UI（槽位/池子/详情/统计弹窗），OnGUI |
| `Test/ManualBattleBridge.cs` | HTTP 客户端 + python 子进程回退，后台线程轮询 |
| `Test/ManualBattleModels.cs` | 目录/统计 JSON 模型 |
| `battle/tools/battle_server.py` | 常驻结算 HTTP 服务（/catalog /battle /stats） |
| `battle/tools/client_battle_bridge.py` | 命令行版：目录导出 / 单场战报 / 百场统计 |
