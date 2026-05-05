using UnityEngine;

/// <summary>
/// 纯净版车辆控制器 (支持 NPC 纯数学运动优化)
/// 功能：玩家模式下使用物理驱动；NPC模式下脱离物理引擎，使用数学插值贴合地面
/// </summary>
public class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    public float maxSpeed = 30f;
    public float acceleration = 4f; 
    public float brakeDeceleration = 10f;
    public float steeringSpeed = 80f;
    public float maxSteeringAngle = 45f;

    [Header("控制模式")]
    [Tooltip("勾选听从ROS/AI，取消勾选听从键盘WASD")]
    public bool autoMode = false;

    [Header("性能优化 (NPC专用)")]
    [Tooltip("勾选后车辆将彻底脱离物理引擎，变为纯数学轨道车，极大降低CPU消耗")]
    public bool isNPC = false;
    [Tooltip("地面所在的Layer，用于NPC射线检测地面高度")]
    public LayerMask groundLayer = 1; 
    [Tooltip("NPC车辆距离地面的悬挂高度")]
    public float npcSuspensionHeight = 0.5f;
    [Tooltip("NPC车辆用于检测地面的射线长度")]
    public float npcRaycastLength = 2f;

    [Header("调试信息")]
    public float currentSpeed = 0f;
    public float currentSteeringAngle = 0f;

    [Header("物理环境")]
    public float slipFactor = 0.5f;
   
    private RaycastSensor sensor;
    // 内部变量
    private Rigidbody rb;
    private float targetSpeed = 0f;
    private float targetSteering = 0f;
    
    // 缓存所有的碰撞体，用于NPC模式下批量禁用
    private Collider[] allColliders;

    // 外部控制接口
    private float autoThrottle = 0f;
    private float autoSteering = 0f;

    void Awake()
    {
        // 获取底盘及子物体（车轮）上的所有碰撞体
        allColliders = GetComponentsInChildren<Collider>();

        if (isNPC)
        {
            // 【NPC 性能优化核心】
            // 1. 禁用所有碰撞体，彻底脱离物理引擎的碰撞检测计算
            foreach (var col in allColliders)
            {
                col.enabled = false;
            }
            Debug.Log($"✅ {gameObject.name} 已切换为 NPC 纯数学模式，物理碰撞已禁用。");
        }
        else
        {
            // 玩家模式：确保碰撞体开启
            foreach (var col in allColliders)
            {
                col.enabled = true;
            }
        }
    }

    void Start()
    {
        rb = GetComponentInParent<Rigidbody>();
        if (rb == null && !isNPC)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        // 仅在非 NPC 模式下配置物理属性
        if (!isNPC && rb != null)
        {
            rb.mass = 1500f;
            rb.drag = 0.5f;
            rb.angularDrag = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.None;
            rb.angularDrag = 8f;
        }
        // 如果是 NPC，确保刚体是 Kinematic 的（即使碰撞体被禁用，这也是个好习惯）
        else if (isNPC && rb != null)
        {
            rb.isKinematic = true;
        }

        sensor = GetComponent<RaycastSensor>();
        this.enabled = true;
    }

    void Update()
    {
        // 1. 获取驾驶意图
        if (!isNPC)
        {
            currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
        }
        else
        {
            // NPC 模式下，currentSpeed 仅用于视觉展示或简单的数学记录
            // 这里我们让它平滑趋近于目标速度
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 2f);
        }
    
        if (autoMode) HandleAutoDrive();
        else HandleManualControl();

        // 更新面板显示
        currentSteeringAngle = targetSteering;
    }

    // 物理更新 (仅玩家模式使用)
    void FixedUpdate()
    {
        if (isNPC || rb == null) return;

        // --- 玩家原有的物理逻辑保持不变 ---
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.fixedDeltaTime * 3f);

        RaycastHit hit;
        bool isGrounded = Physics.Raycast(
            transform.position + Vector3.up * 0.3f,
            Vector3.down, out hit, 2.0f
        );

        if (isGrounded)
        {
            Quaternion slopeRot = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized,
                hit.normal
            );
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, slopeRot, Time.fixedDeltaTime * 8f));
        }

        ApplySteering();

        Vector3 moveDir = isGrounded
            ? Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized
            : transform.forward;

        Vector3 newVelocity = moveDir * currentSpeed;
        if (!isGrounded) newVelocity.y = rb.velocity.y;

        Vector3 localVel = transform.InverseTransformDirection(newVelocity);
        localVel.x *= slipFactor;
        rb.velocity = transform.TransformDirection(localVel);
    }

    // 纯数学更新 (仅 NPC 模式使用)
    void LateUpdate()
    {
        if (!isNPC) return;

        // 1. 纯数学位移计算 (根据 throttle 直接累加位置)
        // 使用 transform.forward 保证朝着车头方向移动
        float moveStep = targetSpeed * Time.deltaTime;
        transform.position += transform.forward * moveStep;

        // 2. 纯数学转向计算 (根据 steering 直接旋转车身)
        if (Mathf.Abs(targetSpeed) > 0.01f)
        {
            // 简单的阿克曼转向近似公式：角速度 = (速度 * tan(转向角)) / 轴距
            // 这里为了简化，直接复用你原本的转向逻辑系数
            float speedFactor = Mathf.Pow(1f - (Mathf.Abs(targetSpeed) / maxSpeed), 2);
            float normalizedSteering = targetSteering / maxSteeringAngle;
            float turnAmount = normalizedSteering * speedFactor * steeringSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        // 3. 地形高度贴合 (Raycast 强制贴合地面)
        RaycastHit hit;
        // 从车身中心稍高的位置向下发射射线
        Vector3 rayOrigin = transform.position + Vector3.up * 1.0f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, npcRaycastLength, groundLayer))
        {
            // 强行设置 Y 轴坐标，使其贴合地面高度 + 悬挂高度
            Vector3 pos = transform.position;
            pos.y = hit.point.y + npcSuspensionHeight;
            transform.position = pos;

            // 根据地面法线微调车身的 Pitch (俯仰) 和 Roll (侧倾)
            // 计算车头朝向在地面法线上的投影方向
            Vector3 forwardOnSlope = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
            if (forwardOnSlope != Vector3.zero)
            {
                // 使用 LookRotation 让车身根据地面法线自动调整倾角
                Quaternion targetRot = Quaternion.LookRotation(forwardOnSlope, hit.normal);
                // 使用 Slerp 进行平滑插值，避免车身角度突变抖动
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            }
        }
    }

    void HandleManualControl()
    {
        float throttle = Input.GetAxis("Vertical");
        float steering = Input.GetAxis("Horizontal");

        if (throttle > 0)
        {
            targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f);
        }
        else if (throttle < 0)
        {
            if (currentSpeed > 0.5f) targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 5f); 
            else targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f); 
        }
        else
        {
            targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f); 
        }

        targetSteering = steering * maxSteeringAngle;

        if (Input.GetKey(KeyCode.Space)) targetSpeed = 0f;
    }

    void HandleAutoDrive()
    {
        if (autoThrottle >= 0)
        {
            targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 2f);
        }
        else
        {
            targetSpeed = maxSpeed * autoThrottle; 
        }
        targetSteering = autoSteering * maxSteeringAngle;
    }

    void ApplySteering()
    {
        // 玩家物理转向逻辑 (NPC 模式下不会执行到这里)
        if (Mathf.Abs(currentSpeed) > 0.01f)
        {
            float speedFactor = Mathf.Pow(1f - (Mathf.Abs(currentSpeed) / maxSpeed), 2);
            float normalizedSteering = targetSteering / maxSteeringAngle;
            float turnRate = normalizedSteering * speedFactor * steeringSpeed * Time.fixedDeltaTime;
            transform.Rotate(0, turnRate, 0);
        }
    }

    // ========== 极简的对外接口 ==========
    public void SetAutoControl(float throttle, float steering)
    {
        this.autoThrottle = throttle;
        this.autoSteering = steering;
    }

    public float GetSpeed() { return currentSpeed; }
    public Vector3 GetPosition() { return transform.position; }
    public Quaternion GetRotation() { return transform.rotation; }

    public void ToggleMode()
    {
        autoMode = !autoMode;
        SimpleAutoDrive autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive != null && !autoMode)
        {
            autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
            SetAutoControl(0f, 0f);
        }
    }
}