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
    [Header("🛡️ 主动安全系统 (AEB & 边界防护)")]
    public bool enableAEB = true;            // 是否开启紧急制动
    public float emergencyBrakeDistance = 2.5f; // 触发刹车的极限距离
    public float mapHeightLimit = -2.0f;     // 掉出地图的Y轴高度限制

    private RaycastSensor sensor;            // 雷达引用
    private Vector3 lastSafePosition;        // 记录最后一次的安全位置
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
        // 【核心修复】智能获取或生成刚体，绝不自杀！
        rb = GetComponentInParent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 1500f; // 赋予 1.5 吨的真实车重
            rb.drag = 0.1f;
            rb.angularDrag = 0.5f;
            Debug.Log("🔧 底盘控制器已动态补齐 Rigidbody 组件！");
        }

        // 确保自己的脚本是开启状态
        this.enabled = true;
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
        sensor = GetComponent<RaycastSensor>();
        lastSafePosition = transform.position;
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
        if (rb == null) return;

        // 1. 引擎动力：无论手动还是自动，把目标速度 (targetSpeed) 平滑转化为当前真实速度
        // 这里的 3f 是加速度系数，相当于踩下油门的响应速度
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.fixedDeltaTime * 3f);

        // 2. 核心物理移动：将速度赋予刚体！
        Vector3 newVelocity = transform.forward * currentSpeed;
        
        // 🌟 极其重要：必须保留 Y 轴的重力下落速度，否则车子会浮空或摩擦力错乱
        newVelocity.y = rb.velocity.y; 
        
        rb.velocity = newVelocity;

        // 3. 执行转向
        ApplySteering();

        // 4. 防侧滑物理 (保留我们之前加的天气打滑系统)
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        localVel.x *= slipFactor; // slipFactor 默认 0.5f，下雨天变大
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
        // 【核心优化 1】AEB 自动紧急制动拦截
        if (enableAEB && sensor != null)
        {
            float dist = sensor.GetFrontDistance();
            
            // 如果前方有障碍物，且距离小于极限距离，且当前指令是"往前开"(油门>0)
            if (dist > 0 && dist < emergencyBrakeDistance && throttle > 0)
            {
                // 强行切断动力，不允许往前！
                throttle = 0f; 
                
                // 如果车速还很快，直接物理强刹车
                if (rb != null && rb.velocity.magnitude > 0.5f)
                {
                    rb.velocity = Vector3.Lerp(rb.velocity, Vector3.zero, Time.deltaTime * 10f);
                }
                
                // 打印红字警告（可注释掉防刷屏）
                Debug.LogWarning($"🛑 AEB 触发！距离墙面 {dist:F1}m，已切断 ROS2/自动驾驶油门！");
            }
        }

        // --- 下面保留你原本的代码 ---
        targetSpeed = throttle * maxSpeed;
        targetSteering = steering * maxSteeringAngle;
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
