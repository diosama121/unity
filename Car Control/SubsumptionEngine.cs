using UnityEngine;

/// <summary>
/// 包容式架构 (Subsumption Architecture) 纵向控制引擎
/// 论文 §4.2.1 - 公式 2-13
/// 
/// 4 层架构按优先级从高到低：
///   L3 紧急制动 — 碰撞不可避免时，A3=1 抢夺控制权
///   L2 跟车避障 — 前车距离 < 安全阈值，A2=1 抢夺控制权
///   L1 交通规则 — 红绿灯/停止线，A1=1 抢夺控制权
///   L0 基础巡航 — 车道保持，A0≡1 永远激活
/// 
/// 布尔仲裁矩阵：O = Σ ok · Ak · Π(1 - Aj)  for j > k
/// 即：高层激活时，低层输出被屏蔽，由最高激活层提供最终输出。
/// 
/// 时间窗防抖：候选层需持续满足 0.5s 才允许切换，防止状态抖动。
/// </summary>
public class SubsumptionEngine
{
    // ============================================================
    // 输出结构
    // ============================================================
    public struct LayerOutput
    {
        public float throttle;      // -1(全倒车) ~ 1(全油门)
        public float brake;         // 0(不刹) ~ 1(全力刹)
        public float targetSpeed;   // 目标速度 m/s
        public bool isHardBrake;    // 急刹标志（用于视觉反馈）
        public LongitudinalState state; // 当前激活层对应的纵向状态
    }

    public struct SensorInput
    {
        public float currentSpeed;
        public float maxSpeed;
        public float maxAcceleration;
        public float maxDeceleration;
        public float frontDistance;
        public float safeDistance;
        public float emergencyBrakeDist;
        public float frontSpeed;          // 前车速度 m/s
        public bool redLightAhead;
        public bool pedestrianDanger;
        public float distToStopLine;
        public float targetCruiseSpeed;   // 期望巡航速度
    }

    // ============================================================
    // 公开属性
    // ============================================================
    public int ActiveLayer { get; private set; }
    public LongitudinalState ActiveState { get; private set; }

    // ============================================================
    // 时间窗防抖 (论文 §4.2.1)
    // ============================================================
    private float _debounceTimer = 0f;
    private int _pendingLayer = -1;
    private int _lastActiveLayer = 0;
    private const float DEBOUNCE_WINDOW = 0.5f;

    // ============================================================
    // 主仲裁入口
    // ============================================================
    public LayerOutput Resolve(SensorInput s, float dt)
    {
        // 1. 计算激活标志位 Ak
        bool A3 = (s.frontDistance < s.emergencyBrakeDist) || s.pedestrianDanger;
        bool A2 = s.frontDistance < s.safeDistance;
        bool A1 = s.redLightAhead;

        // 2. 计算各层输出
        LayerOutput L0 = ComputeL0(s);
        LayerOutput L1 = ComputeL1(s);
        LayerOutput L2 = ComputeL2(s);
        LayerOutput L3 = ComputeL3(s);

        // 3. 确定候选层（最高激活层）
        int candidateLayer = 0;
        if (A3) candidateLayer = 3;
        else if (A2) candidateLayer = 2;
        else if (A1) candidateLayer = 1;
        else candidateLayer = 0;

        // 4. 时间窗防抖
        candidateLayer = ApplyDebounce(candidateLayer, dt);
        _lastActiveLayer = candidateLayer;
        ActiveLayer = candidateLayer;

        // 5. 返回对应层输出
        LayerOutput result;
        switch (candidateLayer)
        {
            case 3: result = L3; ActiveState = LongitudinalState.Brake; break;
            case 2: result = L2; ActiveState = LongitudinalState.FollowCar; break;
            case 1: result = L1; ActiveState = (s.distToStopLine < 2f) ? LongitudinalState.Stopped : LongitudinalState.Brake; break;
            default: result = L0; ActiveState = LongitudinalState.FreeDrive; break;
        }
        return result;
    }

    // ============================================================
    // L0 基础巡航层 (A0≡1, 永远激活)
    // ============================================================
    private LayerOutput ComputeL0(SensorInput s)
    {
        float speedDiff = s.targetCruiseSpeed - s.currentSpeed;
        float throttle = Mathf.Clamp(speedDiff / Mathf.Max(s.maxSpeed, 1f), -0.3f, 1f);
        return new LayerOutput
        {
            throttle = throttle,
            brake = 0f,
            targetSpeed = s.targetCruiseSpeed,
            isHardBrake = false,
            state = LongitudinalState.FreeDrive
        };
    }

    // ============================================================
    // L1 交通规则层 (红绿灯/停止线)
    // ============================================================
    private LayerOutput ComputeL1(SensorInput s)
    {
        // 距停止线越近，减速度越大
        float stopDist = Mathf.Max(s.distToStopLine, 0.1f);
        float brake = Mathf.Clamp01(1f - stopDist / 18f); // 18m 内开始线性增强刹车
        float targetSpd = Mathf.Lerp(0f, s.currentSpeed, stopDist / 18f);

        return new LayerOutput
        {
            throttle = -brake,
            brake = brake,
            targetSpeed = targetSpd,
            isHardBrake = false,
            state = LongitudinalState.Stopped
        };
    }

    // ============================================================
    // L2 跟车避障层
    // ============================================================
    private LayerOutput ComputeL2(SensorInput s)
    {
        // 匹配前车速度，保持安全距离
        float distRatio = Mathf.Clamp01(s.frontDistance / s.safeDistance);
        float targetSpd = Mathf.Lerp(0f, s.frontSpeed, distRatio);
        targetSpd = Mathf.Max(targetSpd, 1f); // 最低 1m/s，不跟停

        float speedDiff = targetSpd - s.currentSpeed;
        float throttle = Mathf.Clamp(speedDiff / Mathf.Max(s.maxSpeed, 1f), -0.5f, 1f);

        return new LayerOutput
        {
            throttle = throttle,
            brake = s.currentSpeed > targetSpd ? Mathf.Clamp01((s.currentSpeed - targetSpd) / s.maxSpeed) : 0f,
            targetSpeed = targetSpd,
            isHardBrake = false,
            state = LongitudinalState.FollowCar
        };
    }

    // ============================================================
    // L3 紧急制动层 (最高优先级)
    // ============================================================
    private LayerOutput ComputeL3(SensorInput s)
    {
        return new LayerOutput
        {
            throttle = -1f,
            brake = 1f,
            targetSpeed = 0f,
            isHardBrake = true,
            state = LongitudinalState.Brake
        };
    }

    /// <summary>
    /// 时间窗防抖：候选层需持续满足 0.5s 才切换
    /// 论文 §4.2.1
    /// </summary>
    private int ApplyDebounce(int candidateLayer, float dt)
    {
        if (candidateLayer == _lastActiveLayer)
        {
            // 层不变，重置所有防抖
            _pendingLayer = -1;
            _debounceTimer = 0f;
            return candidateLayer;
        }

        if (candidateLayer == _pendingLayer)
        {
            // 同一候选层，继续计时
            _debounceTimer += dt;
            if (_debounceTimer >= DEBOUNCE_WINDOW)
            {
                // 0.5s 坚持，允许切换
                _pendingLayer = -1;
                _debounceTimer = 0f;
                return candidateLayer;
            }
            else
            {
                return _lastActiveLayer; // 还没到时间，维持旧层
            }
        }
        else
        {
            // 新候选层，重置计时器
            _pendingLayer = candidateLayer;
            _debounceTimer = 0f;
            return _lastActiveLayer; // 维持旧层
        }
    }

    /// <summary>
    /// 重置防抖状态（手动模式切换时调用）
    /// </summary>
    public void ResetDebounce()
    {
        _pendingLayer = -1;
        _debounceTimer = 0f;
        _lastActiveLayer = 0;
        ActiveLayer = 0;
        ActiveState = LongitudinalState.FreeDrive;
    }
}