using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// <summary>
/// V4.1 上帝视角观测台 (a5 视觉与数据观测官)
/// 核心准则：零物理射线，纯语义数据驱动
/// 功能：节点总数统计、NPC活跃监控、模式状态实时观测、城乡一键切换、高程场健康度监控、一键数据录制
/// </summary>
public class DebugPanel : MonoBehaviour
{
    [Header("=== UI 文本组件 ===")]
    public Text worldStatsText;          // 世界统计信息
    public Text mouseHoverInfoText;      // 鼠标悬停语义信息
    public Text cameraGroundInfoText;    // 相机下方语义信息

    [Header("=== UI 按钮组件 ===")]
    public Button toggleRecordButton;     // 一键录制按钮
    public Text recordButtonText;         // 录制按钮状态文本
    public Button toggleCountrysideButton;// 城乡模式切换按钮
    public Text modeButtonText;           // 模式按钮状态文本

    [Header("=== 观测台设置 ===")]
    public KeyCode togglePanelKey = KeyCode.F1; // 开关面板快捷键
    public float updateInterval = 0.1f;         // 数据刷新频率

    // 内部引用缓存
    private SystemDataManager dataManager;
    private RoadNetworkGenerator roadGen;
    private float updateTimer;
    private bool isPanelVisible = true;

    void Start()
    {
        // 预缓存核心管理器，避免每帧查找损耗
        dataManager = FindObjectOfType<SystemDataManager>();
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        
        // 初始化录制按钮事件
        if (toggleRecordButton != null)
        {
            toggleRecordButton.onClick.AddListener(OnToggleRecordClicked);
        }
        
        // 初始化城乡模式切换按钮事件
        if (toggleCountrysideButton != null)
        {
            toggleCountrysideButton.onClick.AddListener(OnToggleCountrysideClicked);
        }
        
        // 初始更新UI
        UpdateWorldStats();
        UpdateRecordButtonUI(false);
        UpdateModeButtonUI();
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
        int nodeCount = 0;
        if (WorldModel.Instance != null)
        {
            var nodeCountProp = WorldModel.Instance.GetType().GetProperty("NodeCount");
            if (nodeCountProp != null)
            {
                nodeCount = (int)nodeCountProp.GetValue(WorldModel.Instance);
            }
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

        // 2. 获取活跃 NPC 数量
        int npcCount = FindObjectsOfType<SimpleAutoDrive>().Length;
        sb.AppendLine($"活跃 NPC 数量: {npcCount}");

        worldStatsText.text = sb.ToString();
    }

    /// <summary>
    /// 更新鼠标悬停位置的语义信息 (零物理射线 + V4.1 状态监控)
    /// </summary>
    void UpdateMouseHoverInfo()
    {
        if (mouseHoverInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 🖱️ 鼠标悬停语义 ===");

        // 【V4.1 核心新增】生成模式与种子状态探测
        if (roadGen != null)
        {
            sb.AppendLine($"当前模式: {(roadGen.isCountryside ? "🏞️ 乡村起伏" : "🏙️ 城市纯平")}");
            sb.AppendLine($"当前种子 (Seed): {roadGen.seed}");
        }
        else
        {
            sb.AppendLine("⚠️ 未找到 RoadNetworkGenerator 组件");
        }

        // 零物理射线坐标转换
        Vector3 mouseScreenPos = Input.mousePosition;
        mouseScreenPos.z = 10f;
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        
        // 提取 XZ 坐标
        Vector2 mouseXZ = new Vector2(mouseWorldPos.x, mouseWorldPos.z);
        RoadNode nearestNode = WorldModel.Instance.GetNearestNode(new Vector3(mouseXZ.x, 0, mouseXZ.y));

        if (nearestNode != null)
        {
            sb.AppendLine($"鼠标 XZ: ({mouseXZ.x:F1}, {mouseXZ.y:F1})");
            sb.AppendLine($"最近节点 ID: {nearestNode.Id}");
            sb.AppendLine($"节点世界坐标: ({nearestNode.WorldPos.x:F1}, {nearestNode.WorldPos.y:F1}, {nearestNode.WorldPos.z:F1})");
            
            // 节点类型判定
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

        // 【V4.1 核心新增】统一高程观测
        float unifiedY = WorldModel.Instance.GetUnifiedHeight(mouseXZ.x, mouseXZ.y);
        sb.AppendLine($"地表绝对高程: {unifiedY:F2} m");

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

        Vector3 cameraPos = Camera.main.transform.position;
        Vector2 cameraXZ = new Vector2(cameraPos.x, cameraPos.z);
        
        RoadNode nearestNode = WorldModel.Instance.GetNearestNode(new Vector3(cameraXZ.x, 0, cameraXZ.y));

        if (nearestNode != null)
        {
            sb.AppendLine($"相机 XZ: ({cameraXZ.x:F1}, {cameraXZ.y:F1})");
            sb.AppendLine($"最近节点 ID: {nearestNode.Id}");
            
            string nodeType = "未知";
            if (nearestNode.NeighborIds != null)
            {
                if (nearestNode.NeighborIds.Count >= 3) nodeType = "路口";
                else if (nearestNode.NeighborIds.Count == 2) nodeType = "路段";
                else nodeType = "端点";
            }
            sb.AppendLine($"节点类型: {nodeType}");
            
            // 相机下方统一高程监控
            float unifiedHeight = WorldModel.Instance.GetUnifiedHeight(cameraXZ.x, cameraXZ.y);
            sb.AppendLine($"地表绝对高程: {unifiedHeight:F2} m");
        }
        else
        {
            sb.AppendLine("未检测到有效路网节点");
        }

        cameraGroundInfoText.text = sb.ToString();
    }

    /// <summary>
    /// 【V4.1 新增】一键切换城乡生成模式
    /// </summary>
    void OnToggleCountrysideClicked()
    {
        if (roadGen == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 RoadNetworkGenerator，无法切换模式！");
            return;
        }

        // 翻转模式状态
        roadGen.isCountryside = !roadGen.isCountryside;
        Debug.Log(roadGen.isCountryside ? "🏞️ [DebugPanel] 已切换至乡村起伏模式" : "🏙️ [DebugPanel] 已切换至城市纯平模式");
        
        // 更新按钮UI
        UpdateModeButtonUI();
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
                var exportMethod = dataManager.GetType().GetMethod("ExportToCSV", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                exportMethod?.Invoke(dataManager, null);
            }
        }
    }

    /// <summary>
    /// 更新模式按钮 UI
    /// </summary>
    void UpdateModeButtonUI()
    {
        if (modeButtonText != null && roadGen != null)
        {
            modeButtonText.text = roadGen.isCountryside ? "切换至城市纯平" : "切换至乡村起伏";
            modeButtonText.color = roadGen.isCountryside ? Color.green : Color.blue;
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
}