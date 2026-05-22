using UnityEngine;

// ==========================================
// 决策层与执行层之间的唯一通信协议
// ==========================================
public enum VehiclePriority
{
    Normal,
    Emergency
}

public struct VehicleCommand
{
    public float throttle;  // -1 到 1，负值=倒车
    public float steering;  // -1 到 1，负值=左转
    public bool  isBraking; // 踩死刹车（覆盖 throttle）
}

// ==========================================
// 纯运动学执行层：只负责接收 VehicleCommand 并移动
// 不读输入、不做决策、不依赖 Rigidbody
// ==========================================
public class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    public float maxSpeed          = 30f;
    public float acceleration      = 4f;
    public float brakeDeceleration = 10f;
    public float steeringSpeed     = 80f;
    public float maxSteeringAngle  = 45f;

    [Header("车辆优先级")]
    public VehiclePriority vehiclePriority = VehiclePriority.Normal;

    [Header("NPC 专用")]
    public bool  isNPC               = false;
    public float npcSuspensionHeight = 0.5f;

    [Header("调试信息")]
    public float currentSpeed         = 0f;
    public float currentSteeringAngle = 0f;

    // --- 内部状态 ---
    private Vector3 _originalPosition;

    void Awake()
    {
        _originalPosition = transform.position;
    }

    // ==========================================
    // 核心接口：外部唯一入口，直接操作 currentSpeed / currentSteeringAngle
    // ==========================================
    public void ApplyCommand(VehicleCommand cmd)
    {
        if (cmd.isBraking)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, brakeDeceleration * Time.deltaTime);
        }
        else
        {
            float targetSpeed = cmd.throttle * maxSpeed;
            float accel = (Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed)) ? acceleration : brakeDeceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * Time.deltaTime);
        }
        currentSteeringAngle = cmd.steering * maxSteeringAngle;
    }

    // ==========================================
    // 纯运动学位移
    // ==========================================
    void Update()
    {
        // NPC 车辆位移完全由 SimpleAutoDrive.SnapToCurve 接管，此处不再移动/旋转，
        // 否则两套位移系统会在转弯时互相打架导致抽搐。
        if (isNPC)
        {
            ApplyGroundAlignment();
            return;
        }

        // 1. 位移
        float moveStep = currentSpeed * Time.deltaTime;
        transform.position += transform.forward * moveStep;

        // 2. 转向
        if (Mathf.Abs(currentSpeed) > 0.01f)
        {
            float turnAmount = currentSteeringAngle * (currentSpeed / maxSpeed) * Time.deltaTime;
            transform.Rotate(0f, turnAmount, 0f);
        }

        // 3. 贴地
        ApplyGroundAlignment();
    }

    // ==========================================
    // 贴地
    // ==========================================
    void ApplyGroundAlignment()
    {
        if (WorldModel.Instance == null) return;

        float baseY  = WorldModel.Instance.GetUnifiedHeight(transform.position.x, transform.position.z);
        float frontY = WorldModel.Instance.GetUnifiedHeight(
            transform.position.x + transform.forward.x,
            transform.position.z + transform.forward.z);

        Vector3 pos = transform.position;
        pos.y = baseY + npcSuspensionHeight;
        transform.position = pos;

        Vector3 slopeForward = new Vector3(transform.forward.x, frontY - baseY, transform.forward.z).normalized;

        float leftY  = WorldModel.Instance.GetUnifiedHeight(transform.position.x - transform.right.x, transform.position.z - transform.right.z);
        float rightY = WorldModel.Instance.GetUnifiedHeight(transform.position.x + transform.right.x, transform.position.z + transform.right.z);
        Vector3 slopeRight = new Vector3(transform.right.x, rightY - leftY, transform.right.z).normalized;

        Vector3 trueUp = Vector3.Cross(slopeForward, slopeRight).normalized;
        if (slopeForward.sqrMagnitude > 0.001f && trueUp.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(slopeForward, trueUp);
        }
    }

    // ==========================================
    // 查询接口
    // ==========================================
    public float     GetSpeed()     => currentSpeed;
    public Vector3   GetPosition()  => transform.position;
    public Quaternion GetRotation() => transform.rotation;

    public void ResetPosition()
    {
        if (WorldModel.Instance != null)
        {
            var nearest = WorldModel.Instance.GetNearestNode(transform.position);
            if (nearest != null)
            {
                Vector3 safePos = nearest.WorldPos;
                safePos.y = WorldModel.Instance.GetUnifiedHeight(safePos.x, safePos.z) + 1.0f;
                transform.position = safePos;
                transform.rotation = Quaternion.identity;
            }
        }
        else
        {
            transform.position = _originalPosition + Vector3.up * 1f;
        }

        currentSpeed         = 0f;
        currentSteeringAngle = 0f;
    }
}