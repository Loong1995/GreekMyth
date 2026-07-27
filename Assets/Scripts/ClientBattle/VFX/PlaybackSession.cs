using ClientBattle.Events;
using ClientBattle.Units;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】PlaybackSession：一次播放的全部可变状态容器（R-7.3）。
    //
    // 由 PlaybackWorldBuilder 装配、PerformanceRunner（控制器）持有；
    // 硬停止/重播时整体丢弃重建，禁止跨会话复用其中任何字段。
    // static 镜像账本（MomentumService/UnitAuraService）的 ClearAll 收口在
    // 会话建立（Builder）与销毁（Runner.HardStop/Teardown）两处。
    // =========================================================================

    /// <summary>播放生命周期状态机（迁移表见 docs/client/architecture.md §二）。</summary>
    public enum PlaybackState
    {
        Idle,       // 无会话
        Building,   // 建世界中
        Prewarming, // 等 VFX 离屏预热收尾
        Playing,    // 主循环推进中
        Finished,   // 播完（结算面板可见，世界仍在）
    }

    public class PlaybackSession
    {
        public BattleReport Report;
        public BattleBoardView Board;
        public VFXContext Ctx;
        public VFXResolver Resolver;
        /// <summary>编译好的播放流（开播前一次编译，运行期只读；
        /// 主循环/高光/Skip 三处消费同一份，禁止再自行跑管线）。</summary>
        public CompiledPlayback Compiled;

        public DefaultPerformance DefaultPerf;
        public OracleAuraPerformance OraclePerf;
        public DuelPerformance DuelPerf;

        public BattleSettlementSnapshot Settlement;
    }

    /// <summary>节奏参数只读口：Director/Builder 经此读控制器上的实时可调值，
    /// 不反向依赖 PerformanceRunner 具体类型。</summary>
    public interface IPlaybackPacing
    {
        float Speed { get; }
        float DurationMul { get; }
        float ActionPauseSeconds { get; }
        float GroupPauseSeconds { get; }
    }
}
