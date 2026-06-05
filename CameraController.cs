using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 影视级多视角相机控制器 (毕设 Demo 录制专用)
/// 功能：自由漫游、目标跟随、多车无缝切换
/// V2.0 升级：使用 WorldModel 地形高度，无物理射线/碰撞
/// </summary>
public class CameraController : MonoBehaviour
{
    public enum CameraMode { Follow, FreeFly }

    [Header("模式设置")]
    public CameraMode currentMode = CameraMode.Follow;
    [Tooltip("按此键在跟随和自由模式间切换")]
    public KeyCode modeSwitchKey = KeyCode.C;
    [Tooltip("按此键在不同车辆间切换视角")]
    public KeyCode targetSwitchKey = KeyCode.Tab;

    [Header("跟随模式参数")]
    public Transform target;
    public Vector3 followOffset = new Vector3(0, 4f, -8f);
    public float followSmoothTime = 0.15f;
    public float rotationSmoothTime = 0.1f;
    
    // 平滑阻尼变量
    private Vector3 velocity = Vector3.zero;
    private float currentYAngle;
    private float yAngleVelocity;

    [Header("自由漫游参数 (WASD + 鼠标右键)")]
    public float flySpeed = 20f;
    public float flyFastMultiplier = 3f; // 按住Shift加速
    public float mouseSensitivity = 2f;
    private float pitch = 0f;
    private float yaw = 0f;

    // 目标管理
    private List<Transform> allVehicles = new List<Transform>();
    private int currentTargetIndex = 0;

    void Start()
    {
        RefreshVehicleList();
        
        // 如果有车，默认跟随第一辆
        if (allVehicles.Count > 0 && target == null)
        {
            target = allVehicles[0];
        }

        // 初始化自由视角角度
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;
    }

    void Update()
    {
        HandleInput();

        if (currentMode == CameraMode.FreeFly)
        {
            HandleFreeFly();
        }
    }

    void LateUpdate()
    {
        if (currentMode == CameraMode.Follow)
        {
            HandleFollow();
        }
    }

    /// <summary>
/// 处理按键输入
/// </summary>
    void HandleInput()
    {
        // R键：重置所有Vehicle标签车辆到最近路口(排除主车)
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetAllNPCsToIntersections();
        }

        // 切换模式 (原有逻辑)
        if (Input.GetKeyDown(modeSwitchKey))
        {
            currentMode = currentMode == CameraMode.Follow ? CameraMode.FreeFly : CameraMode.Follow;
            if (currentMode == CameraMode.FreeFly)
            {
                pitch = transform.eulerAngles.x;
                yaw = transform.eulerAngles.y;
            }
        }

        // 切换目标并【真正移交控制权】
        if (Input.GetKeyDown(targetSwitchKey))
        {
            RefreshVehicleList();
            if (allVehicles.Count > 0)
            {
                // 0. 旧车离场 → 重置回车流
                ResetTargetToTraffic(target);

                // 1. 剥夺当前旧车的控制权
                if (target != null)
                {
                    SimpleAutoDrive oldDrive = target.GetComponent<SimpleAutoDrive>();
                    if (oldDrive != null) oldDrive.isPlayerControlled = false;
                }

                // 2. 寻找下一辆车
                currentTargetIndex = (currentTargetIndex + 1) % allVehicles.Count;
                target = allVehicles[currentTargetIndex];
                
                // 3. 赋予新车控制权
                if (target != null)
                {
                    SimpleAutoDrive newDrive = target.GetComponent<SimpleAutoDrive>();
                    if (newDrive != null) newDrive.isPlayerControlled = true;
                }

                currentMode = CameraMode.Follow; 
                Debug.Log($"相机目标切换并接管控制: {target.name}");
            }
        }

        // 鼠标左键点击接管车辆（Possess）
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                SimpleAutoDrive targetDrive = hit.collider.GetComponentInParent<SimpleAutoDrive>();
                if (targetDrive != null)
                {
                    // 旧车离场 → 重置回车流
                    Transform oldTarget = target;
                    ResetTargetToTraffic(oldTarget);

                    // 先把所有车的控制权交还给 AI
                    SimpleAutoDrive[] allCars = FindObjectsOfType<SimpleAutoDrive>();
                    foreach (var car in allCars)
                    {
                        car.isPlayerControlled = false;
                    }

                    // 接管被点击的车
                    targetDrive.isPlayerControlled = true;

                    // 将相机的 target 设为这辆车
                    this.target = targetDrive.transform;
                    currentMode = CameraMode.Follow;

                    Debug.Log("已接管车辆: " + targetDrive.gameObject.name);
                }
            }
        }
    }

    /// <summary>
    /// 自由漫游逻辑 (无人机视角)
    /// </summary>
    void HandleFreeFly()
    {
        // 鼠标右键旋转视角
        if (Input.GetMouseButton(1)) 
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        // WASD 移动
        float currentSpeed = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? flyFastMultiplier : 1f);
        float h = Input.GetAxis("Horizontal"); // A/D
        float v = Input.GetAxis("Vertical");   // W/S
        float u = 0f;
        if (Input.GetKey(KeyCode.E)) u = 1f;   // E 上升
        if (Input.GetKey(KeyCode.Q)) u = -1f;  // Q 下降

        Vector3 moveDir = (transform.forward * v) + (transform.right * h) + (Vector3.up * u);
        transform.position += moveDir * currentSpeed * Time.deltaTime;

        // V2.0 语义地形高度约束
        if (WorldModel.Instance != null)
        {
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            float terrainHeight = WorldModel.Instance.GetTerrainHeight(currentXZ);
            transform.position = new Vector3(transform.position.x, terrainHeight + 1f, transform.position.z);
        }
    }

    /// <summary>
    /// 平滑跟随逻辑 (车载稳定器视角)
    /// V2.0：使用 WorldModel 地形高度
    /// </summary>
    void HandleFollow()
    {
        if (target == null) return;

        // 1. 平滑旋转 (只跟随车辆的Y轴旋转)
        float targetYAngle = target.eulerAngles.y;
        currentYAngle = Mathf.SmoothDampAngle(currentYAngle, targetYAngle, ref yAngleVelocity, rotationSmoothTime);
        Quaternion currentRotation = Quaternion.Euler(0, currentYAngle, 0);

        // 2. 计算目标位置 (车辆位置 + 旋转后的偏移量)
        Vector3 targetPosition = target.position + currentRotation * followOffset;

        // V2.0 语义地形高度适配
        if (WorldModel.Instance != null)
        {
            Vector2 targetXZ = new Vector2(targetPosition.x, targetPosition.z);
            float terrainHeight = WorldModel.Instance.GetTerrainHeight(targetXZ);
            targetPosition.y = terrainHeight + followOffset.y;
        }

        // 3. 平滑移动
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, followSmoothTime);

        // 4. 始终看向车辆前方一点的位置
        Vector3 lookTarget = target.position + Vector3.up * 1.5f + target.forward * 3f;
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(lookTarget - transform.position), Time.deltaTime * 10f);
    }

    /// <summary>
    /// 刷新场景中的车辆列表（SimpleCarController + Vehicle标签）
    /// </summary>
    void RefreshVehicleList()
    {
        allVehicles.Clear();
        HashSet<Transform> seen = new HashSet<Transform>();

        // 所有带 SimpleCarController 的车(含NPC)
        SimpleCarController[] cars = FindObjectsOfType<SimpleCarController>();
        foreach (var car in cars)
        {
            if (car == null) continue;
            if (car.transform.position.y < -5f) continue;
            if (seen.Add(car.transform))
                allVehicles.Add(car.transform);
        }

        // 补充：所有标签为 "Vehicle" 的对象（后备兜底）
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Vehicle");
        foreach (var go in tagged)
        {
            if (go == null) continue;
            if (go.transform.position.y < -5f) continue;
            if (seen.Add(go.transform))
                allVehicles.Add(go.transform);
        }
    }

    /// <summary>
    /// R键：重置所有Vehicle标签车辆到最近路口（排除isPlayerControlled主车）
    /// </summary>
    void ResetAllNPCsToIntersections()
    {
        if (WorldModel.Instance == null) return;

        GameObject[] allTagged = GameObject.FindGameObjectsWithTag("Vehicle");
        int count = 0;
        foreach (var go in allTagged)
        {
            if (go == null) continue;
            SimpleAutoDrive drive = go.GetComponent<SimpleAutoDrive>();
            if (drive == null) continue;
            if (drive.isPlayerControlled) continue; // 跳过主车

            // 找最近的路口节点(邻居≥2)
            RoadNode bestNode = null;
            float bestDist = float.MaxValue;
            foreach (RoadNode node in WorldModel.Instance.Nodes)
            {
                if (node.NeighborIds == null || node.NeighborIds.Count < 2) continue;
                float d = Vector3.Distance(go.transform.position, node.WorldPos);
                if (d < bestDist) { bestDist = d; bestNode = node; }
            }

            if (bestNode != null)
            {
                // 重置到路口位置+找最近车道
                go.transform.position = bestNode.WorldPos + Vector3.up * 1f;

                int laneId = WorldModel.Instance.FindNearestLane(go.transform.position);
                if (laneId >= 0)
                {
                    drive.SetPath(new List<int> { laneId }, 0f);
                }
                else
                {
                    // 兜底：随机给一条车道
                    var lanes = WorldModel.Instance.GlobalLanes;
                    if (lanes != null && lanes.Count > 0)
                    {
                        var enumerator = lanes.Values.GetEnumerator();
                        enumerator.MoveNext();
                        drive.SetPath(new List<int> { enumerator.Current.LaneId }, 0f);
                    }
                }
                count++;
            }
        }
        Debug.Log($"[CameraCtrl] R键：{count}辆NPC已重置到最近路口");
    }

    /// <summary>
    /// 摄像头离开后 → 旧车重置回车流
    /// </summary>
    void ResetTargetToTraffic(Transform oldTarget)
    {
        if (oldTarget == null) return;
        if (WorldModel.Instance == null) return;

        SimpleAutoDrive drive = oldTarget.GetComponent<SimpleAutoDrive>();
        if (drive == null) return;

        // 曾是主车 → 释放控制权，由 SimpleAutoDrive.MergeBackToTraffic() 接管归位
        drive.isPlayerControlled = false;

        // ★ 修复：不再在此调用 SetPath，避免与 MergeBackToTraffic 的路径规划冲突
        // MergeBackToTraffic 会在下一帧自动处理：吸附车道 → 锁定轨迹 → 重新寻路
        Debug.Log($"[CameraCtrl] {oldTarget.name} 离场,移交自动驾驶");
    }
}