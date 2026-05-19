using UnityEngine;

public enum VehiclePriority { Normal, Emergency }

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

    [Header("优先级")]
    public VehiclePriority vehiclePriority = VehiclePriority.Normal;

    private Rigidbody rb;
    private float targetSpeed = 0f;
    private float targetSteering = 0f;
    private Vector3 originalPosition;
    
    private Collider[] allColliders;
    private float autoThrottle = 0f;
    private float autoSteering = 0f;
    private float autoBrakingDecel = 0f;

    public bool wasdOverride = false;
    private bool autoModeBeforeOverride = false;
    public bool ros2Controlled = false;

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

    void FixedUpdate()
    {
        if (isNPC || rb == null) return;

        RaycastHit hit;
        bool isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 2.0f);

        Vector3 projForward = transform.forward;

        if (isGrounded)
        {
            projForward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            Vector3 safeForward = CarControlUtility.SafeNormalize(projForward, Vector3.zero);
            Quaternion slopeRot = Quaternion.LookRotation(safeForward, hit.normal);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, slopeRot, Time.fixedDeltaTime * 8f));
        }

        ApplySteering();
        Vector3 moveDir = isGrounded ? CarControlUtility.SafeNormalize(projForward, transform.forward) : transform.forward;
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
            float speedFactor = CarControlUtility.ComputeSpeedFactor(targetSpeed, maxSpeed);
            float normalizedSteering = CarControlUtility.ComputeNormalizedSteering(targetSteering, maxSteeringAngle);
            float turnAmount = normalizedSteering * speedFactor * steeringSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        if (WorldModel.Instance != null)
        {
            Vector3 pos = transform.position;
            float baseGroundY;
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f))
            {
                baseGroundY = hit.point.y;
            }
            else
            {
                baseGroundY = WorldModel.Instance.GetUnifiedHeight(pos.x, pos.z);
            }

            Vector3 frontCheck = pos + transform.forward * 2f;
            float frontY;
            if (Physics.Raycast(frontCheck + Vector3.up * 5f, Vector3.down, out RaycastHit frontHit, 20f))
            {
                frontY = frontHit.point.y;
            }
            else
            {
                frontY = WorldModel.Instance.GetUnifiedHeight(frontCheck.x, frontCheck.z);
            }

            pos.y = baseGroundY + npcSuspensionHeight;
            transform.position = pos;

            Vector3 slopeForward = new Vector3(transform.forward.x, frontY - baseGroundY, transform.forward.z).normalized;

            Vector3 leftCheck = pos - transform.right * 1.5f;
            float leftY;
            if (Physics.Raycast(leftCheck + Vector3.up * 5f, Vector3.down, out RaycastHit leftHit, 20f))
                leftY = leftHit.point.y;
            else
                leftY = WorldModel.Instance.GetUnifiedHeight(leftCheck.x, leftCheck.z);

            Vector3 rightCheck = pos + transform.right * 1.5f;
            float rightY;
            if (Physics.Raycast(rightCheck + Vector3.up * 5f, Vector3.down, out RaycastHit rightHit, 20f))
                rightY = rightHit.point.y;
            else
                rightY = WorldModel.Instance.GetUnifiedHeight(rightCheck.x, rightCheck.z);

            Vector3 slopeRight = new Vector3(transform.right.x, rightY - leftY, transform.right.z).normalized;

            Vector3 trueUp = Vector3.Cross(slopeForward, slopeRight).normalized;

            transform.rotation = Quaternion.LookRotation(slopeForward, trueUp);
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
            float speedFactor = CarControlUtility.ComputeSpeedFactor(currentSpeed, maxSpeed);
            float normalizedSteering = CarControlUtility.ComputeNormalizedSteering(targetSteering, maxSteeringAngle);
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
