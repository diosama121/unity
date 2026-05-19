using UnityEngine;

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
        if (isNPC || rb == null) return;

        RaycastHit hit;
        bool isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 2.0f);

        Vector3 projForward = transform.forward;

        if (isGrounded)
        {
            projForward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            // 幻觉清除：直接用原生的 normalized，如果是 zero 向量则退化为默认前向
            Vector3 safeForward = projForward.sqrMagnitude > 0.0001f ? projForward.normalized : transform.forward;
            Quaternion slopeRot = Quaternion.LookRotation(safeForward, hit.normal);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, slopeRot, Time.fixedDeltaTime * 8f));
        }

        ApplySteering();
        
        // 幻觉清除：原生的安全归一化
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
            // 幻觉清除：用原生数学逻辑替换所谓的 CarControlUtility
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
            // 防止 zero 向量报错
            if (slopeForward.sqrMagnitude > 0.001f && trueUp.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(slopeForward, trueUp);
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
            // 幻觉清除：用原生数学逻辑替换所谓的 CarControlUtility
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
        transform.position = originalPosition;
        targetSpeed = 0f;
        currentSpeed = 0f;
    }
}