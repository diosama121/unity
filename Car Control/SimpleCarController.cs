using UnityEngine;

/// <summary>
/// V4.1 纯净版车辆控制器
/// 玩家模式：保留物理射线与 Rigidbody 驱动。
/// NPC模式：彻底切断物理引擎与旧版接口，通过 V4.1 统一高程真理层进行纯数学轨道飞行。
/// </summary>
public partial class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    public float maxSpeed = 30f;
    public float acceleration = 4f; 
    public float brakeDeceleration = 10f;
    public float steeringSpeed = 80f;
    public float maxSteeringAngle = 45f;

    [Header("控制模式")]
    public bool autoMode = false;

    [Header("性能优化 (NPC专用)")]
    public bool isNPC = false;
    [Tooltip("NPC车辆距离地面的悬挂高度")]
    public float npcSuspensionHeight = 0.5f;

    [Header("调试信息")]
    public float currentSpeed = 0f;
    public float currentSteeringAngle = 0f;

    [Header("物理环境")]
    public float slipFactor = 0.5f;
   
    // private RaycastSensor sensor;
    private Rigidbody rb;
    private float targetSpeed = 0f;
    private float targetSteering = 0f;
    private Vector3 originalPosition;
    
    private Collider[] allColliders;
    private float autoThrottle = 0f;
    private float autoSteering = 0f;
    private float autoBrakingDecel = 0f;

    // Bug2修复: WASD临时接管，不永久修改autoMode
    private bool wasdOverride = false;
    private bool autoModeBeforeOverride = false;

    void Awake()
    {
        allColliders = GetComponentsInChildren<Collider>();
        if (isNPC)
        {
            foreach (var col in allColliders) col.enabled = false;
        }
        else
        {
            foreach (var col in allColliders) col.enabled = true;
        }
    }

    void Start()
    {
        rb = GetComponentInParent<Rigidbody>();
        if (rb == null && !isNPC) rb = gameObject.AddComponent<Rigidbody>();

        if (!isNPC && rb != null)
        {
            rb.mass = 1500f; rb.drag = 0.5f; rb.angularDrag = 8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.None;
        }
        else if (isNPC && rb != null)
        {
            rb.isKinematic = true;
        }

      //  sensor = GetComponent<RaycastSensor>();
        this.enabled = true;
        originalPosition = transform.position;
    }

    void Update()
    {
        HandlePlayerInput();

        if (!isNPC)
        {
            currentSpeed = Vector3.Dot(rb.velocity, transform.forward);
        }
        else
        {
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 2f);
        }
    
        if (autoMode) HandleAutoDrive();
        else HandleManualControl();

        currentSteeringAngle = targetSteering;
    }

    // 物理更新 (仅玩家模式使用，保留原有射线不变)
    void FixedUpdate()
    {
        if (isNPC || rb == null) return;

        RaycastHit hit;
        bool isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 2.0f);

        if (isGrounded)
        {
            Quaternion slopeRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized, hit.normal);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, slopeRot, Time.fixedDeltaTime * 8f));
        }

        ApplySteering();
        Vector3 moveDir = isGrounded ? Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized : transform.forward;
        Vector3 newVelocity = moveDir * targetSpeed;
        if (!isGrounded) newVelocity.y = rb.velocity.y;
        Vector3 localVel = transform.InverseTransformDirection(newVelocity);
        localVel.x *= slipFactor;
        rb.velocity = transform.TransformDirection(localVel);
    }

    // 纯数学更新 (仅 NPC 模式使用)
    void LateUpdate()
    {
        if (!isNPC) return;

        // 1. 纯数学位移计算
        float moveStep = targetSpeed * Time.deltaTime;
        transform.position += transform.forward * moveStep;

        // 2. 纯数学转向计算
        if (Mathf.Abs(targetSpeed) > 0.01f)
        {
            float speedFactor = Mathf.Pow(1f - (Mathf.Abs(targetSpeed) / maxSpeed), 2);
            float normalizedSteering = targetSteering / maxSteeringAngle;
            float turnAmount = normalizedSteering * speedFactor * steeringSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        // ====================================================================
        // 【V4.1 并发突击】彻底切断旧版接口，严格对齐全局统一高程真理层
        // ====================================================================
        if (WorldModel.Instance != null)
        {
            // [核心修改] 废弃 GetTerrainHeight，调用 V4.1 统一高程公共契约
            float trueGroundY = WorldModel.Instance.GetUnifiedHeight(transform.position.x, transform.position.z)+0.2f;
            
            Vector3 pos = transform.position;
            // 叠加悬挂高度偏移
            pos.y = trueGroundY + npcSuspensionHeight;
            transform.position = pos;

            // 脱离物理法线后，强制锁定 Pitch 与 Roll 轴，实现绝对的“轨道车”匀速贴地飞行
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
    }

    void HandleManualControl()
    {
        float throttle = Input.GetAxis("Vertical");
        float steering = Input.GetAxis("Horizontal");
        if (throttle > 0) targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f);
        else if (throttle < 0)
        {
            if (currentSpeed > 0.5f) targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 5f); 
            else targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * throttle, Time.deltaTime * 2f); 
        }
        else targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f); 

        targetSteering = steering * maxSteeringAngle;
        if (Input.GetKey(KeyCode.Space)) targetSpeed = 0f;
    }

    void HandleAutoDrive()
    {
        if (autoBrakingDecel > 0.01f && currentSpeed > 0.1f)
        {
            float effectiveDecel = Mathf.Max(autoBrakingDecel, brakeDeceleration * 0.3f);
            targetSpeed = Mathf.Max(0f, currentSpeed - effectiveDecel * Time.deltaTime);
        }
        else
        {
            if (autoThrottle >= 0) targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 2f);
            else targetSpeed = maxSpeed * autoThrottle;
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

    public void SetAutoControl(float throttle, float steering)
    {
        this.autoThrottle = throttle;
        this.autoSteering = steering;
    }

    public void SetAutoBrake(float deceleration)
    {
        autoBrakingDecel = Mathf.Max(0f, deceleration);
    }

    public float GetSpeed() => currentSpeed;
    public Vector3 GetPosition() => transform.position;
    public Quaternion GetRotation() => transform.rotation;

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

    public void ResetPosition()
    {
        transform.position = originalPosition;
        targetSpeed = 0f;
        currentSpeed = 0f;
    }
}
