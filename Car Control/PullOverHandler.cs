using UnityEngine;

/// <summary>
/// 直路让行靠边处理器
/// 当检测到优先车辆（紧急车辆）时，车辆减速并向右偏移到车道边缘，
/// 待优先车辆通过后，平滑返回车道中心线。
///
/// 规则：
/// - 仅在直路（Lane 非 Connector）且远离路口时允许靠边
/// - 横向偏移量由 pullOverDistance 决定
/// - 靠边和返回都有独立的平滑速度
/// </summary>
public class PullOverHandler
{
    // ============================================================
    // 配置
    // ============================================================
    [Header("靠边参数")]
    public float pullOverDistance = 2.5f;   // 向右偏移最大距离（米）
    public float deploySpeed = 1.8f;        // 靠边横向速度（m/s）
    public float returnSpeed = 1.2f;        // 返回横向速度（m/s）

    // ============================================================
    // 公开属性
    // ============================================================
    /// <summary>当前是否处于靠边状态</summary>
    public bool IsActive { get; private set; }
    /// <summary>当前横向偏移量（米，正值=向右）</summary>
    public float CurrentOffset { get; private set; }
    /// <summary>是否已完全靠边（达到目标偏移量）</summary>
    public bool IsFullyDeployed => Mathf.Abs(CurrentOffset - pullOverDistance) < 0.05f;
    /// <summary>是否已完全返回中心线</summary>
    public bool IsFullyReturned => Mathf.Abs(CurrentOffset) < 0.02f;

    // ============================================================
    // 内部状态
    // ============================================================
    private bool _targetActive = false;     // 是否希望靠边（外部激活）
    private bool _isReturning = false;      // 是否正在返回中

    // ============================================================
    // 主入口
    // ============================================================

    /// <summary>激活靠边动作</summary>
    public void Activate()
    {
        _targetActive = true;
        _isReturning = false;
    }

    /// <summary>解除靠边动作，开始返回中心线</summary>
    public void Deactivate()
    {
        _targetActive = false;
        _isReturning = true;
    }

    /// <summary>
    /// 每帧更新
    /// 返回 true 表示横向偏移有变化（需要重新设置位置）
    /// </summary>
    public bool Update(float dt)
    {
        float prevOffset = CurrentOffset;

        if (_targetActive)
        {
            // 靠边：向右移动
            float speed = deploySpeed;
            CurrentOffset = Mathf.MoveTowards(CurrentOffset, pullOverDistance, speed * dt);
            IsActive = true;
        }
        else if (_isReturning)
        {
            // 返回：向中心线移动
            float speed = returnSpeed;
            CurrentOffset = Mathf.MoveTowards(CurrentOffset, 0f, speed * dt);
            IsActive = CurrentOffset > 0.01f;
            if (!IsActive)
                _isReturning = false;
        }

        return Mathf.Abs(CurrentOffset - prevOffset) > 0.001f;
    }

    /// <summary>
    /// 根据车道切线方向，计算世界空间下的横向偏移向量
    /// </summary>
    /// <param name="laneTangent">车道切线方向（世界空间）</param>
    /// <returns>横向偏移向量（世界空间）</returns>
    public Vector3 GetLateralOffset(Vector3 laneTangent)
    {
        if (Mathf.Abs(CurrentOffset) < 0.001f)
            return Vector3.zero;

        // 右方向 = Up × Forward（Unity坐标系）
        Vector3 right = Vector3.Cross(Vector3.up, laneTangent).normalized;
        return right * CurrentOffset;
    }

    /// <summary>
    /// 重置状态（模式切换时调用）
    /// </summary>
    public void Reset()
    {
        CurrentOffset = 0f;
        IsActive = false;
        _targetActive = false;
        _isReturning = false;
    }
}