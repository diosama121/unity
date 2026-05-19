using UnityEngine;

// 添加缺失的枚举定义（放在类外，方便全局访问）
public enum VehiclePriority
{
    Normal,
    Emergency
}

public partial class SimpleCarController : MonoBehaviour
{
    [Header("车辆参数")]
    public float maxSpeed = 30f;
    public float acceleration = 4f; 
    public float brakeDeceleration = 10f;
    public float steeringSpeed = 80f;
    public float maxSteeringAngle = 45f;

    // 新增：车辆优先级，由控制器统一管理
    [Header("车辆优先级")]
    public VehiclePriority vehiclePriority = VehiclePriority.Normal;

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

    private Rigidbody rb;
    private float targetSpeed = 0f;
    private float targetSteering = 0f;
    private Vector3 originalPosition;
    
    private Collider[] allColliders;
    private float autoThrottle = 0f;
    private float autoSteering = 0f;
    private float autoBrakingDecel = 0f;

    public bool wasdOverride = false;

  void Awake()
{
    allColliders = GetComponentsInChildren<Collider>();
    // ✅ 修复：初始化时，调用一次 ChangeRole 确保状态绝对同步
    ChangeRole(isNPC);
}

    void Start()
    {
        rb = GetComponent<Rigidbody>();
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

        this.enabled = true;
        originalPosition = transform.position;
    }

    void Update()
    {
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

    void FixedUpdate()
    {
        if (isNPC || rb == null || rb.isKinematic) return;

        RaycastHit hit;
        bool isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 2.0f);

        Vector3 projForward = transform.forward;

        if (isGrounded)
        {
            projForward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            Vector3 safeForward = projForward.sqrMagnitude > 0.0001f ? projForward.normalized : transform.forward;
            Quaternion slopeRot = Quaternion.LookRotation(safeForward, hit.normal);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, slopeRot, Time.fixedDeltaTime * 8f));
        }

        ApplySteering();
        
        Vector3 moveDir = isGrounded ? (projForward.sqrMagnitude > 0.0001f ? projForward.normalized : transform.forward) : transform.forward;
        Vector3 newVelocity = moveDir * targetSpeed;
        if (!isGrounded) newVelocity.y = rb.velocity.y;
        
        Vector3 localVel = transform.InverseTransformDirection(newVelocity);
        localVel.x *= slipFactor;
        Vector3 targetVel = transform.TransformDirection(localVel);
        rb.velocity = Vector3.Lerp(rb.velocity, targetVel, Time.fixedDeltaTime * 10f);
    }

    void LateUpdate()
    {
        if (!isNPC) return;

        float moveStep = targetSpeed * Time.deltaTime;
        transform.position += transform.forward * moveStep;

        if (Mathf.Abs(targetSpeed) > 0.01f)
        {
            float speedFactor = maxSpeed > 0.001f ? (Mathf.Abs(targetSpeed) / maxSpeed) : 0f;
            float normalizedSteering = maxSteeringAngle > 0.001f ? (targetSteering / maxSteeringAngle) : 0f;
            
            float turnAmount = normalizedSteering * speedFactor * steeringSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        if (WorldModel.Instance != null)
        {
            float baseGroundY = WorldModel.Instance.GetUnifiedHeight(transform.position.x, transform.position.z);
            float frontY = WorldModel.Instance.GetUnifiedHeight(transform.position.x + transform.forward.x, transform.position.z + transform.forward.z);

            Vector3 pos = transform.position;
            pos.y = baseGroundY + npcSuspensionHeight;
            transform.position = pos;

            Vector3 slopeForward = new Vector3(transform.forward.x, frontY - baseGroundY, transform.forward.z).normalized;

            float leftY = WorldModel.Instance.GetUnifiedHeight(transform.position.x - transform.right.x, transform.position.z - transform.right.z);
            float rightY = WorldModel.Instance.GetUnifiedHeight(transform.position.x + transform.right.x, transform.position.z + transform.right.z);
            Vector3 slopeRight = new Vector3(transform.right.x, rightY - leftY, transform.right.z).normalized;

            Vector3 trueUp = Vector3.Cross(slopeForward, slopeRight).normalized;
            if (slopeForward.sqrMagnitude > 0.001f && trueUp.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(slopeForward, trueUp);
            }
        }
    }
// 动态切换车辆身份（玩家/NPC）
    public void ChangeRole(bool toNPC)
    {
        isNPC = toNPC;
        if (allColliders == null) allColliders = GetComponentsInChildren<Collider>();
        if (rb == null) rb = GetComponent<Rigidbody>();

        foreach (var col in allColliders) { if (col != null) col.enabled = !isNPC; }

        if (rb != null)
        {
            if (isNPC)
            {
                rb.isKinematic = true;
                wasdOverride = false;
                autoMode = true; // NPC 必须是自动驾驶
            }
            else
            {
                rb.isKinematic = false;
                rb.mass = 1500f; rb.drag = 0.5f; rb.angularDrag = 8f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.constraints = RigidbodyConstraints.None;
                
                autoMode = false; // 【关键修复】变成玩家时，强制关闭自动驾驶，交还 WASD 控制权
                targetSpeed = currentSpeed;
            }
        }
    }
    void HandleManualControl()
    {
        if (wasdOverride)
        {
            if (autoThrottle > 0) targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 2f);
            else if (autoThrottle < 0)
            {
                if (currentSpeed > 0.5f) targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 8f);
                else targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 4f);
            }
            else targetSpeed = Mathf.Lerp(targetSpeed, 0, Time.deltaTime * 1f);
            targetSteering = autoSteering * maxSteeringAngle;
            if (Input.GetKey(KeyCode.Space)) targetSpeed = 0f;
            return;
        }

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
            else targetSpeed = Mathf.Lerp(targetSpeed, maxSpeed * autoThrottle, Time.deltaTime * 4f);
        }
        targetSteering = autoSteering * maxSteeringAngle;
    }

    void ApplySteering()
    {
        if (Mathf.Abs(currentSpeed) > 0.01f)
        {
            float speedFactor = maxSpeed > 0.001f ? (Mathf.Abs(currentSpeed) / maxSpeed) : 0f;
            float normalizedSteering = maxSteeringAngle > 0.001f ? (targetSteering / maxSteeringAngle) : 0f;
            float turnRate = normalizedSteering * speedFactor * steeringSpeed * Time.fixedDeltaTime;

            if (!isNPC && rb != null)
            {
                Quaternion turnRotation = Quaternion.Euler(0f, turnRate, 0f);
                rb.MoveRotation(rb.rotation * turnRotation);
            }
            else
            {
                transform.Rotate(0, turnRate, 0);
            }
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
        // 智能复位：找最近的路网节点，并抬高 1 米防止卡在地里
        if (WorldModel.Instance != null)
        {
            var nearest = WorldModel.Instance.GetNearestNode(transform.position);
            if (nearest != null)
            {
                Vector3 safePos = nearest.WorldPos;
                safePos.y = WorldModel.Instance.GetUnifiedHeight(safePos.x, safePos.z) + 1.0f;
                transform.position = safePos;
                transform.rotation = Quaternion.identity; // 摆正车身
            }
        }
        else transform.position = originalPosition + Vector3.up * 1f;

        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        targetSpeed = 0f; currentSpeed = 0f;
    }
}