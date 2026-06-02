using UnityEngine;

/// <summary>
/// 外部事件网关 —— ROS2 AEB / 外部UI触发 / 特殊场景中断
/// 
/// ★ 注意：红灯停止、前车跟随、紧急制动等常规纵向控制已由 SubsumptionEngine 接管。
///     本类仅保留外部触发的中断信号（ROS2 AEB、手动UI强制刹车等）。
///     这些外部命令比包容式架构优先级更高，直接覆盖 SubsumptionEngine 输出。
/// 
/// 挂载到与 SimpleAutoDrive 同一 GameObject 上。
/// </summary>
[RequireComponent(typeof(SimpleCarController))]
public class SpecialSituations : MonoBehaviour
{
    [Header("外部 AEB 触发（ROS2 / UI）")]
    [SerializeField] private bool _externalAebActive = false;

    [Header("强制刹车（UI手动触发）")]
    [SerializeField] private bool _forceBrakeActive = false;

    [Header("可视化")]
    public bool showDebugGizmos = true;

    // ===== 内部引用 =====
    private SimpleAutoDrive _autoDrive;
    private SimpleCarController _carController;
    private Transform _carTransform;

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

    // ============================================================
    // 主入口 —— 返回 null 表示无特殊中断，走 SubsumptionEngine
    // 返回 VehicleCommand 表示外部中断，直接覆盖引擎输出
    // ============================================================
    public VehicleCommand? Handle()
    {
        if (_autoDrive == null || _carController == null) return null;

        // ★ 优先级最高：外部 AEB（ROS2 触发，论文 §5.3）
        if (_externalAebActive)
            return EmergencyBrakeCmd("[ROS2 AEB]");

        // ★ 手动强制刹车（UI Debug触发）
        if (_forceBrakeActive)
            return EmergencyBrakeCmd("[UI ForceBrake]");

        // 无外部中断 → 交由 SubsumptionEngine 处理
        return null;
    }

    // ============================================================
    // 紧急制动指令（供 ROS2 AEB 和 UI 刹车使用）
    // ============================================================
    private VehicleCommand EmergencyBrakeCmd(string source)
    {
        return new VehicleCommand
        {
            throttle  = -1f,
            steering  = 0f,
            isBraking = true
        };
    }

    // ============================================================
    // 公共接口
    // ============================================================

    /// <summary>ROS2 AEB 触发/解除紧急制动（论文 §5.3）</summary>
    public void TriggerExternalAEB(bool active)
    {
        _externalAebActive = active;
        Debug.Log($"[SpecialSituations] {name} 外部AEB: {(active ? "激活" : "解除")}");
    }

    /// <summary>UI Debug 面板强制刹车</summary>
    public void SetForceBrake(bool active)
    {
        _forceBrakeActive = active;
        Debug.Log($"[SpecialSituations] {name} 强制刹车: {(active ? "激活" : "解除")}");
    }

    /// <summary>查询外部AEB是否激活</summary>
    public bool IsExternalAEBActive() => _externalAebActive;

    /// <summary>查询是否强制刹车中</summary>
    public bool IsForceBrakeActive() => _forceBrakeActive;

    /// <summary>重置所有外部状态（模式切换时调用）</summary>
    public void ResetAll()
    {
        _externalAebActive = false;
        _forceBrakeActive = false;
    }

    // ============================================================
    // 调试可视化
    // ============================================================
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || _autoDrive == null || _carTransform == null) return;

        // AEB激活 → 红色警告球
        if (_externalAebActive || _forceBrakeActive)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_carTransform.position, 5f);
        }

        // 停止线区域
        if (_autoDrive.redLightAhead && _autoDrive.hasStopLineAhead)
        {
            Gizmos.color = Color.yellow;
            Vector3 stopPos = _autoDrive.nearestStopLinePos;
            Gizmos.DrawWireCube(stopPos, new Vector3(4f, 0.5f, 1f));
        }
    }
}