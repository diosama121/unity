using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

public class DebugPanel : MonoBehaviour
{
    [Header("=== UI 文本组件 ===")]
    public TextMeshProUGUI worldStatsText;
    public TextMeshProUGUI mouseHoverInfoText;
    public TextMeshProUGUI cameraGroundInfoText;

    [Header("=== UI 按钮组件 ===")]
    public Button toggleRecordButton;
    public TextMeshProUGUI recordButtonText;
    public Button toggleCountrysideButton;
    public TextMeshProUGUI modeButtonText;

    [Header("=== 观测台设置 ===")]
    public KeyCode togglePanelKey = KeyCode.F1; // 开关面板快捷键
    public float updateInterval = 0.1f;         // 数据刷新频率

    // 内部引用缓存
    private SystemDataManager dataManager;
    private RoadNetworkGenerator roadGen;
    private TrafficManager trafficManager;
    private ROS2BridgeV2 ros2Bridge;
    private float updateTimer;
    private bool isPanelVisible = true;
    private float fpsAccumulator = 0f;
    private int fpsFrameCount = 0;
    private float currentFPS = 60f;

    void Start()
    {
        dataManager = FindObjectOfType<SystemDataManager>();
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        trafficManager = FindObjectOfType<TrafficManager>();
        ros2Bridge = FindObjectOfType<ROS2BridgeV2>();

        if (worldStatsText == null)
        {
            CreateDebugUI();
        }

        if (toggleRecordButton != null)
        {
            toggleRecordButton.onClick.AddListener(OnToggleRecordClicked);
        }

        if (toggleCountrysideButton != null)
        {
            toggleCountrysideButton.onClick.AddListener(ToggleMode);
        }

        UpdateWorldStats();
        UpdateRecordButtonUI(false);
        UpdateModeButtonUI();
    }

    private void CreateDebugUI()
    {
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("NotoSansSC-Regular SDF");
        if (font != null) font.atlasPopulationMode = AtlasPopulationMode.Dynamic;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject panelGo = new GameObject("PanelBg");
        panelGo.transform.SetParent(transform, false);
        RectTransform panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0, 1);
        panelRt.anchorMax = new Vector2(0, 1);
        panelRt.pivot = new Vector2(0, 1);
        panelRt.anchoredPosition = new Vector2(10, -10);
        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 440f);
        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 260f);
        Image panelBg = panelGo.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.75f);

        worldStatsText = CreateTMPText("StatsText", panelRt, font, 16f,
            new Vector2(8, -8), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), 424f, 120f);

        mouseHoverInfoText = CreateTMPText("HoverText", panelRt, font, 14f,
            new Vector2(8, -132), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), 424f, 55f);

        cameraGroundInfoText = CreateTMPText("CameraText", panelRt, font, 14f,
            new Vector2(8, -192), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), 424f, 55f);

        GameObject btnBar = new GameObject("ButtonBar");
        btnBar.transform.SetParent(panelRt, false);
        RectTransform barRt = btnBar.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0, 1);
        barRt.anchorMax = new Vector2(0, 1);
        barRt.pivot = new Vector2(0, 1);
        barRt.anchoredPosition = new Vector2(8, -252);
        barRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 424f);
        barRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 26f);

        toggleRecordButton = CreateButton("RecordBtn", barRt, font, "录制",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 1), 208f, 24f);
        recordButtonText = toggleRecordButton.GetComponentInChildren<TextMeshProUGUI>();
        toggleRecordButton.onClick.AddListener(OnToggleRecordClicked);
    }

    private TextMeshProUGUI CreateTMPText(string name, RectTransform parent, TMP_FontAsset font, float fontSize,
        Vector2 pos, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float width, float height)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        return tmp;
    }

    private Button CreateButton(string name, RectTransform parent, TMP_FontAsset font, string label,
        Vector2 pos, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float width, float height)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        TextMeshProUGUI tmp = CreateTMPText("Label", rt, font, 16f,
            Vector2.zero,
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), width, height);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return btn;
    }

    void Update()
    {
        fpsAccumulator += Time.unscaledDeltaTime;
        fpsFrameCount++;
        if (fpsAccumulator >= 0.25f)
        {
            currentFPS = fpsFrameCount / fpsAccumulator;
            fpsAccumulator = 0f;
            fpsFrameCount = 0;
        }

        if (Input.GetKeyDown(togglePanelKey))
        {
            isPanelVisible = !isPanelVisible;
            GetComponent<Canvas>().enabled = isPanelVisible;
        }

        if (!isPanelVisible) return;

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
        sb.AppendLine("<color=#FFDD44>=== 系统监控面板 ===</color>");
        sb.AppendLine($"<color=#00FFAA>FPS: {currentFPS:F0}</color>");

        int nodeCount = 0;
        if (WorldModel.Instance != null)
        {
            nodeCount = WorldModel.Instance.NodeCount;
        }
        sb.AppendLine($"路网节点: {nodeCount}");

        int npcCount = (trafficManager != null && trafficManager.ActiveNPCs != null)
            ? trafficManager.ActiveNPCs.Count : 0;
        sb.AppendLine($"<color=#00FFAA>活跃车辆: {npcCount}</color>");

        if (ros2Bridge != null)
        {
            string status = ros2Bridge.isConnected ? "已连接" : "未连接";
            string freq = ros2Bridge.sendRate.ToString("F0") + "Hz";
            string pcd = ros2Bridge.isConnected ? "Active" : "Idle";
            sb.AppendLine($"<color=#33EE55>ROS2: {status} | {freq} | PCD:{pcd}</color>");
        }
        else
        {
            sb.AppendLine("<color=#888888>ROS2 Bridge 未加载</color>");
        }

        worldStatsText.text = sb.ToString();
    }

    /// <summary>
    /// Update semantic info at mouse hover position (zero physics ray, semantic data driven)
    /// </summary>
    void UpdateMouseHoverInfo()
    {
        if (mouseHoverInfoText == null || WorldModel.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 鼠标悬停语义 ===");

        if (roadGen != null)
        {
            sb.AppendLine($"当前模式: {(roadGen.isCountryside ? " 乡村起伏" : " 城市纯平")}");
            sb.AppendLine($"当前种子 (Seed): {roadGen.seed}");
        }
        else
        {
            sb.AppendLine("未找到 RoadNetworkGenerator 组件");
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

        // 统一高程观测
        float terrainHeight = WorldModel.Instance.GetUnifiedHeight(mouseXZ.x, mouseXZ.y);
        sb.AppendLine($"地表绝对高程: {terrainHeight:F2} m");

        mouseHoverInfoText.text = sb.ToString();
    }

    /// <summary>
    /// 更新相机下方的语义信息 (零物理射线)
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

    public void ToggleMode()
    {
        if (roadGen == null)
        {
            Debug.LogWarning("[DebugPanel] 未找到 RoadNetworkGenerator，无法切换模式！");
            return;
        }

        // 翻转模式状态
        roadGen.isCountryside = !roadGen.isCountryside;
        Debug.Log(roadGen.isCountryside ? "[DebugPanel] 已切换至乡村起伏模式" : "[DebugPanel] 已切换至城市纯平模式");
        
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
                Debug.Log(" [DebugPanel] 一键启动数据录制...");
            }
            else
            {
                Debug.Log("[DebugPanel] 一键停止数据录制，正在导出...");
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
            recordButtonText.text = isRecording ? "停止录制" : "录制";
            recordButtonText.color = isRecording ? Color.red : Color.green;
        }
    }
}