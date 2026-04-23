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
    [Header("🛡️ 主动安全系统 (AEB)")]
    public bool enableAEB = true;
    public float emergencyBrakeDistance = 2.5f;
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
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        
        sensor = GetComponent<RaycastSensor>();
        
        this.enabled = true;
    }

  void Update()
    {
        // 1. 获取驾驶意图 (WASD 或 ROS2/AI)
        if (autoMode) HandleAutoDrive();
        else HandleManualControl();

        // 🌟 2. 核心：全局 AEB 墙体防御 🌟
        // 无论上面获取到的 targetSpeed 有多快，只要要撞墙了，一票否决！
        if (enableAEB && sensor != null)
        {
            float dist = sensor.GetFrontDistance();
            
            // 只要扫到障碍物（dist>0），且进入危险距离，且车子正打算往前开（targetSpeed > 0.1f）
            if (dist > 0 && dist < emergencyBrakeDistance && targetSpeed > 0.1f)
            {
                targetSpeed = 0f;    // 抹杀目标动力
                currentSpeed = 0f;   // 抹杀当前动力
                
                // 物理引擎直接定死（保留Y轴重力防止悬空，彻底清零X和Z轴前进动力）
                if (rb != null)
                {
                    rb.velocity = new Vector3(0, rb.velocity.y, 0);
                }
                
                Debug.LogWarning($"🛑 AEB 触发！距离障碍物 {dist:F1}m，物理动力已切断！");
            }
        }

        // 3. 更新面板显示
        currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
        currentSteeringAngle = targetSteering;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // 1. 平滑过渡到目标速度
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.fixedDeltaTime * 3f);

        // 2. 赋予刚体速度 (严格保留Y轴重力)
        Vector3 newVelocity = transform.forward * currentSpeed;
        newVelocity.y = rb.velocity.y;
        rb.velocity = newVelocity;

        // 3. 转向逻辑
        ApplySteering();

        // 4. 防侧滑
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
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