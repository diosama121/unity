using UnityEngine;

/// <summary>
/// 简单车辆控制器
/// 功能：手动控制（WASD）+ 自动驾驶接口
/// 作者：
/// 日期：2025
/// </summary>
public class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    [Tooltip("最大速度 (m/s)")]
    public float maxSpeed = 25f;

    [Tooltip("加速度 (m/s²)")]
    public float acceleration = 4f;

    [Tooltip("制动减速度 (m/s²)")]
    public float brakeDeceleration = 10f;

    [Tooltip("转向速度 (度/秒)")]
    public float steeringSpeed = 80f;

    [Tooltip("最大转向角度")]
    public float maxSteeringAngle = 45f;

    [Header("控制模式")]
    [Tooltip("是否启用自动驾驶")]
    public bool autoMode = false;

    [Header("调试信息")]
    public float currentSpeed = 0f;
    public float currentSteeringAngle = 0f;
    [Header("物理环境")]
    [Tooltip("侧向滑动系数 (0.1=极强抓地, 0.5=正常干地, 0.9=雨雪湿滑)")]
    public float slipFactor = 0.5f;
    // 内部变量
    private Rigidbody rb;
    private float targetSpeed = 0f;
    private float targetSteering = 0f;

    // 外部控制接口（供自动驾驶使用）
    private float autoThrottle = 0f;  // -1 到 1
    private float autoSteering = 0f;  // -1 到 1

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError("车辆缺少 Rigidbody 组件！");
            enabled = false;
            return;
        }

        // 设置刚体参数
        rb.mass = 1500f;  // 1.5吨
        rb.drag = 0.5f;
        rb.angularDrag = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX
               | RigidbodyConstraints.FreezeRotationZ;
    }

    void Update()
    {
        if (autoMode) HandleAutoDrive();
        else HandleManualControl();

        // 有符号速度：前进正，后退负
        currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
        currentSteeringAngle = targetSteering;
    }
    void FixedUpdate()
    {
        ApplyAcceleration();
        ApplySteering();
        LimitSpeed();

        //  防侧滑
      Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        localVel.x *= slipFactor; // 将原来的 0.5f 替换为动态的 slipFactor
        rb.velocity = transform.TransformDirection(localVel);
    }

    /// <summary>
    /// 手动控制处理
    /// </summary>
    void HandleManualControl()
    {
        // WASD 控制
        float throttle = Input.GetAxis("Vertical");    // W/S
        float steering = Input.GetAxis("Horizontal");  // A/D

        if (throttle > 0)
        {
            targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f);
        }
        else if (throttle < 0)
        {
            // 【修复 1】分离刹车和倒车逻辑
            if (currentSpeed > 0.5f)
            {
                // 如果车还在往前开，按S键是刹车减速
                targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 5f);
            }
            else
            {
                // 如果车已经基本停下，按S键则是倒车
                targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f);
            }
        }
        else
        {
            // 无输入时缓慢减速
            targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f);
        }

        targetSteering = steering * maxSteeringAngle;

        if (Input.GetKey(KeyCode.Space))
        {
            targetSpeed = 0f;
        }
    }

    /// <summary>
    /// 自动驾驶处理
    /// </summary>
    void HandleAutoDrive()
{
    if (autoThrottle >= 0)
    {
        targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 2f);
    }
    else
    {
        // 倒车：直接设置不用Lerp，立即响应
        targetSpeed = maxSpeed * autoThrottle;
    }
    targetSteering = autoSteering * maxSteeringAngle;
}
    /// <summary>
    /// 应用加速度
    /// </summary>
  void ApplyAcceleration()
{
    float diff = targetSpeed - currentSpeed;
    float force = diff * acceleration;

    // 倒车静止起步额外推力
    if (targetSpeed < 0 && currentSpeed > -0.5f)
        force -= 8f;

    rb.AddForce(transform.forward * force, ForceMode.Acceleration);
}
    /// <summary>
    /// 应用转向
    /// </summary>
   void ApplySteering()
    {
        if (Mathf.Abs(currentSpeed) > 0.01f)
        {
            // ✅ 高速转向衰减
            float speedFactor = Mathf.Pow(1f - (Mathf.Abs(currentSpeed) / maxSpeed), 2);

            // 【核心修复】将 targetSteering (最大45) 还原成 -1 到 1 的系数
            float normalizedSteering = targetSteering / maxSteeringAngle; 
            
            float steering = normalizedSteering * speedFactor;

            // ✅ 使用 steeringSpeed (80度/秒) 乘以系数，得出每帧真实旋转角度
            float turnRate = steering * steeringSpeed * Time.fixedDeltaTime;
            transform.Rotate(0, turnRate, 0);
        }
    }
    /// <summary>
    /// 限制最大速度
    /// </summary>
    void LimitSpeed()
{
    float signedSpeed = Vector3.Dot(rb.velocity, transform.forward);
    if (Mathf.Abs(signedSpeed) > maxSpeed)
    {
        rb.velocity = transform.forward * Mathf.Sign(signedSpeed) * maxSpeed;
    }
}

    // ========== 公共接口（供其他脚本调用） ==========

    /// <summary>
    /// 设置自动驾驶控制指令
    /// </summary>
    /// <param name="throttle">油门 (0到1)</param>
    /// <param name="steering">转向 (-1到1，负数左转，正数右转)</param>
    public void SetAutoControl(float throttle, float steering)
    {
        autoThrottle = Mathf.Clamp(throttle, -1f, 1f);
        autoSteering = Mathf.Clamp(steering, -1f, 1f);
    }

    /// <summary>
    /// 获取当前速度
    /// </summary>
    public float GetSpeed()
    {
        return currentSpeed;
    }

    /// <summary>
    /// 获取车辆位置
    /// </summary>
    public Vector3 GetPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// 获取车辆朝向
    /// </summary>
    public Quaternion GetRotation()
    {
        return transform.rotation;
    }

    /// <summary>
    /// 切换控制模式
    /// </summary>
    public void ToggleMode()
    {
        autoMode = !autoMode;
        Debug.Log($"控制模式切换为: {(autoMode ? "自动驾驶" : "手动控制")}");
        
        // 【修复 2】强制重置状态机
        // 防止手动接管期间，AI 的状态机还卡在“避障”或“红绿灯”状态导致切回时暴走
        SimpleAutoDrive autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive != null && !autoMode)
        {
            autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
            SetAutoControl(0f, 0f); // 瞬间清除残留的油门转向指令
        }
    }
    // ========== 调试可视化 ==========
    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            // 绘制速度向量
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + rb.velocity);

            // 绘制前方方向
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 5f);
        }
    }
}
