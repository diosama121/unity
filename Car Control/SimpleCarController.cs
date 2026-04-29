using UnityEngine;

/// <summary>
/// 纯净版车辆控制器 (已移除所有 AEB 和防坠落逻辑)
/// 功能：纯粹的物理底层驱动，仅接收本地 WASD 或外部 ROS2/AI 指令
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

    // 外部控制接口
    private float autoThrottle = 0f;
    private float autoSteering = 0f;

    void Start()
    {
        // 最安全的刚体获取方式
        rb = GetComponentInParent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.Log("🔧 底盘已自动补齐 Rigidbody 组件！");
        }

        if (rb == null)
        {
            Debug.LogError("❌ 无法获取刚体！");
            this.enabled = false;
            return;
        }

        rb.mass = 1500f;
        rb.drag = 0.5f;
        rb.angularDrag = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // 🚨【核心修改】：在乡村模式下，必须允许 X 和 Z 轴旋转，车才能爬坡
        // 将原本的 RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ 改为 None
rb.constraints = RigidbodyConstraints.None;
// 用大阻尼防翻车代替约束
rb.angularDrag = 8f;

        sensor = GetComponent<RaycastSensor>();

        this.enabled = true;
    }

    void Update()
    {
        // 1. 获取驾驶意图 (WASD 或 ROS2/AI)
      currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
    
    if (autoMode) HandleAutoDrive();
    else HandleManualControl();

    

        // 3. 更新面板显示
        currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
        currentSteeringAngle = targetSteering;
    }

 void FixedUpdate()
{
    if (rb == null) return;

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

    // ✅ 转向先做，rotation 到位之后再算速度方向
    ApplySteering();

    // ✅ 用最新的 transform.forward 算速度，不会有一帧误差
    Vector3 moveDir = isGrounded
        ? Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized
        : transform.forward;

    Vector3 newVelocity = moveDir * currentSpeed;
    if (!isGrounded) newVelocity.y = rb.velocity.y;

    // ✅ 防侧滑在赋速度之前做，不要赋完再改
    Vector3 localVel = transform.InverseTransformDirection(newVelocity);
    localVel.x *= slipFactor;
    rb.velocity = transform.TransformDirection(localVel);
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
            if (currentSpeed > 0.5f) targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 5f); // 刹车
            else targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f); // 倒车
        }
        else
        {
            targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f); // 滑行减速
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
            targetSpeed = maxSpeed * autoThrottle; // 倒车不Lerp
        }
        targetSteering = autoSteering * maxSteeringAngle;
    }

    void ApplySteering()
    {
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
        // 没有任何安检，纯粹地接收指令
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