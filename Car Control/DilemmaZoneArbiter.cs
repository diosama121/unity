using UnityEngine;

/// <summary>
/// 黄灯困境仲裁器 (论文 §4.2.5)
/// 公式 2-14: 安全停止临界距离  Xs = v·τ + v²/(2·a_max)
/// 公式 2-15: 安全清空临界距离  Xc = v·T_yellow + ½·a_max·T_yellow² - W_intersection
/// 
/// 决策逻辑：
///   d > Xs          → 足够远，安全制动
///   d < Xc          → 足够近，保持通过
///   Xc ≤ d ≤ Xs     → 困境区，论文说"转入制动保护自身"
/// </summary>
public class DilemmaZoneArbiter
{
    // ============================================================
    // 配置
    // ============================================================
    public float reactionTime = 0.5f;          // τ 驾驶员反应时间 (s)
    public float defaultYellowDuration = 3f;   // 默认黄灯时长 (s)，无 TrafficLightController 时使用
    public float defaultIntersectionWidth = 15f; // 默认路口宽度 (m)

    // ============================================================
    // 公开属性
    // ============================================================
    public bool ShouldBrake { get; private set; }
    public bool IsInDilemmaZone { get; private set; }

    // ============================================================
    // 主入口
    // ============================================================
    /// <summary>
    /// 判断黄灯时是否应该刹车。
    /// 返回 true = 应刹车，false = 保持通过。
    /// </summary>
    /// <param name="distToStopLine">距离停止线 (m)</param>
    /// <param name="speed">当前速度 (m/s)</param>
    /// <param name="maxDeceleration">最大减速度 (m/s²)</param>
    /// <param name="yellowDuration">当前黄灯剩余时间 (s)，-1 时使用默认值</param>
    /// <param name="intersectionWidth">路口宽度 (m)，-1 时使用默认值</param>
    public bool Evaluate(float distToStopLine, float speed, float maxDeceleration,
                         float yellowDuration = -1f, float intersectionWidth = -1f)
    {
        if (yellowDuration < 0f) yellowDuration = defaultYellowDuration;
        if (intersectionWidth < 0f) intersectionWidth = defaultIntersectionWidth;

        float a_max = Mathf.Abs(maxDeceleration);
        if (a_max < 0.01f) a_max = 5f; // 安全兜底

        // 公式 2-14: 安全停止临界距离
        float Xs = speed * reactionTime + (speed * speed) / (2f * a_max);

        // 公式 2-15: 安全清空临界距离
        float Xc = speed * yellowDuration + 0.5f * a_max * yellowDuration * yellowDuration - intersectionWidth;

        IsInDilemmaZone = (distToStopLine >= Xc && distToStopLine <= Xs);

        if (distToStopLine > Xs)
        {
            ShouldBrake = true;   // 足够远，安全制动
            return true;
        }
        else if (distToStopLine < Xc)
        {
            ShouldBrake = false;  // 足够近，能清空路口
            return false;
        }
        else
        {
            ShouldBrake = true;   // 困境区 Xc ≤ d ≤ Xs，强制刹车保护自身
            return true;
        }
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        ShouldBrake = false;
        IsInDilemmaZone = false;
    }
}