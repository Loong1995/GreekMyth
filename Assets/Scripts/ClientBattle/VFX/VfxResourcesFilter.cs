namespace ClientBattle.VFX
{
    /// <summary>Resources/ClientBattle/VFX 清单过滤：画廊 [1/8]、预热、离线 dump 共用。
    /// 备份/过渡件不得进 Ordinal 序号表（P-82）。</summary>
    public static class VfxResourcesFilter
    {
        public static bool IsOursGalleryItem(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("_bak_", System.StringComparison.Ordinal)) return false;
            if (name.EndsWith("_pre_magic", System.StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
