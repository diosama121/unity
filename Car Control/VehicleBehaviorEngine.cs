using UnityEngine;

/// <summary>
/// 车辆行为决策引擎（已剥离接管层）。
/// 原 Subsumption 架构已移除，不再直接修改 autoDrive.targetSpeed 或调用 SetAutoBrake。
/// 决策权全部归 SimpleAutoDrive 的 FSM。
/// 保留此组件以避免 Prefab 引用丢失；Update 为空循环。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(SimpleAutoDrive))]
[RequireComponent(typeof(SimpleCarController))]
public class VehicleBehaviorEngine : MonoBehaviour
{
    [Header("速度基准（供外部参考，不再主动修改 targetSpeed）")]
    public float cruiseSpeedBase = 15f;

    void Start() { }
    void Update() { }
}