using UnityEngine;

/// <summary>
/// 死锁脱困处理器 (论文 §4.2.3)
/// 两阶段策略：
///   Stage 1 — 停留时间 > 10s，注入高斯分布随机扰动（微扰退火）
///   Stage 2 — 8m 范围内卡死超 5 次，强制重规划
/// </summary>
public class DeadlockRecovery
{
    // ============================================================
    // 配置
    // ============================================================
    public float stuckTimeThreshold = 10f;       // Stage1 触发阈值 (秒)
    public float perturbationDuration = 3f;       // 扰动持续时间 (秒)
    public float perturbationSteeringSigma = 0.3f; // 转向扰动 σ
    public float perturbationSpeedSigma = 0.2f;    // 速度扰动 σ
    public float stuckDistanceThreshold = 8f;      // Stage2 卡死范围 (米)
    public int maxStuckRetries = 5;                // Stage2 最大重试次数

    // ============================================================
    // 公开属性
    // ============================================================
    public bool IsPerturbating => _perturbationActive;
    public float PerturbationSteering => _perturbSteering;
    public float PerturbationSpeed => _perturbSpeed;
    public bool NeedsForceRepath { get; private set; }

    // ============================================================
    // 内部状态
    // ============================================================
    private float _stuckTimer = 0f;
    private int _stuckCount = 0;
    private Vector3 _lastPosition;
    private bool _firstFrame = true;

    private bool _perturbationActive = false;
    private float _perturbationTimer = 0f;
    private float _perturbSteering = 0f;
    private float _perturbSpeed = 0f;

    // ============================================================
    // 主入口 — 每帧调用
    // ============================================================
    public void Update(Vector3 currentPosition, float dt)
    {
        NeedsForceRepath = false;

        if (_firstFrame)
        {
            _lastPosition = currentPosition;
            _firstFrame = false;
            return;
        }

        float moved = Vector3.Distance(currentPosition, _lastPosition);
        _lastPosition = currentPosition;

        if (moved < 0.1f)
        {
            _stuckTimer += dt;
        }
        else
        {
            _stuckTimer = 0f;
            // 扰动中如果移动了，延长一点时间保出困
            if (_perturbationActive && moved > 0.5f)
            {
                _perturbationTimer = Mathf.Max(_perturbationTimer, perturbationDuration - 1f);
            }
        }

        // Stage 1: 停留 > 10s → 注入高斯随机扰动
        if (_stuckTimer > stuckTimeThreshold && !_perturbationActive)
        {
            ActivatePerturbation();
        }

        // Stage 1 持续中
        if (_perturbationActive)
        {
            _perturbationTimer += dt;
            if (_perturbationTimer > perturbationDuration)
            {
                DeactivatePerturbation();
                _stuckCount++;
                _stuckTimer = 0f;

                // Stage 2: 8m 内卡死超 5 次 → 强制重规划
                if (_stuckCount >= maxStuckRetries)
                {
                    NeedsForceRepath = true;
                    _stuckCount = 0;
                }
            }
        }

        // 如果移动了足够距离，重置卡死计数
        if (moved > stuckDistanceThreshold)
        {
            _stuckCount = 0;
        }
    }

    // ============================================================
    // 扰动控制
    // ============================================================
    private void ActivatePerturbation()
    {
        _perturbationActive = true;
        _perturbationTimer = 0f;
        _perturbSteering = GaussianRandom(0f, perturbationSteeringSigma);
        _perturbSpeed = GaussianRandom(0f, perturbationSpeedSigma);
    }

    private void DeactivatePerturbation()
    {
        _perturbationActive = false;
        _perturbationTimer = 0f;
        _perturbSteering = 0f;
        _perturbSpeed = 0f;
    }

    /// <summary>
    /// 重置状态（路径变更时调用）
    /// </summary>
    public void Reset()
    {
        _stuckTimer = 0f;
        _stuckCount = 0;
        _perturbationActive = false;
        _perturbationTimer = 0f;
        _perturbSteering = 0f;
        _perturbSpeed = 0f;
        _firstFrame = true;
        NeedsForceRepath = false;
    }

    // ============================================================
    // Box-Muller 高斯随机数生成器
    // ============================================================
    private float GaussianRandom(float mean, float stdDev)
    {
        // Box-Muller 变换
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        if (u1 < 0.0001f) u1 = 0.0001f;
        float randStdNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        return mean + stdDev * randStdNormal;
    }
}