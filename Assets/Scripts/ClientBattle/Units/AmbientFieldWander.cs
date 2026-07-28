using UnityEngine;

namespace ClientBattle.Units
{
    /// <summary>让一处场域源在自己的圈里换点，而不是钉死在一点。
    ///
    /// 【为什么需要】雷暴钉死在一点时，观众读到的是"中心有个循环动画"，
    /// 不是"在打雷"。真实感来自**发生地不断变**，所以每隔一小段时间把源
    /// 挪到圈内的新点上；粒子层本身不动，是源在动，故不会打断任何一次发射。
    ///
    /// 只动 x/z：抬高是这处源的语义高度（近处贴地劈、远处天上闪），
    /// 让它随机上下会把两层的远近关系搅乱。</summary>
    public class AmbientFieldWander : MonoBehaviour
    {
        public float Radius;
        public float Interval;

        Vector3 _home;
        float _next;

        void OnEnable()
        {
            _home = transform.localPosition;
            _next = 0f;   // 首帧就换一次，避免所有源在开场同点
        }

        void Update()
        {
            if (Radius <= 0f || Interval <= 0f) return;
            _next -= Time.deltaTime;
            if (_next > 0f) return;
            _next = Interval;

            var offset = Random.insideUnitCircle * Radius;
            transform.localPosition = _home + new Vector3(offset.x, 0f, offset.y);
        }
    }
}
