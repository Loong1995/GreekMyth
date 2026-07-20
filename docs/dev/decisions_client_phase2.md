# Phase 2 客户端决策存档（C 系列）【历史文档】

> **历史存档，仅供追溯。** 本文件记录 Phase 2 旧客户端
> （`Assets/Scripts/Battle/`，已整体删除）的实现决策。2026-07-09 客户端按
> `client_perform.md` 重构为 `Assets/Scripts/ClientBattle/` 后，
> C-06/C-07/C-09/C-10 提到的 `SkillVfxTable`/`StatusVfxTable`/`VfxLibrary`/
> `VfxKit`/`CardView`/`CardFX.shader`/`T1~T7 模板` 等实现均已不存在，
> 现行实现与配置入口以 `docs/client/performance_mechanisms.md` 与
> `docs/client/client_battle_framework.md` 为准。
> 其中**仍然有效的玩法/表现级结论**：C-01 横屏上下布局、C-03 数字不缩略、
> C-04 跳过=静默快进、C-05 时长预算精神（现行节奏参数见 performance_mechanisms）。

## C-01 屏幕布局与卡牌站位

**横屏 + 卡牌上下占位**（2026-07-05 人工修订确认）：敌方 3 卡横排在上、
我方 3 卡横排在下（主将居中），中央留横向演出通道。卡牌比例约 3:4（竖卡）。
【确认状态】✅已人工确认（2026-07-05）。**现行 ClientBattle 沿用此布局**。

## C-02 单挑独立演出规格

暗场独立擂台 + 立绘对峙 + 武力对比条 + 三次交锋剪影；接受 ≤4.0s、拒绝 ≤2.0s；
允许付费采购资源。
【确认状态】✅已人工确认（2026-07-05）。
**现行实现为简化版**：压暗非参战者 + 横幅 + 卡牌三次对撞
（`PerformanceRunner.PlayDuel`），独立擂台/立绘条待美术资源到位再升级。

## C-03 飘字风格与数字缩略规则

10000 兵量级不缩略，直接显示原始整数；暴击金黄大号；同目标纵向堆叠。
【确认状态】✅已人工确认（2026-07-05）。
**现行实现**：`FloatingTextService`（TextMesh + 自绘 Update 动画，
纵向 stack 即时错位，无 0.1s 错帧；颜色方案见 performance_mechanisms §四）。

## C-04 跳过播放的表现

跳过 = 镜像静默快进，不播关键帧摘要。
【确认状态】✅已人工确认（2026-07-05）。
**现行实现**：`PerformanceRunner.SkipToEnd`（ApplySilently 落账）；
结算面板 UI 未实现，属待做项。

## C-05 模板时长预算终值

单体 ≤1.2s；全体 ≤2.0s；大招 cut-in ≤3.0s；单挑 ≤4.0s；2x 倍速时长×0.5。
【确认状态】✅已人工确认（2026-07-05）。预算精神沿用；现行节奏另有
零死帧原则 + ActionPause/GroupPause，见 performance_mechanisms。

## C-06 状态特效扩展机制（已废弃）

旧入口 `StatusVfxTable`。**现行入口**：`UnitAuraService.StatusAuraTable`
（常驻光环）+ `PerformanceDatabase.SpecialProfiles`（触发演出）。

## C-07 「自带机制触发」伤害的默认表现（已废弃）

旧方案 T7 天降+头像标。**现行方案**：`status_tick` 组根按 StatusTrigger 组
走 `DefaultPerformance`（弹道/群攻中心/近身，按配置），无头像标。

## C-09 / C-10 B2/B3 实现方式备案（已废弃）

CardFX.shader、CardView 代码工厂、VfxLibrary/VfxKit 三层回退、
VfxConfigBuilder、SfxService 11 键、OnGUI 最小 UI 等均属旧架构。
现行对应物：`UnitView`、`VFXManager`+`PlaceholderFactory` 三级回退、
`PerformanceDatabase`（SO+代码默认）、`SfxManager`、`BattleReportTester` OnGUI。
