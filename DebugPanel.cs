using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// <summary>
/// V4.2 上帝视角观测台 (a5 视觉与数据观测官)
/// V4.2 优化：去除反射调用，改用 SystemDataManager.Instance 公共API
/// 功能：节点总数统计、NPC活跃监控、模式状态实时观测、城乡一键切换、高程场健康度监控、一键数据录制/导出
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
    public Button exportReportButton;     // ★ V4.2 一键导出完整报告按钮
    public Text exportButtonText;         // ★ 导出按钮文本

    [Header("=== 观测台设置 ===")]
    public KeyCode togglePanelKey = KeyCode.F1; // 开关面板快捷键
    public float updateInterval = 0.1f;         // 数据刷新频率

    // 内部引用缓存
    private RoadNetworkGenerator _roadGen;
    private float _updateTimer;
    private bool _isPanelVisible = true;

    void Start()
    {
        _roadGen = FindObjectOfType<RoadNetworkGenerator>();

        // ★ V4.2：直接用 SystemDataManager.Instance，不再反射
        if (toggleRecordButton != null)
            toggleRecordButton.onClick.AddListener(OnToggleRecordClicked);

        if (toggleCountrysideButton != null)
            toggleCountrysideButton.onClick.AddListener(OnToggleCountrysideClicked);

        if (exportReportButton != null)
            exportReportButton.onClick.AddListener(OnExportReportClicked);

        UpdateWorldStats();
        UpdateRecordButtonUI(false);
        UpdateModeButtonUI();
    }

    void Update()
    {
        if (Input.GetKeyDown(togglePanelKey))
        {
            _isPanelVisible = !_isPanelVisible;
            gameObject.SetActive(_isPanelVisible);
        }

        if (!_isPanelVisible) return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            UpdateWorldStats();
            UpdateMouseHoverInfo();
            UpdateCameraGroundInfo();

            // ★ V4.2：同步录制按钮状态
            if (SystemDataManager.Instance != null)
            {
                bool recording = SystemDataManager.Instance.IsRecording;
                UpdateRecordButtonUI(recording);
            }
            _updateTimer = 0f;
        }
    }

    /// <summary>
    /// 更新世界统计信息（节点总数、NPC数量）
    /// </summary>
    void UpdateWorldStats()
    {
        if (worldStatsText == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 世界语义统计 ===");

        // 1. 节点总数
        int nodeCount = 0;
        if (WorldModel.Instance != null)
        {
            nodeCount = WorldModel.Instance.NodeCount;
        }
        sb.AppendLine($"路网节点总数: {nodeCount}");

        // 2. 车道数
        int laneCount = WorldModel.Instance?.GlobalLanes?.Count ?? 0;
        sb.AppendLine($"车道总数: {laneCount}");

        // 3. 活跃 NPC 数量
        int npcCount = FindObjectsOfType<SimpleAutoDrive>().Length;
        sb.AppendLine($"活跃 NPC 数量: {npcCount}");

        // 4. 连接器数
        int connCount = WorldModel.Instance?.GlobalConnectors?.Count ?? 0;
        sb.AppendLine($"连接器总数: {connCount}");

        worldStatsText.text = sb.ToString();
    }

    /// <summary>
    /// 更新鼠标悬停位置语义信息
    /// </summary>
    void UpdateMouseHoverInfo()
    {
        if (mouseHoverInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 鼠标悬停语义 ===");

        if (_roadGen != null)
        {
            sb.AppendLine($"当前模式: {(_roadGen.isCountryside ? "乡村起伏" : "城市纯平")}");
            sb.AppendLine($"当前种子 (Seed): {_roadGen.seed}");
        }
        else
        {
            sb.AppendLine("未找到 RoadNetworkGenerator 组件");
        }

        Vector3 mouseScreenPos = Input.mousePosition;
        mouseScreenPos.z = 10f;
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        Vector2 mouseXZ = new Vector2(mouseWorldPos.x, mouseWorldPos.z);
        RoadNode nearestNode = WorldModel.Instance.GetNearestNode(new Vector3(mouseXZ.x, 0, mouseXZ.y));

        if (nearestNode != null)
        {
            sb.AppendLine($"鼠标 XZ: ({mouseXZ.x:F1}, {mouseXZ.y:F1})");
            sb.AppendLine($"最近节点 ID: {nearestNode.Id}");
            sb.AppendLine($"节点坐标: ({nearestNode.WorldPos.x:F1}, {nearestNode.WorldPos.y:F1}, {nearestNode.WorldPos.z:F1})");

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

        float unifiedY = WorldModel.Instance.GetUnifiedHeight(mouseXZ.x, mouseXZ.y);
        sb.AppendLine($"地表绝对高程: {unifiedY:F2} m");

        mouseHoverInfoText.text = sb.ToString();
    }

    /// <summary>
    /// 更新相机下方语义信息
    /// </summary>
    void UpdateCameraGroundInfo()
    {
        if (cameraGroundInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 相机下方语义 ===");

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

            float unifiedHeight = WorldModel.Instance.GetUnifiedHeight(cameraXZ.x, cameraXZ.y);
            sb.AppendLine($"地表绝对高程: {unifiedHeight:F2} m");
        }
        else
        {
            sb.AppendLine("未检测到有效路网节点");
        }

        cameraGroundInfoText.text = sb.ToString();
    }

    // ============================================================
    // 按钮回调
    // ============================================================

    /// <summary>城乡模式切换</summary>
    void OnToggleCountrysideClicked()
    {
        if (_roadGen == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 RoadNetworkGenerator");
            return;
        }
        _roadGen.isCountryside = !_roadGen.isCountryside;
        Debug.Log(_roadGen.isCountryside ? "[DebugPanel] 已切换至乡村起伏模式" : "[DebugPanel] 已切换至城市纯平模式");
        UpdateModeButtonUI();
    }

    /// <summary>一键录制（F9等效）</summary>
    void OnToggleRecordClicked()
    {
        if (SystemDataManager.Instance == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 SystemDataManager.Instance");
            return;
        }
        SystemDataManager.Instance.ToggleRecording();
    }

    /// <summary>★ V4.2：一键导出完整报告（F11等效）</summary>
    void OnExportReportClicked()
    {
        if (SystemDataManager.Instance == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 SystemDataManager.Instance");
            return;
        }
        string path = SystemDataManager.Instance.ExportFullReport();
        if (!string.IsNullOrEmpty(path))
        {
            Debug.Log($"[DebugPanel] 报告已导出: {path}");
        }
    }

    // ============================================================
    // UI 更新
    // ============================================================

    void UpdateModeButtonUI()
    {
        if (modeButtonText != null && _roadGen != null)
        {
            modeButtonText.text = _roadGen.isCountryside ? "切换至城市纯平" : "切换至乡村起伏";
            modeButtonText.color = _roadGen.isCountryside ? Color.green : Color.blue;
        }
    }

    void UpdateRecordButtonUI(bool isRecording)
    {
        if (recordButtonText != null)
        {
            recordButtonText.text = isRecording ? "停止录制" : "开始录制";
            recordButtonText.color = isRecording ? Color.red : Color.green;
        }
    }
}