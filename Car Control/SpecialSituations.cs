using UnityEngine;

/// <summary>
/// 特殊场景处理器 —— 挂到与 SimpleAutoDrive 同一 GameObject 上
/// 负责：红灯停止、前车跟随、紧急制动、ROS2 AEB 外部触发
/// 
/// 调用流程：SimpleAutoDrive.Update() → 先问 SpecialSituations.Handle() → 有结果就用它的cmd → 否则走正常自动驾驶
/// </summary>
[RequireComponent(typeof(SimpleCarController))]
public class SpecialSituations : MonoBehaviour
{
    [Header("红灯停止")]
    public float redLightStopDistance = 18f;        // 距停止线多远开始减速
    public float stopLinePassedThreshold = -2f;     // 超过停止线2m后不强制停车
    public float redLightTargetStopDist = 2f;       // 最终停车位置距停止线多少米

    [Header("前车跟随")]
    public float safeFollowDistance = 6f;           // 与前车保持的安全距离
    public float followMatchSpeedFactor = 0.9f;     // 跟随速度系数（略慢于前车以便拉开距离）
    public float followReactionTime = 0.5f;         // 反应延迟（避免频繁加减速）

    [Header("紧急制动")]
    public float emergencyBrakeDist = 3.5f;         // 硬刹距离
    public float emergencyBrakeDecel = 15f;         // 紧急减速度 m/s²

    [Header("外部 AEB（ROS2）")]
    [SerializeField] private bool _externalAebActive = false;

    // ===== 内部引用 =====
    private SimpleAutoDrive _autoDrive;
    private SimpleCarController _carController;
    private Transform _carTransform;

    // ===== 跟随状态 =====
    private float _followTimer = 0f;
    private bool _isFollowing = false;
    private float _matchedSpeed = 0f;

    // ===== 红灯状态 =====
    private bool _isWaitingAtRed = false;
    private float _redWaitTimer = 0f;

    void Awake()
    {
        _autoDrive = GetComponent<SimpleAutoDrive>();
        _carController = GetComponent<SimpleCarController>();
        _carTransform = transform;
    }

    void Start()
    {
        if (_autoDrive == null)
            Debug.LogError($"[SpecialSituations] {name} 缺少 SimpleAutoDrive 组件！");
    }

    // ==========================================
    // 主入口：SimpleAutoDrive 每帧调用
    // 返回 null = 无特殊场景，走正常驾驶
    // 返回 VehicleCommand = 特殊场景指令，替换正常驾驶
    // ==========================================
    public VehicleCommand? Handle()
    {
        if (_autoDrive == null || _carController == null) return null;

        // 优先级从高到低：
        // 1. 外部 AEB（ROS2 触发）
        if (_externalAebActive)
            return EmergencyBrakeCmd();

        // 2. 碰撞紧急制动
        if (_autoDrive.frontDistance < emergencyBrakeDist)
            return EmergencyBrakeCmd();

        // 3. 红灯停车
        if (_autoDrive.redLightAhead)
        {
            VehicleCommand? redCmd = HandleRedLight();
            if (redCmd.HasValue) return redCmd;
        }

        // 4. 前车跟随
        if (_autoDrive.frontDistance < safeFollowDistance && _autoDrive.frontDistance > emergencyBrakeDist)
        {
            VehicleCommand? followCmd = HandleFrontFollow();
            if (followCmd.HasValue) return followCmd;
        }
        else
        {
            _isFollowing = false;
            _followTimer = 0f;
        }

        _isWaitingAtRed = false;
        return null;
    }

    // ==========================================
    // 红灯停止
    // ==========================================
    VehicleCommand? HandleRedLight()
    {
        float distToStop = _autoDrive.distToStopLine;
        float currentSpeed = _carController.currentSpeed;

        // 已越过停止线 → 放行
        if (distToStop < stopLinePassedThreshold)
        {
            _isWaitingAtRed = false;
            return null;
        }

        // 已经很近且速度很低 → 刹停等待
        if (distToStop <= redLightTargetStopDist && currentSpeed < 0.5f)
        {
            _isWaitingAtRed = true;
            _redWaitTimer += Time.deltaTime;
            return new VehicleCommand { throttle = 0f, steering = 0f, isBraking = true };
        }

        // 接近停止线 → 减速
        if (distToStop < redLightStopDistance)
        {
            float speedLimit = Mathf.Lerp(0.5f, _autoDrive.targetSpeed,
                (distToStop - redLightTargetStopDist) / (redLightStopDistance - redLightTargetStopDist));
            speedLimit = Mathf.Max(speedLimit, 0.5f);

            if (currentSpeed > speedLimit)
                return new VehicleCommand
                {
                    throttle = Mathf.Clamp(speedLimit / Mathf.Max(_carController.maxSpeed, 1f), -0.2f, 1f),
                    steering = 0f,
                    isBraking = false
                };
        }

        return null;
    }

    // ==========================================
    // 前车跟随
    // ==========================================
    VehicleCommand? HandleFrontFollow()
    {
        float frontDist = _autoDrive.frontDistance;
        float currentSpeed = _carController.currentSpeed;
        float frontSpeed = _autoDrive.frontSpeed;

        // 反应延迟：避免频繁切进切出
        if (!_isFollowing)
        {
            _followTimer += Time.deltaTime;
            if (_followTimer < followReactionTime) return null;
            _isFollowing = true;
            _followTimer = 0f;
            _matchedSpeed = Mathf.Max(frontSpeed * followMatchSpeedFactor, 1f);
        }

        // 距离误差 → 调速
        float distError = frontDist - safeFollowDistance;

        // 太近 → 减速
        if (distError < 0f)
        {
            float brakeStrength = Mathf.InverseLerp(0f, -5f, distError);
            if (brakeStrength > 0.8f)
                return EmergencyBrakeCmd();

            float targetSpeed = _matchedSpeed * (1f - brakeStrength * 0.7f);
            targetSpeed = Mathf.Max(targetSpeed, 0.5f);

            return new VehicleCommand
            {
                throttle = Mathf.Clamp(targetSpeed / Mathf.Max(_carController.maxSpeed, 1f), -0.2f, 1f),
                steering = 0f,
                isBraking = brakeStrength > 0.5f
            };
        }

        // 距离合适 → 匹配前车速度
        if (distError < 3f)
        {
            return new VehicleCommand
            {
                throttle = Mathf.Clamp(_matchedSpeed / Mathf.Max(_carController.maxSpeed, 1f), -0.2f, 1f),
                steering = 0f,
                isBraking = false
            };
        }

        // 距离太远 → 恢复正常驾驶
        _isFollowing = false;
        _followTimer = 0f;
        return null;
    }

    // ==========================================
    // 紧急制动
    // ==========================================
    VehicleCommand EmergencyBrakeCmd()
    {
        return new VehicleCommand
        {
            throttle = -1f,
            steering = 0f,
            isBraking = true
        };
    }

    // ==========================================
    // 外部接口（ROS2 AEB / UI 触发）
    // ==========================================

    /// <summary>ROS2 AEB 触发紧急制动</summary>
    public void TriggerExternalAEB(bool active)
    {
        _externalAebActive = active;
        Debug.Log($"[SpecialSituations] {name} 外部AEB: {(active ? "激活" : "解除")}");
    }

    /// <summary>查询当前是否在红灯等待中</summary>
    public bool IsWaitingAtRedLight() => _isWaitingAtRed;

    /// <summary>查询是否在跟随前车</summary>
    public bool IsFollowingCar() => _isFollowing;

    /// <summary>查询外部AEB是否激活</summary>
    public bool IsExternalAEBActive() => _externalAebActive;

    // ==========================================
    // 调试可视化
    // ==========================================
    void OnDrawGizmosSelected()
    {
        if (_autoDrive == null) return;

        // 红灯停止线区域
        if (_autoDrive.redLightAhead && _autoDrive.hasStopLineAhead)
        {
            Gizmos.color = _isWaitingAtRed ? Color.red : Color.yellow;
            Vector3 stopPos = _autoDrive.nearestStopLinePos;
            Gizmos.DrawWireCube(stopPos, new Vector3(4f, 0.5f, 1f));
            Vector3 brakeStart = stopPos + _carTransform.forward * redLightStopDistance;
            Gizmos.DrawLine(stopPos, brakeStart);
        }

        // 跟随距离
        if (_autoDrive.frontDistance < safeFollowDistance)
        {
            Gizmos.color = _isFollowing ? Color.cyan : Color.gray;
            Vector3 target = _carTransform.position + _carTransform.forward * safeFollowDistance;
            Gizmos.DrawWireSphere(target, 1f);
        }
    }
}