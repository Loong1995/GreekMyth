using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>把一份常驻特效实例的**播放相位**错开。
    ///
    /// 【为什么需要】同一个 key 挂在三个人身上时，三份实例是同一帧创建的，
    /// 于是三团电弧逐帧同步地闪——观众读到的不是"三个人各自带着雷"，
    /// 而是"一个动画被复制了三份"。这是所有常驻件（罩身/光环/场域）共有的问题，
    /// 与具体是哪一件无关，故做成通用工具而不是写在某个挂载点里。
    ///
    /// 做法两条，缺一不可：
    ///   ① **预演**：创建时先把粒子系统快进一段随机时间，起手就处在不同阶段；
    ///   ② **变速**：给每份实例一点速度失谐，否则预演的相位差会一直保持固定，
    ///      长时间看仍是"整齐地错开"，尤其是循环层。
    /// 三个互质频率失谐的思路与卡面呼吸（`CardIdleMotion`）同源。</summary>
    public static class VfxPhaseDesync
    {
        /// <summary>错相位：快进 0~<paramref name="maxLead"/> 秒，并把播放速度
        /// 在 1±<paramref name="speedJitter"/> 内随机失谐。</summary>
        public static void Apply(GameObject instance, float maxLead, float speedJitter)
        {
            if (instance == null) return;
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) return;

            float speed = 1f + Random.Range(-speedJitter, speedJitter);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.simulationSpeed = Mathf.Max(0.1f, main.simulationSpeed * speed);
            }

            if (maxLead <= 0f) return;
            float lead = Random.Range(0f, maxLead);
            foreach (var ps in systems)
            {
                // 只在最上层预演：Simulate 带 withChildren 会把子发射器一起推进，
                // 逐层各推一次会打乱层间相位（与"只在最上层 Play"同一条理由）。
                if (ps.transform.parent != null
                    && ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                    continue;
                ps.Simulate(lead, true, true);
                ps.Play(true);
            }
        }
    }
}
