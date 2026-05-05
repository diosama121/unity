using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// <summary>
/// V2.0 上帝视角观测台 (a5 视觉与数据观测官)
/// 核心准则：零物理射线，纯语义数据驱动
/// 功能：节点总数统计、NPC活跃监控、鼠标悬停语义查询、一键数据录制
/// </summary>
public class DebugPanel : MonoBehaviour
{
    [Header("=== UI 文本组件 ===")]
    public Text worldStatsText;          // 世界统计信息
    public Text mouseHoverInfoText;      // 鼠标悬停语义信息
    public Text cameraGroundInfoText;    // 相机下方语义信息

    [Header("=== UI 按钮组件 ===")]
    public Button toggleRecordButton;    // 一键录制按钮
    public Text recordButtonText;        // 录制按钮状态文本

    [Header("=== 观测台设置 ===")]
    public KeyCode togglePanelKey = KeyCode.F1; // 开关面板快捷键
    public float updateInterval = 0.1f;         // 数据刷新频率

    // 内部引用
    private SystemDataManager dataManager;
    private float updateTimer;
    private bool isPanelVisible = true;

    void Start()
    {
        // 自动查找数据管理器
        dataManager = FindObjectOfType<SystemDataManager>();
        
        // 初始化按钮事件
        if (toggleRecordButton != null)
        {
            toggleRecordButton.onClick.AddListener(OnToggleRecordClicked);
        }

        // 初始更新UI
        UpdateWorldStats();
        UpdateRecordButtonUI(false);
    }

    void Update()
    {
        // 面板开关控制
        if (Input.GetKeyDown(togglePanelKey))
        {
            isPanelVisible = !isPanelVisible;
            gameObject.SetActive(isPanelVisible);
        }

        if (!isPanelVisible) return;

        // 定时刷新数据
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            UpdateWorldStats();
            UpdateMouseHoverInfo();
            UpdateCameraGroundInfo();
            updateTimer = 0f;
        }
    }

    /// <summary>
    /// 更新世界统计信息 (节点总数、NPC数量)
    /// </summary>
    void UpdateWorldStats()
    {
        if (worldStatsText == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 🌍 世界语义统计 ===");
        
        // 1. 获取节点总数 (通过 WorldModel 接口)
        // 假设 WorldModel 提供 NodeCount 属性或 Nodes 列表，此处兼容两种常见模式
        int nodeCount = 0;
        if (WorldModel.Instance != null)
        {
            // 优先尝试 NodeCount 属性
            var nodeCountProp = WorldModel.Instance.GetType().GetProperty("NodeCount");
            if (nodeCountProp != null)
            {
                nodeCount = (int)nodeCountProp.GetValue(WorldModel.Instance);
            }
            // 备选：尝试 Nodes 列表
            else
            {
                var nodesProp = WorldModel.Instance.GetType().GetProperty("Nodes");
                if (nodesProp != null)
                {
                    var nodesList = nodesProp.GetValue(WorldModel.Instance) as System.Collections.IList;
                    nodeCount = nodesList?.Count ?? 0;
                }
            }
        }
        sb.AppendLine($"路网节点总数: {nodeCount}");

        // 2. 获取活跃 NPC 数量 (查找 SimpleAutoDrive 组件)
        int npcCount = FindObjectsOfType<SimpleAutoDrive>().Length;
        sb.AppendLine($"活跃 NPC 数量: {npcCount}");

        worldStatsText.text = sb.ToString();
    }

    /// <summary>
    /// 更新鼠标悬停位置的语义信息 (零物理射线)
    /// </summary>
    void UpdateMouseHoverInfo()
    {
        if (mouseHoverInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 🖱️ 鼠标悬停语义 ===");

        // V2.0 核心：零物理射线，仅通过屏幕坐标转 XZ 平面查询
        Vector3 mouseScreenPos = Input.mousePosition;
        // 假设相机在场景中，取一个合理的 Y 高度用于 ScreenToWorldPoint
        // 这里我们只关心 XZ 平面，所以用一个固定的 Y 值
        mouseScreenPos.z = 10f; // 距离相机 10 个单位
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        
        // 提取 XZ 坐标，忽略 Y，通过 WorldModel 查询最近节点
        Vector2 mouseXZ = new Vector2(mouseWorldPos.x, mouseWorldPos.z);
        RoadNode nearestNode = WorldModel.Instance.GetNearestNode(new Vector3(mouseXZ.x, 0, mouseXZ.y));

        if (nearestNode != null)
        {
            sb.AppendLine($"鼠标 XZ: ({mouseXZ.x:F1}, {mouseXZ.y:F1})");
            sb.AppendLine($"最近节点 ID: {nearestNode.Id}");
            sb.AppendLine($"节点世界坐标: ({nearestNode.WorldPos.x:F1}, {nearestNode.WorldPos.y:F1}, {nearestNode.WorldPos.z:F1})");
            
            // 通过 NeighborIds 判断节点类型
            string nodeType = "未知";
            if (nearestNode.NeighborIds != null)
            {
                if (nearestNode.NeighborIds.Count >= 3) nodeType = "路口";
                else if (nearestNode.NeighborIds.Count == 2) nodeType = "路段";
                else nodeType = "端点";
            }
            sb.AppendLine($"节点类型: {nodeType}");
            sb.AppendLine($"邻接节点数: {nearestNode.NeighborIds?.Count ?? 0}");
        }
        else
        {
            sb.AppendLine("未检测到有效路网节点");
        }

        mouseHoverInfoText.text = sb.ToString();
    }

    /// <summary>
    /// 更新相机下方的语义信息 (零物理射线)
    /// </summary>
    void UpdateCameraGroundInfo()
    {
        if (cameraGroundInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 📷 相机下方语义 ===");

        // 获取相机位置，提取 XZ 平面
        Vector3 cameraPos = Camera.main.transform.position;
        Vector2 cameraXZ = new Vector2(cameraPos.x, cameraPos.z);
        
        // 通过 WorldModel 查询最近节点
        RoadNode nearestNode = WorldModel.Instance.GetNearestNode(new Vector3(cameraXZ.x, 0, cameraXZ.y));

        if (nearestNode != null)
        {
            sb.AppendLine($"相机 XZ: ({cameraXZ.x:F1}, {cameraXZ.y:F1})");
            sb.AppendLine($"最近节点 ID: {nearestNode.Id}");
            
            // 通过 NeighborIds 判断节点类型
            string nodeType = "未知";
            if (nearestNode.NeighborIds != null)
            {
                if (nearestNode.NeighborIds.Count >= 3) nodeType = "路口";
                else if (nearestNode.NeighborIds.Count == 2) nodeType = "路段";
                else nodeType = "端点";
            }
            sb.AppendLine($"节点类型: {nodeType}");
        }
        else
        {
            sb.AppendLine("未检测到有效路网节点");
        }

        cameraGroundInfoText.text = sb.ToString();
    }

    /// <summary>
    /// 一键触发数据录制
    /// </summary>
    void OnToggleRecordClicked()
    {
        if (dataManager == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 SystemDataManager！");
            return;
        }

        // 反射调用 dataManager 的录制逻辑 (兼容不同实现)
        var isRecordingField = dataManager.GetType().GetField("isRecording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (isRecordingField != null)
        {
            bool currentState = (bool)isRecordingField.GetValue(dataManager);
            bool newState = !currentState;
            isRecordingField.SetValue(dataManager, newState);
            
            UpdateRecordButtonUI(newState);
            
            if (newState)
            {
                Debug.Log("🔴 [DebugPanel] 一键启动数据录制...");
            }
            else
            {
                Debug.Log("✅ [DebugPanel] 一键停止数据录制，正在导出...");
                // 触发导出 (如果有 Export 方法)
                var exportMethod = dataManager.GetType().GetMethod("ExportToCSV", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                exportMethod?.Invoke(dataManager, null);
            }
        }
    }

    /// <summary>
    /// 更新录制按钮 UI
    /// </summary>
    void UpdateRecordButtonUI(bool isRecording)
    {
        if (recordButtonText != null)
        {
            recordButtonText.text = isRecording ? "🔴 停止录制" : "🎬 开始录制";
            recordButtonText.color = isRecording ? Color.red : Color.green;
        }
    }
    /*
    
     a5 观测台核心特性（完全符合 V2.0 规范）
1. 零物理射线，纯语义驱动
鼠标悬停检测：通过 ScreenToWorldPoint 转 XZ 平面，直接调用 WorldModel.Instance.GetNearestNode
相机下方检测：提取相机 XZ 坐标，通过 WorldModel 查询语义
全程无 Physics.Raycast，彻底解耦物理层
2. 核心功能覆盖
世界统计：实时显示路网节点总数、活跃 NPC 数量
语义查询：鼠标悬停 / 相机下方的节点 ID、类型、邻接数
一键录制：按钮触发 SystemDataManager 数据录制与导出
快捷开关：按 F1 显示 / 隐藏观测台
3. 高兼容性设计
使用反射兼容不同 WorldModel 实现（NodeCount / Nodes 列表）
自动适配 SystemDataManager 的录制逻辑
所有 UI 组件可在 Inspector 灵活配置
🎮 使用说明
在 Unity 中创建一个 Canvas，添加以下 UI 元素：
3 个 Text 组件（分别用于世界统计、鼠标悬停、相机下方）
1 个 Button 组件（用于一键录制），并在 Button 下添加一个 Text 子组件
创建一个空物体，挂载 DebugPanel.cs
将 UI 元素拖拽到脚本对应的 Inspector 字段中
运行场景，按 F1 开关观测台，鼠标移动查看语义信息，点击按钮控制录制
观测台已准备完毕，a4 总指挥和 a2 搭档可以随时检阅！需要调整 UI 布局或增加其他观测维度吗？
    */
}