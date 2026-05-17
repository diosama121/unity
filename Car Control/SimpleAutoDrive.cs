using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
public partial class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 15f;
    public float safeDistance = 8f;
    public float lookAheadT = 0.02f;
    
    [Header("=== 交通规则注入 ===")]
    public float rightLaneOffset = 3.5f;

    public enum DriveState { Idle, Following, Avoiding, Stopping, Waiting, RemoteControlled }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public float currentT = 0f;
    public bool obstacleDetected = false;
    public int currentLaneId = -1;
    
    public IntersectionState currentIntersectionState = IntersectionState.Uncontrolled; 
    public int currentDestinationNodeId = -1;
    private Vector3 stopTargetPosition = Vector3.zero;
    private bool hasStopTarget = false;

    private int reverseCount = 0;
    private SimpleCarController carController;
    private TrafficManager trafficManager;
    
    private CatmullRomSpline currentSpline;
    private Vector3 finalDestination = Vector3.zero;

    private float avoidCooldown = 0f;
    private float startupDelay = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;

    private float stuckTimer = 0.3f;
    private Vector3 lastPosition = Vector3.zero;
    private float stuckCheckInterval = 0.5f;
    private float stuckCheckTimer = 0f;
    private float escapeSteering = 0f;

    private float brakeMaxDecel = 8f;
    private float laneSearchTimer = 0f;

    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();
        trafficManager = FindObjectOfType<TrafficManager>();
        carController.autoMode = true;
        lastPosition = transform.position;
        laneSearchTimer = Random.Range(0f, 0.2f);
    }

    void Update()
    {
        if (!carController.autoMode && currentState != DriveState.RemoteControlled) return;
        if (avoidCooldown > 0f) avoidCooldown -= Time.deltaTime;

        UpdateSensorData();
        UpdateStuckDetection();

        switch (currentState)
        {
            case DriveState.Idle: HandleIdleState(); break;
            case DriveState.Following: HandleFollowingState(); break;
            case DriveState.Avoiding: HandleAvoidingState(); break;
            case DriveState.Stopping: HandleStoppingState(); break;
            case DriveState.Waiting: HandleWaitingState(); break;
            case DriveState.RemoteControlled: HandleRemoteControlledState(); break;
        }
    }

    void UpdateSensorData()
    {
        obstacleDetected = false;

        if (trafficManager != null)
        {
            var npcs = trafficManager.ActiveNPCs;
            if (npcs != null)
            {
                Vector3 myPos = transform.position;
                Vector3 myForward = transform.forward;

                for (int i = 0; i < npcs.Count; i++)
                {
                    SimpleAutoDrive other = npcs[i];
                    if (other == this || other == null) continue;

                    Vector3 otherPos = other.transform.position;
                    Vector3 dirToOther = otherPos - myPos;
                    float dist = dirToOther.magnitude;

                    if (dist < safeDistance)
                    {
                        float faceDot = Vector3.Dot(myForward, other.transform.forward);
                        if (faceDot > 0f)
                        {
                            Vector3 dirNorm = dirToOther / dist;
                            float dot = Vector3.Dot(myForward, dirNorm);
                            if (dot > 0.8f)
                            {
                                obstacleDetected = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (WorldModel.Instance != null)
        {
            RoadNode nearestNode = WorldModel.Instance.GetNearestNode(transform.position);
            if (nearestNode != null && (nearestNode.Type == NodeType.Intersection || nearestNode.Type == NodeType.Merge))
            {
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(nearestNode.Id, transform.position);
                if (relevantStopLine != null && Vector3.Distance(transform.position, relevantStopLine.Position) < 20f)
                    currentIntersectionState = WorldModel.Instance.GetPhaseState(relevantStopLine.AssociatedPhaseId);
                else
                    currentIntersectionState = IntersectionState.Uncontrolled;
            }
            else currentIntersectionState = IntersectionState.Uncontrolled;
        }
        else
        {
            currentIntersectionState = IntersectionState.Uncontrolled;
        }
    }

    void UpdateStuckDetection()
    {
        if (currentState != DriveState.Following)
        {
            stuckTimer = 0f; stuckCheckTimer = 0f; startupDelay = 0f; lastPosition = transform.position; return;
        }
        if (startupDelay > 0f)
        {
            startupDelay -= Time.deltaTime;
            stuckTimer = 0f; stuckCheckTimer = 0f; lastPosition = transform.position; return;
        }

        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer < stuckCheckInterval) return;
        stuckCheckTimer = 0f;

        float moved = Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        if (moved < 0.3f)
        {
            stuckTimer += stuckCheckInterval;
            if (stuckTimer > 4f)
            {
                stuckTimer = 0f; isReversing = true; reverseTimer = 0f;
                
                if (currentSpline != null)
                {
                    Vector3 tangent = currentSpline.GetPoint(Mathf.Min(currentT + 0.01f, 1f)) - currentSpline.GetPoint(currentT);
                    Vector3 localTangent = transform.InverseTransformDirection(tangent);
                    escapeSteering = localTangent.x > 0 ? 1f : -1f;
                }
                else escapeSteering = 0f;

                currentState = DriveState.Avoiding;
            }
        }
        else stuckTimer = 0f;
    }

    void HandleIdleState()
    {
        carController.SetAutoControl(0f, 0f);
        if (currentSpline != null)
        {
            currentT = 0f;
            currentState = DriveState.Following;
        }
    }

    void RequestNewRandomPath()
    {
        if (WorldModel.Instance != null && pathPlanner != null)
        {
            for (int i = 0; i < 10; i++)
            {
                int randTargetId = Random.Range(0, WorldModel.Instance.NodeCount);
                RoadNode targetNode = WorldModel.Instance.GetNode(randTargetId);
                if (targetNode != null)
                {
                    CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, targetNode.WorldPos);
                    if (newSpline != null && newSpline.TotalLength > 0)
                    {
                        SetSplinePath(newSpline, targetNode.Id);
                        return;
                    }
                }
            }
        }
        currentState = DriveState.Idle;
        currentSpline = null;
        carController.SetAutoControl(0f, 0f);
    }

    public void ResetNavigation()
    {
        RequestNewRandomPath();
    }

    public void SetSplinePath(CatmullRomSpline spline, int destinationNodeId)
    {
        this.currentSpline = spline;
        this.currentDestinationNodeId = destinationNodeId; 
        this.currentT = 0f;
        this.currentState = DriveState.Following;
    }

    void RerouteToDestination()
    {
        if (pathPlanner == null) return;
        Vector3 target = finalDestination != Vector3.zero ? finalDestination : transform.position + transform.forward * 20f;
        
        CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, target);
        if (newSpline != null && newSpline.TotalLength > 0)
        {
            currentSpline = newSpline;
            currentT = 0f;
        }
    }

    public void SetDestination(Vector3 destination)
    {
        if (pathPlanner == null) { Debug.LogError("未找到PathPlanner！"); return; }
        finalDestination = destination;
        RerouteToDestination();
    }

    public void ToggleAutoDrive()
    {
        carController.autoMode = !carController.autoMode;
        if (!carController.autoMode) currentState = DriveState.Idle;
    }

    public DriveState GetCurrentState() => currentState;

    void OnDrawGizmos()
    {
        if (currentSpline != null && currentT < 1f)
        {
            Gizmos.color = Color.yellow;
            Vector3 drawPoint = currentSpline.GetPoint(currentT);
            Gizmos.DrawWireSphere(drawPoint, 2f);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);
    }
}