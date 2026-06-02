using UnityEngine;

/// <summary>
/// 紧急车辆让行处理器 (论文 §5.4 TF-2)
/// 检测到救护车警报广播后：
///   1. 普通车辆减速到 5 km/h 并靠右
///   2. 路口等待避让，不进入路口
///   3. 形成"优先通行带"
/// </summary>
public class EmergencyYieldHandler
{
    // ============================================================
    // 配置
    // ============================================================
    public float broadcastRange = 50f;      // 救护车警报广播范围 (m)
    public float yieldSpeed = 5f / 3.6f;    // 让行速度 (m/s)
    public float pullOverOffset = 1.5f;     // 靠边偏移量 (m)

    // ============================================================
    // 公开属性
    // ============================================================
    public bool IsYielding { get; private set; }
    public bool ShouldYieldAtIntersection { get; private set; }

    // ============================================================
    // 内部状态
    // ============================================================
    private TrafficManager _trafficManager;
    private float _yieldTimer = 0f;
    private const float YIELD_TIMEOUT = 5f; // 让行超时 (s)，超时后恢复

    /// <summary>
    /// 每帧调用 Update
    /// </summary>
    /// <param name="myPosition">本车位置</param>
    /// <param name="isEmergencyVehicle">本车是否是救护车</param>
    /// <param name="dt">帧时间</param>
    public void Update(Vector3 myPosition, bool isEmergencyVehicle, float dt)
    {
        if (_trafficManager == null)
        {
            _trafficManager = Object.FindObjectOfType<TrafficManager>();
            if (_trafficManager == null) return;
        }

        // 救护车自己不让行
        if (isEmergencyVehicle)
        {
            IsYielding = false;
            ShouldYieldAtIntersection = false;
            return;
        }

        // 检查是否有活跃救护车
        SimpleAutoDrive emergencyVehicle = _trafficManager.activeEmergencyVehicle;
        if (emergencyVehicle == null)
        {
            _yieldTimer += dt;
            if (_yieldTimer > YIELD_TIMEOUT)
            {
                IsYielding = false;
                ShouldYieldAtIntersection = false;
            }
            return;
        }

        // 检查是否在广播范围内
        float dist = Vector3.Distance(myPosition, emergencyVehicle.transform.position);
        if (dist < broadcastRange)
        {
            IsYielding = true;
            _yieldTimer = 0f;

            // 在路口附近时，不进入路口
            ShouldYieldAtIntersection = (dist < broadcastRange * 0.5f);
        }
        else
        {
            _yieldTimer += dt;
            if (_yieldTimer > YIELD_TIMEOUT)
            {
                IsYielding = false;
                ShouldYieldAtIntersection = false;
            }
        }
    }

    /// <summary>
    /// 获取让行时的目标速度
    /// </summary>
    public float GetYieldSpeed()
    {
        return yieldSpeed;
    }

    /// <summary>
    /// 获取让行时的靠边偏移方向（右转）
    /// </summary>
    public Vector3 GetPullOverDirection(Transform carTransform)
    {
        return -carTransform.right * pullOverOffset;
    }

    /// <summary>
    /// 重置
    /// </summary>
    public void Reset()
    {
        IsYielding = false;
        ShouldYieldAtIntersection = false;
        _yieldTimer = 0f;
    }
}