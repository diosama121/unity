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
        // 切换模式
        if (Input.GetKeyDown(modeSwitchKey))
        {
            currentMode = currentMode == CameraMode.Follow ? CameraMode.FreeFly : CameraMode.Follow;
            Debug.Log($"📷 相机模式切换为: {currentMode}");
            
            // 切换到自由视角时，同步当前角度防止跳闪
            if (currentMode == CameraMode.FreeFly)
            {
                pitch = transform.eulerAngles.x;
                yaw = transform.eulerAngles.y;
            }
        }

        // 切换目标
        if (Input.GetKeyDown(targetSwitchKey))
        {
            RefreshVehicleList();
            if (allVehicles.Count > 0)
            {
                currentTargetIndex = (currentTargetIndex + 1) % allVehicles.Count;
                target = allVehicles[currentTargetIndex];
                currentMode = CameraMode.Follow; // 切换目标时强制转为跟随模式
                Debug.Log($"🎯 相机目标切换为: {target.name}");
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
        Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
        float terrainHeight = WorldModel.Instance.GetTerrainHeight(currentXZ);
        transform.position = new Vector3(transform.position.x, terrainHeight + 1f, transform.position.z);
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
        Vector2 targetXZ = new Vector2(targetPosition.x, targetPosition.z);
        float terrainHeight = WorldModel.Instance.GetTerrainHeight(targetXZ);
        targetPosition.y = terrainHeight + followOffset.y;

        // 3. 平滑移动
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, followSmoothTime);

        // 4. 始终看向车辆前方一点的位置
        Vector3 lookTarget = target.position + Vector3.up * 1.5f + target.forward * 3f;
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(lookTarget - transform.position), Time.deltaTime * 10f);
    }

    /// <summary>
    /// 刷新场景中的车辆列表
    /// </summary>
    void RefreshVehicleList()
    {
        allVehicles.Clear();
        SimpleCarController[] cars = FindObjectsOfType<SimpleCarController>();
        foreach (var car in cars)
        {
            allVehicles.Add(car.transform);
        }
    }
}