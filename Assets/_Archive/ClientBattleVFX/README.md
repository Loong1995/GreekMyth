# ClientBattle VFX 归档（不进画廊、不进包体）

从 `Resources/ClientBattle/VFX/` 移出的备份/过渡 prefab：

- `_bak_*`：换料前备份
- `*_pre_magic`：Magic Pack 接线前的过渡件

**禁止**再放回 Resources——会进入 `LoadAll`，把画廊 [1/8] Ordinal 序号整体顶偏（P-82），并被 `VFXManager.Prewarm` 白白预热。
