using UnityEngine;

/// <summary>
/// 简单车辆控制器
/// 功能：手动控制（WASD）+ 自动驾驶接口
/// 作者：[你的名字]
/// 日期：2025
/// </summary>
public class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    [Tooltip("最大速度 (m/s)")]
    public float maxSpeed = 20f;
    
    [Tooltip("加速度 (m/s²)")]
    public float acceleration = 5f;
    
    [Tooltip("制动减速度 (m/s²)")]
    public float brakeDeceleration = 10f;
    
    [Tooltip("转向速度 (度/秒)")]
    public float steeringSpeed = 100f;
    
    [Tooltip("最大转向角度")]
    public float maxSteeringAngle = 30f;

    [Header("控制模式")]
    [Tooltip("是否启用自动驾驶")]
    public bool autoMode = false;

    [Header("调试信息")]
    public float currentSpeed = 0f;
    public float currentSteeringAngle = 0f;

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
    }

    void Update()
    {
        if (autoMode)
        {
            // 自动驾驶模式
            HandleAutoDrive();
        }
        else
        {
            // 手动控制模式
            HandleManualControl();
        }

        // 更新显示信息
        currentSpeed = rb.velocity.magnitude;
        currentSteeringAngle = targetSteering;
    }

    void FixedUpdate()
    {
        // 应用加速度
        ApplyAcceleration();
        
        // 应用转向
        ApplySteering();
        
        // 速度限制
        LimitSpeed();
    }

    /// <summary>
    /// 手动控制处理
    /// </summary>
    void HandleManualControl()
    {
        // WASD 控制
        float throttle = Input.GetAxis("Vertical");    // W/S
        float steering = Input.GetAxis("Horizontal");  // A/D

        // 油门/刹车
        if (throttle > 0)
        {
            targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f);
        }
        else if (throttle < 0)
        {
            targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 5f);
        }
        else
        {
            // 无输入时缓慢减速
            targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f);
        }

        // 转向
        targetSteering = steering * maxSteeringAngle;

        // 空格刹车
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
        // 使用外部设置的控制值
        targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * Mathf.Clamp(autoThrottle, 0f, 1f), Time.deltaTime * 2f);
        targetSteering = autoSteering * maxSteeringAngle;
    }

    /// <summary>
    /// 应用加速度
    /// </summary>
    void ApplyAcceleration()
    {
        float speedDiff = targetSpeed - currentSpeed;
        
        Vector3 force = transform.forward * speedDiff * acceleration;
        rb.AddForce(force, ForceMode.Acceleration);
    }

    /// <summary>
    /// 应用转向
    /// </summary>
    void ApplySteering()
    {
        if (currentSpeed > 0.5f)  // 只在运动时转向
        {
            float steering = targetSteering * (currentSpeed / maxSpeed);  // 速度越快转向越敏感
            transform.Rotate(0, steering * Time.fixedDeltaTime, 0);
        }
    }

    /// <summary>
    /// 限制最大速度
    /// </summary>
    void LimitSpeed()
    {
        if (rb.velocity.magnitude > maxSpeed)
        {
            rb.velocity = rb.velocity.normalized * maxSpeed;
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
