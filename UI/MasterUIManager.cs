using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MasterUIManager : MonoBehaviour
{
    [Header("=== Auto Setup ===")]
    public bool autoGenerateUI = true;
    public TMP_FontAsset customFont;

    private Font _legacyFont;
    private Font legacyFont
    {
        get
        {
            if (_legacyFont == null) _legacyFont = Resources.Load<Font>("NotoSansSC-Regular");
            return _legacyFont;
        }
    }

    private SimpleCarController carController;
    private SimpleAutoDrive autoDrive;
    private TrafficLightManager trafficLightManager;
    private ROS2BridgeV2 ros2Bridge;
    private CameraController cameraController;
    private TrafficManager trafficManager;
    private RoadNetworkGenerator roadGen;
    private ProceduralRoadBuilder roadBuilder;

    private bool isRebinding = false;

    private Canvas mainCanvas;
    private GameObject topBar;
    private GameObject leftPanel;
    private GameObject rightPanel;
    private GameObject hudPanel;
    private GameObject keyPanel;
    private GameObject rightScrollContent;

    private GameObject moduleBaseSettings;
    private GameObject moduleTerrain;
    private GameObject moduleTraffic;
    private GameObject moduleSystem;
    private GameObject moduleCamera;

    private GameObject citySubPanel;
    private GameObject countrysideSubPanel;

    private Dictionary<string, TextMeshProUGUI> hudTexts = new Dictionary<string, TextMeshProUGUI>();
    private Dictionary<string, TextMeshProUGUI> keyTexts = new Dictionary<string, TextMeshProUGUI>();
    private Dictionary<string, InputField> inputFields = new Dictionary<string, InputField>();
    private Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private Dictionary<string, Dropdown> dropdowns = new Dictionary<string, Dropdown>();

    private float hudRefreshInterval = 0.2f;
    private float hudRefreshTimer = 0f;

    private static readonly Vector2 refResolution = new Vector2(1920, 1080);

    private bool isMinimalMode = false;
    private bool autoRegenTerrain = false;

    private GameObject minimapRawImage;
    private GameObject torOverlay;
    private TextMeshProUGUI torOverlayText;
    private bool isTORFlashing = false;
    private float torFlashTimer = 0f;
    private float torFlashDuration = 0f;

    private GameObject thoughtStreamPanel;
    private TextMeshProUGUI thoughtStreamText;
    private List<string> thoughtLines = new List<string>();
    private const int maxThoughtLines = 8;

    private float heartbeatAlpha = 1f;

    void Awake()
    {
        FindAllComponents();

        if (autoGenerateUI)
        {
            BuildCompleteUI();
        }
    }

    void Start()
    {
        SyncAllUIFromComponents();
        BindAllUIEvents();

        if (RuntimeInputManager.Instance != null)
        {
            RuntimeInputManager.Instance.OnKeyRebound += OnKeyRebound;
        }
    }

    void Update()
    {
        heartbeatAlpha = 0.5f + Mathf.Sin(Time.time * 4f) * 0.5f;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleMinimalMode();
        }
if (Input.GetKeyDown(KeyCode.R) && carController != null)
            {
                carController.ResetPosition();
                if (thoughtStreamText != null) AppendThoughtLine("已将主车位置重置到安全路面");
            }
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (carController != null) carController.ResetPosition();
        }

        if (RuntimeInputManager.Instance == null) return;

        if (!isRebinding)
        {
            // [ESC] 隐藏/显示所有 UI 窗口
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool anyActive = (leftPanel != null && leftPanel.activeSelf) ||
                                 (rightPanel != null && rightPanel.activeSelf) ||
                                 (thoughtStreamPanel != null && thoughtStreamPanel.activeSelf) ||
                                 (topBar != null && topBar.activeSelf);
                if (leftPanel != null) leftPanel.SetActive(!anyActive);
                if (rightPanel != null) rightPanel.SetActive(!anyActive);
                if (thoughtStreamPanel != null) thoughtStreamPanel.SetActive(!anyActive);
                if (topBar != null) topBar.SetActive(!anyActive);
            }

            // [F1] 开关左侧数据和日志
            if (Input.GetKeyDown(KeyCode.F1))
            {
                bool newState = leftPanel != null && !leftPanel.activeSelf;
                if (leftPanel != null) leftPanel.SetActive(newState);
                if (thoughtStreamPanel != null) thoughtStreamPanel.SetActive(newState);
            }

            // [F2] 开关右侧参数设置
            if (Input.GetKeyDown(KeyCode.F2))
            {
                if (rightPanel != null) rightPanel.SetActive(!rightPanel.activeSelf);
            }

            // [T] 切换自动驾驶/手动驾驶
            if (Input.GetKeyDown(KeyCode.T) && carController != null)
            {
                carController.ToggleMode();
                if (thoughtStreamText != null) AppendThoughtLine("已切换驾驶模式: " + (carController.autoMode ? "自动" : "手动"));
            }

            // [N] 重新导航 (重置状态)
            if (Input.GetKeyDown(KeyCode.N) && autoDrive != null)
            {
                autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
                if (thoughtStreamText != null) AppendThoughtLine("手动触发：导航状态重置");
            }

            // [Space] 原有刹车逻辑
            if (RuntimeInputManager.Instance != null && RuntimeInputManager.Instance.GetKey("Brake") && carController != null)
            {
                carController.SetAutoBrake(carController.brakeDeceleration);
            }
        }

        if (isTORFlashing)
        {
            torFlashTimer += Time.deltaTime;
            float cycle = torFlashTimer % 0.5f;
            bool visible = cycle < 0.25f;
            if (torOverlay != null) torOverlay.SetActive(visible);
            if (torFlashTimer >= torFlashDuration)
            {
                isTORFlashing = false;
                if (torOverlay != null) torOverlay.SetActive(false);
            }
        }

        hudRefreshTimer += Time.unscaledDeltaTime;
        if (hudRefreshTimer >= hudRefreshInterval)
        {
            RefreshHUD();
            hudRefreshTimer = 0f;
        }
    }

    void OnDestroy()
    {
        if (RuntimeInputManager.Instance != null)
        {
            RuntimeInputManager.Instance.OnKeyRebound -= OnKeyRebound;
        }
    }

    void OnKeyRebound(string actionName, KeyCode newKey)
    {
        if (actionName == "SwitchCam" && cameraController != null)
        {
            cameraController.modeSwitchKey = newKey;
        }
        RefreshAllKeyTexts();
    }

    #region Component Discovery

    void FindAllComponents()
    {
        carController = FindObjectOfType<SimpleCarController>();
        autoDrive = FindObjectOfType<SimpleAutoDrive>();
        trafficLightManager = FindObjectOfType<TrafficLightManager>();
        ros2Bridge = FindObjectOfType<ROS2BridgeV2>();
        cameraController = FindObjectOfType<CameraController>();
        trafficManager = FindObjectOfType<TrafficManager>();
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        roadBuilder = FindObjectOfType<ProceduralRoadBuilder>();

        if (carController == null) Debug.LogWarning("[MasterUIManager] SimpleCarController not found");
        if (autoDrive == null) Debug.LogWarning("[MasterUIManager] SimpleAutoDrive not found");
        if (trafficLightManager == null) Debug.LogWarning("[MasterUIManager] TrafficLightManager not found");
        if (cameraController == null) Debug.LogWarning("[MasterUIManager] CameraController not found");
        if (trafficManager == null) Debug.LogWarning("[MasterUIManager] TrafficManager not found");
        if (roadGen == null) Debug.LogWarning("[MasterUIManager] RoadNetworkGenerator not found");
    }

    #endregion

    void BuildTOROverlay(GameObject root, TMP_FontAsset font)
    {
        torOverlay = new GameObject("TOROverlay");
        torOverlay.transform.SetParent(root.transform, false);
        RectTransform torRT = torOverlay.AddComponent<RectTransform>();
        torRT.anchorMin = new Vector2(0.2f, 0.35f);
        torRT.anchorMax = new Vector2(0.8f, 0.65f);
        torRT.offsetMin = Vector2.zero;
        torRT.offsetMax = Vector2.zero;

        Image torBg = torOverlay.AddComponent<Image>();
        torBg.color = new Color(0.85f, 0.05f, 0.05f, 0.7f);

        GameObject torTextGO = new GameObject("Text");
        torTextGO.transform.SetParent(torOverlay.transform, false);
        torOverlayText = torTextGO.AddComponent<TextMeshProUGUI>();
        torOverlayText.text = "TAKE OVER REQUEST\nEMERGENCY BRAKE";
        torOverlayText.font = font;
        torOverlayText.fontSize = 48;
        torOverlayText.enableAutoSizing = true;
        torOverlayText.fontSizeMin = 18;
        torOverlayText.fontSizeMax = 48;
        torOverlayText.fontStyle = FontStyles.Bold;
        torOverlayText.color = Color.white;
        torOverlayText.alignment = TextAlignmentOptions.Center;
        RectTransform ttxtRT = torTextGO.GetComponent<RectTransform>();
        ttxtRT.anchorMin = Vector2.zero;
        ttxtRT.anchorMax = Vector2.one;
        ttxtRT.offsetMin = new Vector2(16, 16);
        ttxtRT.offsetMax = new Vector2(-16, -16);

        torOverlay.SetActive(false);
    }

    void BuildThoughtStream(GameObject root, TMP_FontAsset font)
    {
        thoughtStreamPanel = new GameObject("ThoughtStream");
        thoughtStreamPanel.transform.SetParent(root.transform, false);
        RectTransform tsRT = thoughtStreamPanel.AddComponent<RectTransform>();
        tsRT.anchorMin = new Vector2(0.25f, 0);
        tsRT.anchorMax = new Vector2(0.75f, 0.15f);
        tsRT.offsetMin = new Vector2(4, 4);
        tsRT.offsetMax = new Vector2(-4, 0);

        Image tsBg = thoughtStreamPanel.AddComponent<Image>();
        tsBg.color = new Color(0, 0, 0, 0.45f);

        GameObject textChild = new GameObject("Text");
        textChild.transform.SetParent(thoughtStreamPanel.transform, false);
        thoughtStreamText = textChild.AddComponent<TextMeshProUGUI>();
        thoughtStreamText.font = font;
        thoughtStreamText.fontSize = 11;
        thoughtStreamText.enableAutoSizing = true;
        thoughtStreamText.fontSizeMin = 7;
        thoughtStreamText.fontSizeMax = 11;
        thoughtStreamText.color = new Color(0.3f, 0.9f, 0.5f);
        thoughtStreamText.alignment = TextAlignmentOptions.BottomLeft;
        thoughtStreamText.enableWordWrapping = false;
        thoughtStreamText.overflowMode = TextOverflowModes.Overflow;
        RectTransform ttsRT = textChild.GetComponent<RectTransform>();
        ttsRT.anchorMin = Vector2.zero;
        ttsRT.anchorMax = Vector2.one;
        ttsRT.offsetMin = new Vector2(6, 4);
        ttsRT.offsetMax = new Vector2(-6, -4);

        // 导出日志按钮
        GameObject exportBtnGO = new GameObject("ExportLogBtn");
        exportBtnGO.transform.SetParent(thoughtStreamPanel.transform, false);
        RectTransform ebRT = exportBtnGO.AddComponent<RectTransform>();
        ebRT.anchorMin = new Vector2(0.92f, 0.55f);
        ebRT.anchorMax = new Vector2(0.99f, 0.9f);
        ebRT.offsetMin = Vector2.zero;
        ebRT.offsetMax = Vector2.zero;
        Button exportBtn = exportBtnGO.AddComponent<Button>();
        Image ebImg = exportBtnGO.AddComponent<Image>();
        ebImg.color = new Color(0.2f, 0.5f, 0.3f, 0.8f);
        exportBtn.targetGraphic = ebImg;
        GameObject ebTxtGO = new GameObject("Text");
        ebTxtGO.transform.SetParent(exportBtnGO.transform, false);
        TextMeshProUGUI ebTxt = ebTxtGO.AddComponent<TextMeshProUGUI>();
        ebTxt.text = "导出日志";
        ebTxt.font = font;
        ebTxt.fontSize = 8;
        ebTxt.enableAutoSizing = true;
        ebTxt.fontSizeMin = 5;
        ebTxt.fontSizeMax = 9;
        ebTxt.color = Color.white;
        ebTxt.alignment = TextAlignmentOptions.Center;
        RectTransform ebTxtRT = ebTxtGO.GetComponent<RectTransform>();
        ebTxtRT.anchorMin = Vector2.zero;
        ebTxtRT.anchorMax = Vector2.one;
        ebTxtRT.offsetMin = Vector2.zero;
        ebTxtRT.offsetMax = Vector2.zero;
        exportBtn.onClick.AddListener(() =>
        {
            try
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = System.IO.Path.Combine(Application.persistentDataPath, "log_" + timestamp + ".txt");
                string content = "";
                for (int i = 0; i < thoughtLines.Count; i++)
                    content += thoughtLines[i] + "\n";
                System.IO.File.WriteAllText(filePath, content);
                Debug.Log("[MasterUIManager] 日志已导出到: " + filePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[MasterUIManager] 日志导出失败: " + e.Message);
            }
        });
    }

    public void ShowTORWarning(float duration = 3f)
    {
        if (torOverlay == null) return;
        isTORFlashing = true;
        torFlashTimer = 0f;
        torFlashDuration = duration;
    }

    public void AppendThoughtLine(string line)
    {
        thoughtLines.Add(Time.time.ToString("F1") + "s > " + line);
        if (thoughtLines.Count > maxThoughtLines)
            thoughtLines.RemoveAt(0);
        if (thoughtStreamText != null)
        {
            string full = "";
            for (int i = thoughtLines.Count - 1; i >= 0; i--)
                full += thoughtLines[i] + "\n";
            thoughtStreamText.text = full;
        }
    }

    #region Toggle & Shortcuts

    void TryAutoRegenTerrain()
    {
        if (!autoRegenTerrain) return;
        if (roadGen != null) roadGen.Generate();
        if (roadBuilder != null) roadBuilder.BuildRoads();
        Debug.Log("[MasterUIManager] 自动重生成地形已触发");
    }

    void ToggleMinimalMode()
    {
        isMinimalMode = !isMinimalMode;
        if (rightPanel != null) rightPanel.SetActive(!isMinimalMode);
        DebugPanel dbg = FindObjectOfType<DebugPanel>();
        if (dbg != null)
        {
            Canvas dbgCanvas = dbg.GetComponent<Canvas>();
            if (dbgCanvas != null) dbgCanvas.enabled = !isMinimalMode;
        }
    }

    void ToggleAutoDrive()
    {
        if (autoDrive != null)
        {
            autoDrive.ToggleAutoDrive();
        }
    }

    #endregion

    #region Complete UI Build

    void BuildCompleteUI()
    {
        TMP_FontAsset font = customFont != null ? customFont : Resources.Load<TMP_FontAsset>("NotoSansSC-Regular SDF");
        if (font != null) font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        UIPanelBuilder.SharedFont = font;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("MasterUICanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = refResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = refResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        mainCanvas = canvas;

        Transform existing = canvas.transform.Find("MasterUIRoot");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject("MasterUIRoot");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        BuildTopBar(root, font);
        BuildLeftPanel(root, font);
        BuildRightPanel(root, font);
        BuildTOROverlay(root, font);
        BuildThoughtStream(root, font);

        if (rightPanel != null) rightPanel.SetActive(true);
        if (moduleBaseSettings != null) moduleBaseSettings.SetActive(true);
        SwitchToModule(moduleBaseSettings);
    }

    void BuildTopBar(GameObject root, TMP_FontAsset font)
    {
        topBar = new GameObject("TopBar");
        topBar.transform.SetParent(root.transform, false);
        RectTransform tbRT = topBar.AddComponent<RectTransform>();
        tbRT.anchorMin = new Vector2(0, 0.93f);
        tbRT.anchorMax = new Vector2(1, 1);
        tbRT.offsetMin = Vector2.zero;
        tbRT.offsetMax = Vector2.zero;

        Image tbBg = topBar.AddComponent<Image>();
        tbBg.color = new Color(0.06f, 0.06f, 0.1f, 0.6f);

        HorizontalLayoutGroup tbh = topBar.AddComponent<HorizontalLayoutGroup>();
        tbh.padding = new RectOffset(12, 12, 6, 6);
        tbh.spacing = 8;
        tbh.childAlignment = TextAnchor.MiddleLeft;
        tbh.childControlWidth = false;
        tbh.childControlHeight = true;
        tbh.childForceExpandWidth = false;
        tbh.childForceExpandHeight = true;

        CreateNavButton(topBar, "NavHUD", "路网", font, () => SwitchToModule(moduleBaseSettings));
        CreateNavButton(topBar, "NavTerrain", "地形", font, () => SwitchToModule(moduleTerrain));
        CreateNavButton(topBar, "NavTraffic", "交通", font, () => SwitchToModule(moduleTraffic));
        CreateNavButton(topBar, "NavSystem", "系统", font, () => SwitchToModule(moduleSystem));
        CreateNavButton(topBar, "NavCamera", "相机", font, () => SwitchToModule(moduleCamera));

        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(topBar.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        // 【新增】：自动重生成地形开关
        GameObject autoRegenToggleRow = new GameObject("AutoRegenToggleRow");
        autoRegenToggleRow.transform.SetParent(topBar.transform, false);
        autoRegenToggleRow.AddComponent<LayoutElement>().minWidth = 160;
        autoRegenToggleRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup artHlg = autoRegenToggleRow.AddComponent<HorizontalLayoutGroup>();
        artHlg.spacing = 6;
        artHlg.childAlignment = TextAnchor.MiddleCenter;
        artHlg.childControlWidth = true;
        artHlg.childControlHeight = true;
        artHlg.childForceExpandWidth = false;
        artHlg.childForceExpandHeight = true;

        GameObject artLabelGO = new GameObject("Label");
        artLabelGO.transform.SetParent(autoRegenToggleRow.transform, false);
        TextMeshProUGUI artLabel = artLabelGO.AddComponent<TextMeshProUGUI>();
        artLabel.text = "自动重生成";
        artLabel.font = font;
        artLabel.fontSize = 12;
        artLabel.enableAutoSizing = true;
        artLabel.fontSizeMin = 7;
        artLabel.fontSizeMax = 12;
        artLabel.color = Color.white;
        artLabel.alignment = TextAlignmentOptions.MiddleLeft;
        artLabelGO.AddComponent<LayoutElement>().minWidth = 80;

        GameObject artToggleGO = new GameObject("Toggle");
        artToggleGO.transform.SetParent(autoRegenToggleRow.transform, false);
        artToggleGO.AddComponent<LayoutElement>().minWidth = 30;
        artToggleGO.AddComponent<LayoutElement>().minHeight = 22;
        Toggle autoRegenToggle = artToggleGO.AddComponent<Toggle>();
        autoRegenToggle.isOn = false;

        GameObject artBgGO = new GameObject("Background");
        artBgGO.transform.SetParent(artToggleGO.transform, false);
        Image artBgImg = artBgGO.AddComponent<Image>();
        artBgImg.color = new Color(0.25f, 0.25f, 0.35f);
        RectTransform artBgRT = artBgGO.GetComponent<RectTransform>();
        artBgRT.anchorMin = Vector2.zero; artBgRT.anchorMax = Vector2.one;
        artBgRT.sizeDelta = Vector2.zero;
        autoRegenToggle.targetGraphic = artBgImg;

        GameObject artCheckGO = new GameObject("Checkmark");
        artCheckGO.transform.SetParent(artBgGO.transform, false);
        Image artCheckImg = artCheckGO.AddComponent<Image>();
        artCheckImg.color = new Color(0.3f, 0.8f, 1f);
        RectTransform artCheckRT = artCheckGO.GetComponent<RectTransform>();
        artCheckRT.anchorMin = new Vector2(0.1f, 0.1f);
        artCheckRT.anchorMax = new Vector2(0.9f, 0.9f);
        artCheckRT.sizeDelta = Vector2.zero;
        autoRegenToggle.graphic = artCheckImg;

        autoRegenToggle.onValueChanged.AddListener((on) => {
            autoRegenTerrain = on;
            AppendThoughtLine(on ? "自动重生成地形：开启" : "自动重生成地形：关闭");
        });

        // 【新增】：生成交通按钮
        GameObject trafficBtnGO = UIPanelBuilder.CreateButton(topBar, "BtnSpawnTraffic", "2. 生成交通");
        trafficBtnGO.AddComponent<LayoutElement>().minWidth = 120;
        Button trafficBtn = trafficBtnGO.GetComponent<Button>();
        trafficBtn.GetComponent<Image>().color = new Color(0.25f, 0.65f, 0.85f); // 蓝色
        trafficBtn.onClick.AddListener(() => {
            if (trafficManager != null) { trafficManager.ResetSpawnState(); trafficManager.SpawnNPCs(); }
            AppendThoughtLine("指令下发：生成NPC与交通流...");
        });

        GameObject rosDotGO = new GameObject("RosStatusDot");
        rosDotGO.transform.SetParent(topBar.transform, false);
        rosDotGO.AddComponent<LayoutElement>().minWidth = 20;
        rosDotGO.AddComponent<LayoutElement>().minHeight = 20;
        TextMeshProUGUI rosDot = rosDotGO.AddComponent<TextMeshProUGUI>();
        rosDot.text = "ROS2桥接";
        rosDot.font = font;
        rosDot.fontSize = 11;
        rosDot.enableAutoSizing = true;
        rosDot.fontSizeMin = 7;
        rosDot.fontSizeMax = 11;
        rosDot.color = Color.white;
        rosDot.alignment = TextAlignmentOptions.Center;
        rosDotGO.name = "RosStatusDot";

        GameObject timeLabelGO = new GameObject("TimeLabel");
        timeLabelGO.transform.SetParent(topBar.transform, false);
        timeLabelGO.AddComponent<LayoutElement>().minWidth = 80;
        TextMeshProUGUI timeLabel = timeLabelGO.AddComponent<TextMeshProUGUI>();
        timeLabel.text = "Time x1";
        timeLabel.font = font;
        timeLabel.fontSize = 12;
        timeLabel.enableAutoSizing = true;
        timeLabel.fontSizeMin = 7;
        timeLabel.fontSizeMax = 12;
        timeLabel.color = new Color(0.3f, 0.8f, 1f);
        timeLabel.alignment = TextAlignmentOptions.Center;
        timeLabelGO.name = "TimeLabel";
    }

    void CreateNavButton(GameObject parent, string name, string label, TMP_FontAsset font, UnityEngine.Events.UnityAction callback)
    {
        GameObject btnGO = new GameObject(name);
        btnGO.transform.SetParent(parent.transform, false);
        btnGO.AddComponent<LayoutElement>().minWidth = 70;
        btnGO.AddComponent<LayoutElement>().minHeight = 30;
        Button btn = btnGO.AddComponent<Button>();
        Image btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.18f, 0.22f, 0.32f);
        btn.targetGraphic = btnImg;
        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(btnGO.transform, false);
        TextMeshProUGUI txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.font = font;
        txt.fontSize = 13;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 8;
        txt.fontSizeMax = 13;
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        RectTransform txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        btn.onClick.AddListener(callback);
    }

    void BuildLeftPanel(GameObject root, TMP_FontAsset font)
    {
        leftPanel = new GameObject("LeftPanel");
        leftPanel.transform.SetParent(root.transform, false);
        RectTransform lpRT = leftPanel.AddComponent<RectTransform>();
        lpRT.anchorMin = new Vector2(0, 0);
        lpRT.anchorMax = new Vector2(0.25f, 0.93f);
        lpRT.offsetMin = new Vector2(8, 4);
        lpRT.offsetMax = new Vector2(0, 0);

        Image lpBg = leftPanel.AddComponent<Image>();
        lpBg.color = new Color(0.06f, 0.08f, 0.16f, 0.05f);

        ScrollRect scrollRect = leftPanel.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(leftPanel.transform, false);
        RectTransform vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        Image vpImg = viewportGO.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 1);
        Mask vpMask = viewportGO.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;

        GameObject scrollContent = new GameObject("ScrollContent");
        scrollContent.transform.SetParent(viewportGO.transform, false);
        RectTransform scRT = scrollContent.AddComponent<RectTransform>();
        scRT.anchorMin = new Vector2(0, 1);
        scRT.anchorMax = new Vector2(1, 1);
        scRT.pivot = new Vector2(0.5f, 1);
        scRT.anchoredPosition = Vector2.zero;
        scRT.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup scVLG = scrollContent.AddComponent<VerticalLayoutGroup>();
        scVLG.padding = new RectOffset(6, 6, 4, 4);
        scVLG.spacing = 4;
        scVLG.childAlignment = TextAnchor.UpperCenter;
        scVLG.childControlWidth = true;
        scVLG.childControlHeight = true;
        scVLG.childForceExpandWidth = true;
        scVLG.childForceExpandHeight = false;

        ContentSizeFitter scCSF = scrollContent.AddComponent<ContentSizeFitter>();
        scCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRT;
        scrollRect.content = scRT;

        BuildHUDPanel(scrollContent, font);
        BuildKeyPanel(scrollContent, font);
    }

    void BuildHUDPanel(GameObject parent, TMP_FontAsset font)
    {
        hudPanel = new GameObject("HUDPanel");
        hudPanel.transform.SetParent(parent.transform, false);
        hudPanel.AddComponent<LayoutElement>().minHeight = 300;
        Image hudBg = hudPanel.AddComponent<Image>();
        hudBg.color = new Color(0.06f, 0.08f, 0.16f, 0.6f);

        VerticalLayoutGroup hudVLG = hudPanel.AddComponent<VerticalLayoutGroup>();
        hudVLG.padding = new RectOffset(8, 8, 8, 8);
        hudVLG.spacing = 2;
        hudVLG.childAlignment = TextAnchor.UpperCenter;
        hudVLG.childControlWidth = true;
        hudVLG.childControlHeight = true;
        hudVLG.childForceExpandWidth = true;
        hudVLG.childForceExpandHeight = false;

        UIPanelBuilder.CreateTitle(hudPanel, "车辆HUD");


        hudTexts["HUDCurrentNode"] = CreateHUDLabel(hudPanel, "当前路口", "N/A", font);
        hudTexts["HUDNextNode"] = CreateHUDLabel(hudPanel, "下一路口", "N/A", font);
        hudTexts["HUDSpeed"] = CreateHUDLabel(hudPanel, "车速", "0.0 m/s", font);
        hudTexts["HUDSteering"] = CreateHUDLabel(hudPanel, "转向角", "0.0 deg", font);
        hudTexts["HUDAutoMode"] = CreateHUDLabel(hudPanel, "自动驾驶", "否", font);
        hudTexts["HUDState"] = CreateHUDLabel(hudPanel, "状态", "Idle", font);
        hudTexts["HUDLaneId"] = CreateHUDLabel(hudPanel, "车道ID", "N/A", font);
        hudTexts["HUDYielding"] = CreateHUDLabel(hudPanel, "避让中", "否", font);
        hudTexts["HUDCoords"] = CreateHUDLabel(hudPanel, "坐标", "0,0,0", font);
        hudTexts["HUDTimeScale"] = CreateHUDLabel(hudPanel, "时间倍率", "x1", font);
        hudTexts["HUDFPS"] = CreateHUDLabel(hudPanel, "帧率", "0", font);
        hudTexts["HUDVehicleCount"] = CreateHUDLabel(hudPanel, "车辆数", "0", font);
        hudTexts["HUDRosStatus"] = CreateHUDLabel(hudPanel, "ROS2连接", "OFF", font);
        hudTexts["HUDNodeCount"] = CreateHUDLabel(hudPanel, "路网节点数", "0", font);
        hudTexts["HUDGroundY"] = CreateHUDLabel(hudPanel, "基准高程", "0.00 m", font);

        UIPanelBuilder.CreateTitle(hudPanel, "主车定向导航");
        
        GameObject navRow = new GameObject("NavRow");
        navRow.transform.SetParent(hudPanel.transform, false);
        HorizontalLayoutGroup navHlg = navRow.AddComponent<HorizontalLayoutGroup>();
        navHlg.spacing = 4; navHlg.childControlWidth = true;
        
        InputField xInput = UIPanelBuilder.CreateInputRow(navRow, "TargetX", "X:", "0", InputField.ContentType.DecimalNumber).GetComponentInChildren<InputField>();
        InputField zInput = UIPanelBuilder.CreateInputRow(navRow, "TargetZ", "Z:", "0", InputField.ContentType.DecimalNumber).GetComponentInChildren<InputField>();
        
        GameObject goBtnObj = UIPanelBuilder.CreateButton(navRow, "GoBtn", "前往");
        goBtnObj.GetComponent<Button>().onClick.AddListener(() => {
            if (autoDrive != null && float.TryParse(xInput.text, out float tx) && float.TryParse(zInput.text, out float tz))
            {
                carController.ChangeRole(false); // 确保是主车
                carController.autoMode = true;   // 强制开启自动驾驶
                autoDrive.SetDestination(new Vector3(tx, 0, tz)); // 下发目标坐标
                AppendThoughtLine($"主车自动驾驶激活，目标坐标: ({tx}, {tz})");
            }
        });
    }

    TextMeshProUGUI CreateHUDLabel(GameObject parent, string label, string defaultValue, TMP_FontAsset font)
    {
        GameObject row = new GameObject("HUD_" + label);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 20;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 8;

        GameObject lGO = new GameObject("Label");
        lGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI lTxt = lGO.AddComponent<TextMeshProUGUI>();
        lTxt.text = label + ":";
        lTxt.font = font;
        lTxt.fontSize = 11;
        lTxt.enableAutoSizing = true;
        lTxt.fontSizeMin = 7;
        lTxt.fontSizeMax = 11;
        lTxt.color = new Color(0.7f, 0.7f, 0.75f);
        lTxt.alignment = TextAlignmentOptions.Left;
        lGO.AddComponent<LayoutElement>().minWidth = 65;

        GameObject vGO = new GameObject("Value");
        vGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI vTxt = vGO.AddComponent<TextMeshProUGUI>();
        vTxt.text = defaultValue;
        vTxt.font = font;
        vTxt.fontSize = 12;
        vTxt.enableAutoSizing = true;
        vTxt.fontSizeMin = 7;
        vTxt.fontSizeMax = 12;
        vTxt.fontStyle = FontStyles.Bold;
        vTxt.color = new Color(0.2f, 0.9f, 0.5f);
        vTxt.alignment = TextAlignmentOptions.Left;
        vGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return vTxt;
    }

    void BuildKeyPanel(GameObject parent, TMP_FontAsset font)
    {
        keyPanel = new GameObject("KeyPanel");
        keyPanel.transform.SetParent(parent.transform, false);
        keyPanel.AddComponent<LayoutElement>().minHeight = 160;
        Image kpBg = keyPanel.AddComponent<Image>();
        kpBg.color = new Color(0.06f, 0.08f, 0.16f, 0.6f);

        VerticalLayoutGroup kpVLG = keyPanel.AddComponent<VerticalLayoutGroup>();
        kpVLG.padding = new RectOffset(8, 8, 8, 8);
        kpVLG.spacing = 2;
        kpVLG.childAlignment = TextAnchor.UpperCenter;
        kpVLG.childControlWidth = true;
        kpVLG.childControlHeight = true;
        kpVLG.childForceExpandWidth = true;
        kpVLG.childForceExpandHeight = false;

        UIPanelBuilder.CreateTitle(keyPanel, "快捷键");

        keyTexts["W"] = CreateKeyDisplay(keyPanel, "W / Up", "前进", font);
        keyTexts["S"] = CreateKeyDisplay(keyPanel, "S / Down", "后退", font);
        keyTexts["A"] = CreateKeyDisplay(keyPanel, "A", "左转", font);
        keyTexts["D"] = CreateKeyDisplay(keyPanel, "D", "右转", font);
        keyTexts["N"] = CreateKeyDisplay(keyPanel, "N", "重新导航", font);
        keyTexts["R"] = CreateKeyDisplay(keyPanel, "R", "重置位置", font);
        keyTexts["T"] = CreateKeyDisplay(keyPanel, "T", "切换自动与否", font);
        keyTexts["Space"] = CreateKeyDisplay(keyPanel, "刹车","控制", font);
    }

    TextMeshProUGUI CreateKeyDisplay(GameObject parent, string key, string desc, TMP_FontAsset font)
    {
        GameObject row = new GameObject("Key_" + key);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 19;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 8;

        GameObject kGO = new GameObject("KeyLabel");
        kGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI kTxt = kGO.AddComponent<TextMeshProUGUI>();
        kTxt.text = key;
        kTxt.font = font;
        kTxt.fontSize = 12;
        kTxt.enableAutoSizing = true;
        kTxt.fontSizeMin = 7;
        kTxt.fontSizeMax = 12;
        kTxt.fontStyle = FontStyles.Bold;
        kTxt.color = new Color(1f, 0.85f, 0.2f);
        kTxt.alignment = TextAlignmentOptions.Left;
        kGO.AddComponent<LayoutElement>().minWidth = 70;

        GameObject dGO = new GameObject("DescLabel");
        dGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI dTxt = dGO.AddComponent<TextMeshProUGUI>();
        dTxt.text = desc;
        dTxt.font = font;
        dTxt.fontSize = 11;
        dTxt.enableAutoSizing = true;
        dTxt.fontSizeMin = 7;
        dTxt.fontSizeMax = 11;
        dTxt.color = new Color(0.55f, 0.55f, 0.6f);
        dTxt.alignment = TextAlignmentOptions.Left;
        dGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return dTxt;
    }

    void BuildRightPanel(GameObject root, TMP_FontAsset font)
    {
        rightPanel = new GameObject("RightPanel");
        rightPanel.transform.SetParent(root.transform, false);
        RectTransform rpRT = rightPanel.AddComponent<RectTransform>();
        rpRT.anchorMin = new Vector2(0.75f, 0);
        rpRT.anchorMax = new Vector2(1, 0.93f);
        rpRT.offsetMin = new Vector2(4, 4);
        rpRT.offsetMax = new Vector2(-8, 0);

        Image rpBg = rightPanel.AddComponent<Image>();
        rpBg.color = new Color(0.07f, 0.07f, 0.13f, 0.6f);

        ScrollRect scrollRect = rightPanel.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(rightPanel.transform, false);
        RectTransform vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        Image vpImg = viewportGO.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 1);
        Mask vpMask = viewportGO.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;

        rightScrollContent = new GameObject("ScrollContent");
        rightScrollContent.transform.SetParent(viewportGO.transform, false);
        RectTransform scRT = rightScrollContent.AddComponent<RectTransform>();
        scRT.anchorMin = new Vector2(0, 1);
        scRT.anchorMax = new Vector2(1, 1);
        scRT.pivot = new Vector2(0.5f, 1);
        scRT.anchoredPosition = Vector2.zero;
        scRT.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup scVLG = rightScrollContent.AddComponent<VerticalLayoutGroup>();
        scVLG.padding = new RectOffset(16, 16, 12, 12);
        scVLG.spacing = 4;
        scVLG.childAlignment = TextAnchor.UpperCenter;
        scVLG.childControlWidth = true;
        scVLG.childControlHeight = true;
        scVLG.childForceExpandWidth = true;
        scVLG.childForceExpandHeight = false;

        ContentSizeFitter scCSF = rightScrollContent.AddComponent<ContentSizeFitter>();
        scCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRT;
        scrollRect.content = scRT;

        BuildAllModules(rightScrollContent, font);
    }

    void BuildAllModules(GameObject content, TMP_FontAsset font)
    {
        moduleBaseSettings = BuildModule1BaseSettings(content, font);
        moduleTerrain = BuildModule2Terrain(content, font);
        moduleTraffic = BuildModule3Traffic(content, font);
        moduleSystem = BuildModule4System(content, font);
        moduleCamera = BuildModule5Camera(content, font);

        BuildModule6GlobalButtons(content, font);
    }

    void SwitchToModule(GameObject targetModule)
    {
        if (moduleBaseSettings != null) moduleBaseSettings.SetActive(targetModule == moduleBaseSettings);
        if (moduleTerrain != null) moduleTerrain.SetActive(targetModule == moduleTerrain);
        if (moduleTraffic != null) moduleTraffic.SetActive(targetModule == moduleTraffic);
        if (moduleSystem != null) moduleSystem.SetActive(targetModule == moduleSystem);
        if (moduleCamera != null) moduleCamera.SetActive(targetModule == moduleCamera);
    }

    GameObject BuildFoldoutSection(GameObject parent, string title, TMP_FontAsset font)
    {
        GameObject section = new GameObject("Foldout_" + title);
        section.transform.SetParent(parent.transform, false);
        section.AddComponent<LayoutElement>().minHeight = 30;

        VerticalLayoutGroup secVLG = section.AddComponent<VerticalLayoutGroup>();
        secVLG.padding = new RectOffset(0, 0, 0, 0);
        secVLG.spacing = 0;
        secVLG.childAlignment = TextAnchor.UpperCenter;
        secVLG.childControlWidth = true;
        secVLG.childControlHeight = true;
        secVLG.childForceExpandWidth = true;
        secVLG.childForceExpandHeight = false;

        GameObject headerGO = new GameObject("FoldoutHeader");
        headerGO.transform.SetParent(section.transform, false);
        headerGO.AddComponent<LayoutElement>().minHeight = 32;
        Image hdrBg = headerGO.AddComponent<Image>();
        hdrBg.color = new Color(0.12f, 0.18f, 0.28f);
        Button hdrBtn = headerGO.AddComponent<Button>();
        hdrBtn.targetGraphic = hdrBg;

        HorizontalLayoutGroup hdrHLG = headerGO.AddComponent<HorizontalLayoutGroup>();
        hdrHLG.padding = new RectOffset(10, 10, 0, 0);
        hdrHLG.childAlignment = TextAnchor.MiddleLeft;
        hdrHLG.childControlWidth = true;
        hdrHLG.childControlHeight = true;
        hdrHLG.childForceExpandWidth = false;
        hdrHLG.childForceExpandHeight = true;

        GameObject arrowGO = new GameObject("Arrow");
        arrowGO.transform.SetParent(headerGO.transform, false);
        TextMeshProUGUI arrowTxt = arrowGO.AddComponent<TextMeshProUGUI>();
        arrowTxt.text = "v";
        arrowTxt.font = font;
        arrowTxt.fontSize = 14;
        arrowTxt.enableAutoSizing = true;
        arrowTxt.fontSizeMin = 8;
        arrowTxt.fontSizeMax = 14;
        arrowTxt.color = new Color(0.3f, 0.8f, 1f);
        arrowTxt.alignment = TextAlignmentOptions.Center;
        arrowGO.AddComponent<LayoutElement>().minWidth = 20;

        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(headerGO.transform, false);
        TextMeshProUGUI titleTxt = titleGO.AddComponent<TextMeshProUGUI>();
        titleTxt.text = title;
        titleTxt.font = font;
        titleTxt.fontSize = 14;
        titleTxt.enableAutoSizing = true;
        titleTxt.fontSizeMin = 8;
        titleTxt.fontSizeMax = 14;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.color = new Color(0.85f, 0.85f, 0.9f);
        titleTxt.alignment = TextAlignmentOptions.Left;
        titleGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        GameObject contentGO = new GameObject("FoldoutContent");
        contentGO.transform.SetParent(section.transform, false);
        contentGO.AddComponent<LayoutElement>().minHeight = 20;

        VerticalLayoutGroup cntVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        cntVLG.padding = new RectOffset(8, 8, 6, 6);
        cntVLG.spacing = 3;
        cntVLG.childAlignment = TextAnchor.UpperCenter;
        cntVLG.childControlWidth = true;
        cntVLG.childControlHeight = true;
        cntVLG.childForceExpandWidth = true;
        cntVLG.childForceExpandHeight = false;

        ContentSizeFitter cntCSF = contentGO.AddComponent<ContentSizeFitter>();
        cntCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        contentGO.SetActive(true);

        hdrBtn.onClick.AddListener(() =>
        {
            bool newState = !contentGO.activeSelf;
            contentGO.SetActive(newState);
            if (arrowTxt != null) arrowTxt.text = newState ? "v" : ">";
        });

        return contentGO;
    }

    GameObject BuildModule1BaseSettings(GameObject parent, TMP_FontAsset font)
    {
        GameObject module = new GameObject("Module_BaseSettings");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = true;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "基础设置");

        GameObject foldContent = BuildFoldoutSection(module, "路网配置", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "CellSizeInput", "单元格大小", "80", InputField.ContentType.DecimalNumber), "CellSize");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "GridWidthInput", "网格宽度", "5", InputField.ContentType.IntegerNumber), "GridWidth");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "GridHeightInput", "网格高度", "5", InputField.ContentType.IntegerNumber), "GridHeight");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "RandOffsetInput", "随机偏移", "5", InputField.ContentType.DecimalNumber), "RandOffset");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "SeedInput", "Seed", "42", InputField.ContentType.IntegerNumber), "Seed");

        GameObject foldRoad = BuildFoldoutSection(module, "道路网格", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "RoadWidthInput", "道路宽度", "6", InputField.ContentType.DecimalNumber), "RoadWidth");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "MeshResInput", "网格精度", "2", InputField.ContentType.DecimalNumber), "MeshRes");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "HeightOffInput", "高度偏移", "0.15", InputField.ContentType.DecimalNumber), "HeightOff");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "UVScaleInput", "UV缩放", "0.1", InputField.ContentType.DecimalNumber), "UVScale");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "TangentLenInput", "切线长度", "0.3", InputField.ContentType.DecimalNumber), "TangentLen");

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule2Terrain(GameObject parent, TMP_FontAsset font)
    {
        GameObject module = new GameObject("Module_Terrain");
        module.transform.SetParent(parent.transform, false);
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.spacing = 2; mVLG.childControlWidth = true;
        module.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "地形与场景");

        // 【修复】：彻底抛弃Dropdown，使用按钮组
        CreateButtonGroup(module, "CityMode", "生成模式:", new string[] { "城市纯平", "乡村起伏" }, 0, (idx) => {
            bool isCity = (idx == 0);
            if (citySubPanel != null) citySubPanel.SetActive(isCity);
            if (countrysideSubPanel != null) countrysideSubPanel.SetActive(!isCity);
            if (roadGen != null) roadGen.isCountryside = !isCity;
        }, font);

        citySubPanel = BuildFoldoutSection(module, "城市设置", font);
        countrysideSubPanel = BuildFoldoutSection(module, "乡村设置", font);

        RegisterToggle(CreateToggleRow(citySubPanel, "GenCityToggle", "生成建筑", true, font), "GenCity");
        RegisterInputField(UIPanelBuilder.CreateInputRow(citySubPanel, "BldHeightInput", "建筑高度", "10", InputField.ContentType.DecimalNumber), "BldHeight");

        // 【修复】：补齐所有乡村专用参数
        RegisterToggle(CreateToggleRow(countrysideSubPanel, "CountryUniformToggle", "统一材质", true, font), "CountryUniform");
        RegisterInputField(UIPanelBuilder.CreateInputRow(countrysideSubPanel, "CountrysideHeightInput", "高度缩放 (HeightScale)", "15", InputField.ContentType.DecimalNumber), "CountrysideHeightScale");
        RegisterInputField(UIPanelBuilder.CreateInputRow(countrysideSubPanel, "NoiseScaleInput", "噪波频率 (Noise)", "0.05", InputField.ContentType.DecimalNumber), "NoiseScale");

        citySubPanel.SetActive(true);
        countrysideSubPanel.SetActive(false);
        module.SetActive(false);
        return module;
    }

    GameObject BuildModule3Traffic(GameObject parent, TMP_FontAsset font)
    {
        GameObject module = new GameObject("Module_Traffic");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = true;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "交通与NPC");

        GameObject foldContent = BuildFoldoutSection(module, "NPC配置", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCCountInput", "NPC数量", "3", InputField.ContentType.IntegerNumber), "NPCCount");

        GameObject npcModeRow = new GameObject("NPCModeDropdownRow");
        npcModeRow.transform.SetParent(foldContent.transform, false);
        npcModeRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup nhlg = npcModeRow.AddComponent<HorizontalLayoutGroup>();
        nhlg.padding = new RectOffset(4, 4, 0, 0);
        nhlg.spacing = 6;
        nhlg.childAlignment = TextAnchor.MiddleLeft;
        nhlg.childControlWidth = true;
        nhlg.childControlHeight = true;
        nhlg.childForceExpandWidth = false;
        nhlg.childForceExpandHeight = true;

        GameObject nmLabel = new GameObject("Label");
        nmLabel.transform.SetParent(npcModeRow.transform, false);
        TextMeshProUGUI nmLabelTxt = nmLabel.AddComponent<TextMeshProUGUI>();
        nmLabelTxt.text = "驾驶模式:";
        nmLabelTxt.font = font;
        nmLabelTxt.fontSize = 13;
        nmLabelTxt.enableAutoSizing = true;
        nmLabelTxt.fontSizeMin = 8;
        nmLabelTxt.fontSizeMax = 13;
        nmLabelTxt.color = Color.white;
        nmLabelTxt.alignment = TextAlignmentOptions.Left;
        nmLabel.AddComponent<LayoutElement>().minWidth = 90;

        GameObject npcModeDropdownGO = new GameObject("Dropdown");
        npcModeDropdownGO.transform.SetParent(npcModeRow.transform, false);
        npcModeDropdownGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        Dropdown npcModeDropdown = npcModeDropdownGO.AddComponent<Dropdown>();
        npcModeDropdown.options = new List<Dropdown.OptionData>
        {
            new Dropdown.OptionData("MathSpline"),
            new Dropdown.OptionData("Rigidbody")
        };
        npcModeDropdown.value = 0;

        Image nmImg = npcModeDropdownGO.AddComponent<Image>();
        nmImg.color = new Color(0.2f, 0.22f, 0.3f);

        GameObject nmDDLabelGO = new GameObject("Label");
        nmDDLabelGO.transform.SetParent(npcModeDropdownGO.transform, false);
        Text nmDDLabel = nmDDLabelGO.AddComponent<Text>();
        nmDDLabel.text = "MathSpline";
        nmDDLabel.font = legacyFont;
        nmDDLabel.fontSize = 13;
        nmDDLabel.resizeTextForBestFit = true;
        nmDDLabel.resizeTextMinSize = 8;
        nmDDLabel.resizeTextMaxSize = 13;
        nmDDLabel.color = Color.white;
        nmDDLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform nmDDRT = nmDDLabelGO.GetComponent<RectTransform>();
        nmDDRT.anchorMin = Vector2.zero;
        nmDDRT.anchorMax = Vector2.one;
        nmDDRT.offsetMin = new Vector2(8, 0);
        nmDDRT.offsetMax = new Vector2(-20, 0);
        npcModeDropdown.captionText = nmDDLabel;

        GameObject nmItemLabelGO = new GameObject("ItemLabel");
        nmItemLabelGO.transform.SetParent(npcModeDropdownGO.transform, false);
        Text nmItemLabel = nmItemLabelGO.AddComponent<Text>();
        nmItemLabel.text = "";
        nmItemLabel.font = legacyFont;
        nmItemLabel.fontSize = 13;
        nmItemLabel.resizeTextForBestFit = true;
        nmItemLabel.resizeTextMinSize = 8;
        nmItemLabel.resizeTextMaxSize = 13;
        nmItemLabel.color = Color.black;
        nmItemLabel.alignment = TextAnchor.MiddleLeft;
        npcModeDropdown.itemText = nmItemLabel;

        dropdowns["NPCMode"] = npcModeDropdown;
        SetupDropdownTemplate(npcModeDropdown, legacyFont);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCMaxSpeedInput", "NPC Max Speed", "30", InputField.ContentType.DecimalNumber), "NPCMaxSpeed");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCSafeDistInput", "Safe Distance", "8", InputField.ContentType.DecimalNumber), "NPCSafeDist");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCLookAheadInput", "Look Ahead T", "0.02", InputField.ContentType.DecimalNumber), "NPCLookAhead");

        GameObject spawnBtn = UIPanelBuilder.CreateButton(foldContent, "SpawnNPCsBtn", "生成NPC");
        Button spawnButton = spawnBtn.GetComponent<Button>();
        if (spawnButton != null)
        {
            spawnButton.onClick.AddListener(() =>
            {
                if (trafficManager != null)
                {
                    trafficManager.ResetSpawnState();
                    trafficManager.SpawnNPCs();
                }
            });
        }

        GameObject emergencyBtn = UIPanelBuilder.CreateButton(foldContent, "EmergencyBtn", "召唤紧急车辆");
        Button embButton = emergencyBtn.GetComponent<Button>();
        if (embButton != null)
        {
            Image embImg = emergencyBtn.GetComponent<Image>();
            if (embImg != null) embImg.color = new Color(0.85f, 0.15f, 0.15f);
            embButton.onClick.AddListener(() =>
            {
                if (trafficManager != null)
                {
                    Camera cam = Camera.main;
                    Vector3 spawnPos = cam != null ? cam.transform.position + cam.transform.forward * 15f : Vector3.zero;
                    trafficManager.SpawnEmergencyVehicle(spawnPos);
                }
            });
        }

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule4System(GameObject parent, TMP_FontAsset font)
    {
        GameObject module = new GameObject("Module_System");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = true;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "系统与调试");

        GameObject foldContent = BuildFoldoutSection(module, "系统设置", font);

        RegisterToggle(CreateToggleRow(foldContent, "ROS2Toggle", "ROS2桥接", false, font), "ROS2Bridge");
        RegisterToggle(CreateToggleRow(foldContent, "SplineGizmoToggle", "射线/样条可视化", false, font), "SplineGizmos");
        RegisterToggle(CreateToggleRow(foldContent, "MinimalModeToggle", "极简UI模式", false, font), "MinimalMode");

        UIPanelBuilder.CreateSectionHeader(foldContent, "--- 红绿灯状态 ---");
        GameObject tlStatusRow = UIPanelBuilder.CreateDebugRow(foldContent, "TLStatus", "红绿灯状态", "N/A");
        TextMeshProUGUI tlStatusTxt = tlStatusRow != null ? tlStatusRow.GetComponentInChildren<TextMeshProUGUI>() : null;
        hudTexts["TLStatus"] = tlStatusTxt;

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule5Camera(GameObject parent, TMP_FontAsset font)
    {
        GameObject module = new GameObject("Module_Camera");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = true;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "相机与时间");

        GameObject foldContent = BuildFoldoutSection(module, "设置", font);

        GameObject camModeRow = new GameObject("CamModeDropdownRow");
        camModeRow.transform.SetParent(foldContent.transform, false);
        camModeRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup chlg = camModeRow.AddComponent<HorizontalLayoutGroup>();
        chlg.padding = new RectOffset(4, 4, 0, 0);
        chlg.spacing = 6;
        chlg.childAlignment = TextAnchor.MiddleLeft;
        chlg.childControlWidth = true;
        chlg.childControlHeight = true;
        chlg.childForceExpandWidth = false;
        chlg.childForceExpandHeight = true;

        GameObject cmLabel = new GameObject("Label");
        cmLabel.transform.SetParent(camModeRow.transform, false);
        TextMeshProUGUI cmLabelTxt = cmLabel.AddComponent<TextMeshProUGUI>();
        cmLabelTxt.text = "相机模式:";
        cmLabelTxt.font = font;
        cmLabelTxt.fontSize = 13;
        cmLabelTxt.enableAutoSizing = true;
        cmLabelTxt.fontSizeMin = 8;
        cmLabelTxt.fontSizeMax = 13;
        cmLabelTxt.color = Color.white;
        cmLabelTxt.alignment = TextAlignmentOptions.Left;
        cmLabel.AddComponent<LayoutElement>().minWidth = 80;

        GameObject camModeDropdownGO = new GameObject("Dropdown");
        camModeDropdownGO.transform.SetParent(camModeRow.transform, false);
        camModeDropdownGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        Dropdown camModeDropdown = camModeDropdownGO.AddComponent<Dropdown>();
        camModeDropdown.options = new List<Dropdown.OptionData>
        {
            new Dropdown.OptionData("Follow"),
            new Dropdown.OptionData("FreeFly")
        };
        camModeDropdown.value = 0;

        Image cmImg = camModeDropdownGO.AddComponent<Image>();
        cmImg.color = new Color(0.2f, 0.22f, 0.3f);

        GameObject cmDDLabelGO = new GameObject("Label");
        cmDDLabelGO.transform.SetParent(camModeDropdownGO.transform, false);
        Text cmDDLabel = cmDDLabelGO.AddComponent<Text>();
        cmDDLabel.text = "Follow";
        cmDDLabel.font = legacyFont;
        cmDDLabel.fontSize = 13;
        cmDDLabel.resizeTextForBestFit = true;
        cmDDLabel.resizeTextMinSize = 8;
        cmDDLabel.resizeTextMaxSize = 13;
        cmDDLabel.color = Color.white;
        cmDDLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform cmDDRT = cmDDLabelGO.GetComponent<RectTransform>();
        cmDDRT.anchorMin = Vector2.zero;
        cmDDRT.anchorMax = Vector2.one;
        cmDDRT.offsetMin = new Vector2(8, 0);
        cmDDRT.offsetMax = new Vector2(-20, 0);
        camModeDropdown.captionText = cmDDLabel;

        GameObject cmItemLabelGO = new GameObject("ItemLabel");
        cmItemLabelGO.transform.SetParent(camModeDropdownGO.transform, false);
        Text cmItemLabel = cmItemLabelGO.AddComponent<Text>();
        cmItemLabel.text = "";
        cmItemLabel.font = legacyFont;
        cmItemLabel.fontSize = 13;
        cmItemLabel.resizeTextForBestFit = true;
        cmItemLabel.resizeTextMinSize = 8;
        cmItemLabel.resizeTextMaxSize = 13;
        cmItemLabel.color = Color.black;
        cmItemLabel.alignment = TextAnchor.MiddleLeft;
        camModeDropdown.itemText = cmItemLabel;

        dropdowns["CamMode"] = camModeDropdown;
        SetupDropdownTemplate(camModeDropdown, legacyFont);

        GameObject timeModeRow = new GameObject("TimeModeDropdownRow");
        timeModeRow.transform.SetParent(foldContent.transform, false);
        timeModeRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup thlg = timeModeRow.AddComponent<HorizontalLayoutGroup>();
        thlg.padding = new RectOffset(4, 4, 0, 0);
        thlg.spacing = 6;
        thlg.childAlignment = TextAnchor.MiddleLeft;
        thlg.childControlWidth = true;
        thlg.childControlHeight = true;
        thlg.childForceExpandWidth = false;
        thlg.childForceExpandHeight = true;

        GameObject tmLabel = new GameObject("Label");
        tmLabel.transform.SetParent(timeModeRow.transform, false);
        TextMeshProUGUI tmLabelTxt = tmLabel.AddComponent<TextMeshProUGUI>();
        tmLabelTxt.text = "Time Scale:";
        tmLabelTxt.font = font;
        tmLabelTxt.fontSize = 13;
        tmLabelTxt.enableAutoSizing = true;
        tmLabelTxt.fontSizeMin = 8;
        tmLabelTxt.fontSizeMax = 13;
        tmLabelTxt.color = Color.white;
        tmLabelTxt.alignment = TextAlignmentOptions.Left;
        tmLabel.AddComponent<LayoutElement>().minWidth = 80;

        GameObject timeModeDropdownGO = new GameObject("Dropdown");
        timeModeDropdownGO.transform.SetParent(timeModeRow.transform, false);
        timeModeDropdownGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        Dropdown timeModeDropdown = timeModeDropdownGO.AddComponent<Dropdown>();
        timeModeDropdown.options = new List<Dropdown.OptionData>
        {
            new Dropdown.OptionData("Pause (0x)"),
            new Dropdown.OptionData("Normal (1x)"),
            new Dropdown.OptionData("Fast (2x)"),
            new Dropdown.OptionData("Turbo (5x)")
        };
        timeModeDropdown.value = 1;

        Image tmImg = timeModeDropdownGO.AddComponent<Image>();
        tmImg.color = new Color(0.2f, 0.22f, 0.3f);

        GameObject tmDDLabelGO = new GameObject("Label");
        tmDDLabelGO.transform.SetParent(timeModeDropdownGO.transform, false);
        Text tmDDLabel = tmDDLabelGO.AddComponent<Text>();
        tmDDLabel.text = "Normal (1x)";
        tmDDLabel.font = legacyFont;
        tmDDLabel.fontSize = 13;
        tmDDLabel.resizeTextForBestFit = true;
        tmDDLabel.resizeTextMinSize = 8;
        tmDDLabel.resizeTextMaxSize = 13;
        tmDDLabel.color = Color.white;
        tmDDLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform tmDDRT = tmDDLabelGO.GetComponent<RectTransform>();
        tmDDRT.anchorMin = Vector2.zero;
        tmDDRT.anchorMax = Vector2.one;
        tmDDRT.offsetMin = new Vector2(8, 0);
        tmDDRT.offsetMax = new Vector2(-20, 0);
        timeModeDropdown.captionText = tmDDLabel;

        GameObject tmItemLabelGO = new GameObject("ItemLabel");
        tmItemLabelGO.transform.SetParent(timeModeDropdownGO.transform, false);
        Text tmItemLabel = tmItemLabelGO.AddComponent<Text>();
        tmItemLabel.text = "";
        tmItemLabel.font = legacyFont;
        tmItemLabel.fontSize = 13;
        tmItemLabel.resizeTextForBestFit = true;
        tmItemLabel.resizeTextMinSize = 8;
        tmItemLabel.resizeTextMaxSize = 13;
        tmItemLabel.color = Color.black;
        tmItemLabel.alignment = TextAnchor.MiddleLeft;
        timeModeDropdown.itemText = tmItemLabel;

        dropdowns["TimeMode"] = timeModeDropdown;
        SetupDropdownTemplate(timeModeDropdown, legacyFont);

        timeModeDropdown.onValueChanged.AddListener((idx) =>
        {
            Time.timeScale = idx switch { 0 => 0f, 1 => 1f, 2 => 2f, 3 => 5f, _ => 1f };
        });

        camModeDropdown.onValueChanged.AddListener((idx) =>
        {
            if (cameraController != null)
            {
                cameraController.currentMode = idx == 0 ? CameraController.CameraMode.Follow : CameraController.CameraMode.FreeFly;
            }
        });

        GameObject deleteNearestBtn = UIPanelBuilder.CreateButton(foldContent, "DeleteNearestNPCBtn", "删除最近NPC");
        Button dnButton = deleteNearestBtn.GetComponent<Button>();
        if (dnButton != null)
        {
            Image dnImg = deleteNearestBtn.GetComponent<Image>();
            if (dnImg != null) dnImg.color = new Color(0.8f, 0.5f, 0.1f);
            dnButton.onClick.AddListener(() =>
            {
                if (trafficManager != null)
                {
                    Camera cam = Camera.main;
                    Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
                    trafficManager.DeleteNearestNPC(pos);
                }
            });
        }

        GameObject clearNPCsBtn = UIPanelBuilder.CreateButton(foldContent, "ClearNPCsBtn", "清除全部NPC");
        Button cnButton = clearNPCsBtn.GetComponent<Button>();
        if (cnButton != null)
        {
            Image cnImg = clearNPCsBtn.GetComponent<Image>();
            if (cnImg != null) cnImg.color = new Color(0.8f, 0.2f, 0.2f);
            cnButton.onClick.AddListener(() =>
            {
                if (trafficManager != null) trafficManager.ClearAllNPCs();
            });
        }

        module.SetActive(false);
        return module;
    }

    void BuildModule6GlobalButtons(GameObject parent, TMP_FontAsset font)
    {
        GameObject btnSection = new GameObject("Module_GlobalBtns");
        btnSection.transform.SetParent(parent.transform, false);
        btnSection.AddComponent<LayoutElement>().minHeight = 60;

        VerticalLayoutGroup bvl = btnSection.AddComponent<VerticalLayoutGroup>();
        bvl.padding = new RectOffset(8, 8, 8, 8);
        bvl.spacing = 6;
        bvl.childAlignment = TextAnchor.UpperCenter;
        bvl.childControlWidth = true;
        bvl.childControlHeight = true;
        bvl.childForceExpandWidth = true;
        bvl.childForceExpandHeight = false;

        GameObject applyBtn = UIPanelBuilder.CreateButton(btnSection, "ApplyRegenBtn", "应用配置并重新生成");
        Button applyButton = applyBtn.GetComponent<Button>();
        if (applyButton != null)
        {
            Image appImg = applyButton.GetComponent<Image>();
            if (appImg != null) appImg.color = new Color(0.7f, 0.2f, 0.2f);
            applyButton.onClick.AddListener(() =>
            {
                ApplyAllConfigToComponents();
                if (WorldModel.Instance != null)
                {
                    WorldModel.Instance.TriggerWorldGeneration();
                }
            });
        }

        GameObject resetBtn = UIPanelBuilder.CreateButton(btnSection, "ResetDefaultsBtn", "恢复默认设置");
        Button resetButton = resetBtn.GetComponent<Button>();
        if (resetButton != null)
        {
            resetButton.onClick.AddListener(() =>
            {
                ResetAllDefaults();
            });
        }
    }

    #endregion

    #region UI Helper Methods

    void SetupDropdownTemplate(Dropdown dropdown, Font font)
    {
        GameObject templateGO = new GameObject("Template");
        templateGO.transform.SetParent(dropdown.transform, false);
        RectTransform templateRT = templateGO.AddComponent<RectTransform>();
        templateRT.anchorMin = new Vector2(0, 0);
        templateRT.anchorMax = new Vector2(1, 0);
        templateRT.pivot = new Vector2(0.5f, 1);
        templateRT.sizeDelta = new Vector2(0, 150);

        Image templateImg = templateGO.AddComponent<Image>();
        templateImg.color = new Color(0.12f, 0.13f, 0.18f);

        ScrollRect scrollRect = templateGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(templateGO.transform, false);
        RectTransform vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.sizeDelta = Vector2.zero;
        Image vpImg = viewportGO.AddComponent<Image>();
        vpImg.color = new Color(0.12f, 0.13f, 0.18f);
        Mask vpMask = viewportGO.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;

        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        RectTransform cntRT = contentGO.AddComponent<RectTransform>();
        cntRT.anchorMin = new Vector2(0, 1);
        cntRT.anchorMax = Vector2.one;
        cntRT.pivot = new Vector2(0.5f, 1);
        cntRT.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup cntVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        cntVLG.childControlWidth = true;
        cntVLG.childControlHeight = true;
        cntVLG.childForceExpandWidth = true;
        cntVLG.childForceExpandHeight = false;

        ContentSizeFitter cntCSF = contentGO.AddComponent<ContentSizeFitter>();
        cntCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject itemGO = new GameObject("Item");
        itemGO.transform.SetParent(contentGO.transform, false);
        RectTransform itemRT = itemGO.AddComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 0.5f);
        itemRT.anchorMax = new Vector2(1, 0.5f);
        itemRT.sizeDelta = new Vector2(0, 24);

        Toggle itemToggle = itemGO.AddComponent<Toggle>();

        GameObject itemBgGO = new GameObject("Item Background");
        itemBgGO.transform.SetParent(itemGO.transform, false);
        Image itemBgImg = itemBgGO.AddComponent<Image>();
        itemBgImg.color = new Color(0.25f, 0.27f, 0.35f);
        RectTransform ibRT = itemBgGO.GetComponent<RectTransform>();
        ibRT.anchorMin = Vector2.zero;
        ibRT.anchorMax = Vector2.one;
        ibRT.sizeDelta = Vector2.zero;
        itemToggle.targetGraphic = itemBgImg;

        GameObject checkmarkGO = new GameObject("Item Checkmark");
        checkmarkGO.transform.SetParent(itemGO.transform, false);
        Image cmImg = checkmarkGO.AddComponent<Image>();
        cmImg.color = new Color(0.3f, 0.8f, 1f);
        RectTransform cmRT = checkmarkGO.GetComponent<RectTransform>();
        cmRT.anchorMin = new Vector2(0, 0);
        cmRT.anchorMax = new Vector2(0, 1);
        cmRT.sizeDelta = new Vector2(20, 0);
        itemToggle.graphic = cmImg;

        GameObject itemLabelGO = new GameObject("Item Label");
        itemLabelGO.transform.SetParent(itemGO.transform, false);
        Text itemLabel = itemLabelGO.AddComponent<Text>();
        itemLabel.font = legacyFont;
        itemLabel.fontSize = 13;
        itemLabel.resizeTextForBestFit = true;
        itemLabel.resizeTextMinSize = 8;
        itemLabel.resizeTextMaxSize = 13;
        itemLabel.color = Color.white;
        itemLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform ilRT = itemLabelGO.GetComponent<RectTransform>();
        ilRT.anchorMin = Vector2.zero;
        ilRT.anchorMax = Vector2.one;
        ilRT.offsetMin = new Vector2(26, 0);
        ilRT.offsetMax = new Vector2(-8, 0);

        dropdown.itemText = itemLabel;

        scrollRect.viewport = vpRT;
        scrollRect.content = cntRT;

        dropdown.template = templateRT;
    }

    GameObject CreateToggleRow(GameObject parent, string name, string label, bool defaultValue, TMP_FontAsset font)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 32;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 8;
        hlg.padding = new RectOffset(4, 4, 0, 0);

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.font = font;
        labelTxt.fontSize = 13;
        labelTxt.enableAutoSizing = true;
        labelTxt.fontSizeMin = 8;
        labelTxt.fontSizeMax = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelGO.AddComponent<LayoutElement>().minWidth = 140;

        GameObject toggleGO = new GameObject("Toggle");
        toggleGO.transform.SetParent(row.transform, false);
        toggleGO.AddComponent<LayoutElement>().minWidth = 36;
        toggleGO.AddComponent<LayoutElement>().minHeight = 24;
        Toggle toggle = toggleGO.AddComponent<Toggle>();
        toggle.isOn = defaultValue;

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(toggleGO.transform, false);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.25f, 0.25f, 0.35f);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        toggle.targetGraphic = bgImg;

        GameObject checkGO = new GameObject("Checkmark");
        checkGO.transform.SetParent(bgGO.transform, false);
        Image checkImg = checkGO.AddComponent<Image>();
        checkImg.color = new Color(0.3f, 0.8f, 1f);
        RectTransform checkRT = checkGO.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0.1f, 0.1f);
        checkRT.anchorMax = new Vector2(0.9f, 0.9f);
        checkRT.sizeDelta = Vector2.zero;
        toggle.graphic = checkImg;

        return row;
    }

    void RegisterInputField(GameObject row, string key)
    {
        if (row == null) return;
        InputField input = row.GetComponentInChildren<InputField>();
        if (input != null)
        {
            inputFields[key] = input;
        }
    }

    void RegisterToggle(GameObject row, string key)
    {
        if (row == null) return;
        Toggle toggle = row.GetComponentInChildren<Toggle>();
        if (toggle != null)
        {
            toggles[key] = toggle;
        }
    }

    void CreateButtonGroup(GameObject parent, string groupName, string label, string[] options, int defaultIndex,
        System.Action<int> onSelected, TMP_FontAsset font)
    {
        GameObject row = new GameObject(groupName + "_Row");
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        GameObject lblGO = new GameObject("Label");
        lblGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI lblTxt = lblGO.AddComponent<TextMeshProUGUI>();
        lblTxt.text = label;
        lblTxt.font = font;
        lblTxt.fontSize = 13;
        lblTxt.enableAutoSizing = true;
        lblTxt.fontSizeMin = 8;
        lblTxt.fontSizeMax = 13;
        lblTxt.color = Color.white;
        lblTxt.alignment = TextAlignmentOptions.Left;
        lblGO.AddComponent<LayoutElement>().minWidth = 70;

        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            GameObject btnGO = UIPanelBuilder.CreateButton(row, groupName + "_Btn" + i, options[i]);
            btnGO.AddComponent<LayoutElement>().minWidth = 80;
            Button btn = btnGO.GetComponent<Button>();
            Image btnImg = btnGO.GetComponent<Image>();
            if (idx == defaultIndex)
                btnImg.color = new Color(0.25f, 0.55f, 0.85f);
            else
                btnImg.color = new Color(0.2f, 0.22f, 0.3f);

            btn.onClick.AddListener(() =>
            {
                onSelected?.Invoke(idx);
                // 高亮选中按钮
                foreach (Transform child in row.transform)
                {
                    Button childBtn = child.GetComponent<Button>();
                    if (childBtn != null)
                    {
                        Image childImg = child.GetComponent<Image>();
                        if (childImg != null)
                            childImg.color = (child.name == groupName + "_Btn" + idx)
                                ? new Color(0.25f, 0.55f, 0.85f)
                                : new Color(0.2f, 0.22f, 0.3f);
                    }
                }
            });
        }
    }

    #endregion

    #region Sync & Bind

    void SyncAllUIFromComponents()
    {
        SyncInputFieldValue("CellSize", roadGen != null ? roadGen.cellSize.ToString("F0") : "80");
        SyncInputFieldValue("GridWidth", roadGen != null ? roadGen.gridWidth.ToString() : "5");
        SyncInputFieldValue("GridHeight", roadGen != null ? roadGen.gridHeight.ToString() : "5");
        SyncInputFieldValue("RandOffset", roadGen != null ? roadGen.randomOffset.ToString("F0") : "5");
        SyncInputFieldValue("Seed", roadGen != null ? roadGen.seed.ToString() : "42");

        SyncInputFieldValue("RoadWidth", roadBuilder != null ? roadBuilder.roadWidth.ToString("F0") : "6");
        SyncInputFieldValue("MeshRes", roadBuilder != null ? roadBuilder.meshResolution.ToString("F0") : "2");
        SyncInputFieldValue("HeightOff", roadBuilder != null ? roadBuilder.roadHeightOffset.ToString("F2") : "0.15");
        SyncInputFieldValue("UVScale", roadBuilder != null ? roadBuilder.uvScale.ToString("F2") : "0.1");
        SyncInputFieldValue("TangentLen", roadBuilder != null ? roadBuilder.tangentLength.ToString("F2") : "0.3");

        SyncToggleValue("GenCity", roadBuilder != null ? roadBuilder.generateCity : true);
        SyncInputFieldValue("BldHeight", roadBuilder != null ? roadBuilder.buildingHeight.ToString("F0") : "10");
        SyncInputFieldValue("Sidewalk", roadBuilder != null ? roadBuilder.sidewalkWidth.ToString("F0") : "2");
        SyncToggleValue("TrafficLights", trafficLightManager != null ? trafficLightManager.isActiveAndEnabled : true);
        SyncInputFieldValue("TLChance", trafficLightManager != null ? trafficLightManager.placementChance.ToString("F2") : "0.1");
        SyncToggleValue("CountryUniform", roadBuilder != null ? roadBuilder.useCountrysideUniformMaterials : true);

        SyncInputFieldValue("NPCCount", trafficManager != null ? trafficManager.npcCount.ToString() : "3");
        SyncInputFieldValue("NPCMaxSpeed", carController != null ? carController.maxSpeed.ToString("F0") : "30");
        SyncInputFieldValue("NPCSafeDist", autoDrive != null ? autoDrive.safeDistance.ToString("F0") : "8");
        SyncInputFieldValue("NPCLookAhead", autoDrive != null ? autoDrive.lookAheadT.ToString("F3") : "0.020");

        SyncToggleValue("SplineGizmos", roadBuilder != null ? roadBuilder.showSplineGizmos : false);

        bool ros2Active = ros2Bridge != null && ros2Bridge.isActiveAndEnabled;
        SyncToggleValue("ROS2Bridge", ros2Active);

        SyncDropdownValue("CityMode", (roadGen != null && roadGen.isCountryside) ? 1 : 0);
        SyncDropdownValue("CamMode", cameraController != null ? (cameraController.currentMode == CameraController.CameraMode.Follow ? 0 : 1) : 0);
        SyncDropdownValue("TimeMode", Mathf.Approximately(Time.timeScale, 0f) ? 0 : (Mathf.Approximately(Time.timeScale, 2f) ? 2 : (Mathf.Approximately(Time.timeScale, 5f) ? 3 : 1)));

        if (citySubPanel != null) citySubPanel.SetActive((roadGen == null) || !roadGen.isCountryside);
        if (countrysideSubPanel != null) countrysideSubPanel.SetActive(roadGen != null && roadGen.isCountryside);
    }

    void SyncInputFieldValue(string key, string value)
    {
        if (inputFields.TryGetValue(key, out InputField input) && input != null)
        {
            input.text = value;
        }
    }

    void SyncToggleValue(string key, bool value)
    {
        if (toggles.TryGetValue(key, out Toggle toggle) && toggle != null)
        {
            toggle.isOn = value;
        }
    }

    void SyncDropdownValue(string key, int value)
    {
        if (dropdowns.TryGetValue(key, out Dropdown dropdown) && dropdown != null)
        {
            if (value >= 0 && value < dropdown.options.Count)
            {
                dropdown.value = value;
            }
        }
    }

    void BindAllUIEvents()
    {
        BindInputFieldEvent("CellSize", (v) => { if (roadGen != null && float.TryParse(v, out float f)) roadGen.cellSize = f; });
        BindInputFieldEvent("GridWidth", (v) => { if (roadGen != null && int.TryParse(v, out int i)) roadGen.gridWidth = i; });
        BindInputFieldEvent("GridHeight", (v) => { if (roadGen != null && int.TryParse(v, out int i)) roadGen.gridHeight = i; });
        BindInputFieldEvent("RandOffset", (v) => { if (roadGen != null && float.TryParse(v, out float f)) roadGen.randomOffset = f; });
        BindInputFieldEvent("Seed", (v) => { if (roadGen != null && int.TryParse(v, out int i)) roadGen.seed = i; });

        BindInputFieldEvent("RoadWidth", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.roadWidth = f; });
        BindInputFieldEvent("MeshRes", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.meshResolution = f; });
        BindInputFieldEvent("HeightOff", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.roadHeightOffset = f; });
        BindInputFieldEvent("UVScale", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.uvScale = f; });
        BindInputFieldEvent("TangentLen", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.tangentLength = f; });

        BindInputFieldEvent("BldHeight", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) { roadBuilder.buildingHeight = f; TryAutoRegenTerrain(); } });
        BindInputFieldEvent("Sidewalk", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) { roadBuilder.sidewalkWidth = f; TryAutoRegenTerrain(); } });
        BindInputFieldEvent("TLChance", (v) => { if (trafficLightManager != null && float.TryParse(v, out float f)) trafficLightManager.placementChance = f; });

        BindInputFieldEvent("CountrysideHeightScale", (v) => { if (roadGen != null && float.TryParse(v, out float f)) { roadGen.countrysideHeightScale = f; TryAutoRegenTerrain(); } });
        BindInputFieldEvent("NoiseScale", (v) => { if (roadGen != null && float.TryParse(v, out float f)) { /* noiseFrequency 设置 */ TryAutoRegenTerrain(); } });

        BindInputFieldEvent("NPCCount", (v) => { if (trafficManager != null && int.TryParse(v, out int i)) trafficManager.npcCount = i; });
        BindInputFieldEvent("NPCMaxSpeed", (v) =>
        {
            if (float.TryParse(v, out float f) && trafficManager != null)
            {
                foreach (var npc in trafficManager.ActiveNPCs)
                {
                    if (npc == null) continue;
                    var ctrl = npc.GetComponent<SimpleCarController>();
                    if (ctrl != null) ctrl.maxSpeed = f;
                }
            }
        });
        BindInputFieldEvent("NPCSafeDist", (v) =>
        {
            if (float.TryParse(v, out float f) && trafficManager != null)
            {
                foreach (var npc in trafficManager.ActiveNPCs)
                {
                    if (npc != null) npc.safeDistance = f;
                }
            }
        });
        BindInputFieldEvent("NPCLookAhead", (v) =>
        {
            if (float.TryParse(v, out float f) && trafficManager != null)
            {
                foreach (var npc in trafficManager.ActiveNPCs)
                {
                    if (npc != null) npc.lookAheadT = f;
                }
            }
        });

        BindToggleEvent("GenCity", (on) => { if (roadBuilder != null) { roadBuilder.generateCity = on; TryAutoRegenTerrain(); } });
        BindToggleEvent("TrafficLights", (on) =>
        {
            if (trafficLightManager != null) trafficLightManager.enabled = on;
        });
        BindToggleEvent("CountryUniform", (on) => { if (roadBuilder != null) { roadBuilder.useCountrysideUniformMaterials = on; TryAutoRegenTerrain(); } });
        BindToggleEvent("SplineGizmos", (on) => { if (roadBuilder != null) roadBuilder.showSplineGizmos = on; });
        BindToggleEvent("ROS2Bridge", (on) =>
        {
            if (ros2Bridge != null)
            {
                if (on)
                    ros2Bridge.Reconnect();
                else
                    ros2Bridge.Disconnect();
            }
        });
        BindToggleEvent("MinimalMode", (on) =>
        {
            isMinimalMode = on;
            if (rightPanel != null) rightPanel.SetActive(!on);
            DebugPanel dbg = FindObjectOfType<DebugPanel>();
            if (dbg != null)
            {
                Canvas dbgCanvas = dbg.GetComponent<Canvas>();
                if (dbgCanvas != null) dbgCanvas.enabled = !on;
            }
        });

        if (dropdowns.TryGetValue("CityMode", out Dropdown cityDD))
        {
            cityDD.onValueChanged.AddListener((idx) =>
            {
                try
                {
                    if (roadGen == null) roadGen = FindObjectOfType<RoadNetworkGenerator>();
                    if (roadBuilder == null) roadBuilder = FindObjectOfType<ProceduralRoadBuilder>();
                    if (trafficManager == null) trafficManager = FindObjectOfType<TrafficManager>();

                    if (roadGen != null)
                    {
                        roadGen.isCountryside = (idx == 1);
                        if (trafficManager != null) trafficManager.ClearAllNPCs();
                        roadGen.Generate();
                        if (roadBuilder != null) roadBuilder.BuildRoads();
                    }
                    if (citySubPanel != null) citySubPanel.SetActive(idx == 0);
                    if (countrysideSubPanel != null) countrysideSubPanel.SetActive(idx == 1);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[MasterUIManager] City mode switch failed: {ex.Message}");
                }
            });
        }

        if (dropdowns.TryGetValue("NPCMode", out Dropdown npcDD))
        {
            npcDD.onValueChanged.AddListener((idx) =>
            {
                if (trafficManager == null) return;
                foreach (var npc in trafficManager.ActiveNPCs)
                {
                    if (npc == null) continue;
                    var ctrl = npc.GetComponent<SimpleCarController>();
                    if (ctrl == null) continue;
                    var rb = npc.GetComponent<Rigidbody>();
                    if (idx == 0)
                    {
                        ctrl.isNPC = true;
                        if (rb != null) rb.isKinematic = true;
                    }
                    else
                    {
                        ctrl.isNPC = false;
                        if (rb != null) rb.isKinematic = false;
                    }
                }
            });
        }
    }

    void BindInputFieldEvent(string key, UnityEngine.Events.UnityAction<string> callback)
    {
        if (inputFields.TryGetValue(key, out InputField input) && input != null)
        {
            input.onEndEdit.AddListener(callback);
        }
    }

    void BindToggleEvent(string key, UnityEngine.Events.UnityAction<bool> callback)
    {
        if (toggles.TryGetValue(key, out Toggle toggle) && toggle != null)
        {
            toggle.onValueChanged.AddListener(callback);
        }
    }

    #endregion

    #region Apply & Reset

    void ApplyAllConfigToComponents()
    {
        foreach (var kvp in inputFields)
        {
            if (kvp.Value == null) continue;
            string v = kvp.Value.text;
            if (string.IsNullOrEmpty(v)) continue;

            switch (kvp.Key)
            {
                case "CellSize": if (roadGen != null && float.TryParse(v, out float cl)) roadGen.cellSize = cl; break;
                case "GridWidth": if (roadGen != null && int.TryParse(v, out int gw)) roadGen.gridWidth = gw; break;
                case "GridHeight": if (roadGen != null && int.TryParse(v, out int gh)) roadGen.gridHeight = gh; break;
                case "RandOffset": if (roadGen != null && float.TryParse(v, out float ro)) roadGen.randomOffset = ro; break;
                case "Seed": if (roadGen != null && int.TryParse(v, out int sd)) roadGen.seed = sd; break;
                case "RoadWidth": if (roadBuilder != null && float.TryParse(v, out float rw)) roadBuilder.roadWidth = rw; break;
                case "MeshRes": if (roadBuilder != null && float.TryParse(v, out float mr)) roadBuilder.meshResolution = mr; break;
                case "HeightOff": if (roadBuilder != null && float.TryParse(v, out float ho)) roadBuilder.roadHeightOffset = ho; break;
                case "UVScale": if (roadBuilder != null && float.TryParse(v, out float us)) roadBuilder.uvScale = us; break;
                case "TangentLen": if (roadBuilder != null && float.TryParse(v, out float tl)) roadBuilder.tangentLength = tl; break;
                case "BldHeight": if (roadBuilder != null && float.TryParse(v, out float bh)) roadBuilder.buildingHeight = bh; break;
                case "Sidewalk": if (roadBuilder != null && float.TryParse(v, out float sw)) roadBuilder.sidewalkWidth = sw; break;
                case "TLChance": if (trafficLightManager != null && float.TryParse(v, out float tc)) trafficLightManager.placementChance = tc; break;
                case "NPCCount": if (trafficManager != null && int.TryParse(v, out int nc)) trafficManager.npcCount = nc; break;
            }
        }

        Debug.Log("[MasterUIManager] All configs applied to components.");
    }

    void ResetAllDefaults()
    {
        SyncInputFieldValue("CellSize", "80");
        SyncInputFieldValue("GridWidth", "5");
        SyncInputFieldValue("GridHeight", "5");
        SyncInputFieldValue("RandOffset", "5");
        SyncInputFieldValue("Seed", "42");
        SyncInputFieldValue("RoadWidth", "6");
        SyncInputFieldValue("MeshRes", "2");
        SyncInputFieldValue("HeightOff", "0.15");
        SyncInputFieldValue("UVScale", "0.1");
        SyncInputFieldValue("TangentLen", "0.3");
        SyncInputFieldValue("BldHeight", "10");
        SyncInputFieldValue("Sidewalk", "2");
        SyncInputFieldValue("TLChance", "0.1");
        SyncInputFieldValue("NPCCount", "3");
        SyncInputFieldValue("NPCMaxSpeed", "30");
        SyncInputFieldValue("NPCSafeDist", "8");
        SyncInputFieldValue("NPCLookAhead", "0.020");

        SyncToggleValue("GenCity", true);
        SyncToggleValue("TrafficLights", true);
        SyncToggleValue("CountryUniform", true);
        SyncToggleValue("SplineGizmos", false);
        SyncToggleValue("ROS2Bridge", false);
        SyncToggleValue("MinimalMode", false);

        SyncDropdownValue("CityMode", 0);
        SyncDropdownValue("CamMode", 0);
        SyncDropdownValue("TimeMode", 1);

        if (citySubPanel != null) citySubPanel.SetActive(true);
        if (countrysideSubPanel != null) countrysideSubPanel.SetActive(false);

        Time.timeScale = 1f;

        Debug.Log("[MasterUIManager] All defaults reset.");
    }

    #endregion

    #region HUD Refresh

    private float fpsAccum = 0f;
    private int fpsFrames = 0;
    private float fpsValue = 60f;

    void RefreshHUD()
    {
        fpsAccum += Time.unscaledDeltaTime;
        fpsFrames++;
        if (fpsAccum >= 0.5f)
        {
            fpsValue = fpsFrames / fpsAccum;
            fpsAccum = 0f;
            fpsFrames = 0;
        }

        if (carController == null) carController = FindObjectOfType<SimpleCarController>();
        if (autoDrive == null) autoDrive = FindObjectOfType<SimpleAutoDrive>();

        SetHUDValue("HUDSpeed", carController != null ? carController.currentSpeed.ToString("F1") + " m/s" : "N/A");
        SetHUDValue("HUDSteering", carController != null ? carController.currentSteeringAngle.ToString("F1") + " deg" : "N/A");
        SetHUDValue("HUDAutoMode", carController != null ? (carController.autoMode ? "是" : "否") : "N/A");
        SetHUDValue("HUDState", autoDrive != null ? autoDrive.currentState.ToString() : "N/A");
        SetHUDValue("HUDLaneId", autoDrive != null ? autoDrive.currentLaneId.ToString() : "N/A");
        SetHUDValue("HUDYielding", autoDrive != null ? (autoDrive.isYielding ? "是 <<<" : "否") : "N/A");

        if (carController != null)
        {
            Vector3 pos = carController.GetPosition();
            SetHUDValue("HUDCoords", pos.x.ToString("F1") + ", " + pos.y.ToString("F1") + ", " + pos.z.ToString("F1"));
        }
        else
        {
            SetHUDValue("HUDCoords", "N/A");
        }

        SetHUDValue("HUDFPS", Mathf.RoundToInt(fpsValue).ToString());
        int vCount = trafficManager != null ? trafficManager.ActiveNPCs.Count : 0;
        SetHUDValue("HUDVehicleCount", vCount.ToString());

        bool rosCon = ros2Bridge != null && ros2Bridge.isConnected;
        string rosHz = ros2Bridge != null ? ros2Bridge.sendRate.ToString("F0") : "10";
        SetHUDValue("HUDRosStatus", rosCon ? "ON " + rosHz + "Hz" : "OFF");
        Color rosColor = rosCon ? new Color(0.2f, 0.9f, 0.3f, heartbeatAlpha) : new Color(0.5f, 0.5f, 0.5f);
        if (hudTexts.TryGetValue("HUDRosStatus", out TextMeshProUGUI rosVal) && rosVal != null) rosVal.color = rosColor;

        string ts = "x" + Time.timeScale.ToString("F0");
        SetHUDValue("HUDTimeScale", ts);

        Transform timeLabel = topBar != null ? topBar.transform.Find("TimeLabel") : null;
        if (timeLabel != null)
        {
            TextMeshProUGUI tl = timeLabel.GetComponent<TextMeshProUGUI>();
            if (tl != null) tl.text = "Time " + ts;
        }

        Transform rosDot = topBar != null ? topBar.transform.Find("RosStatusDot") : null;
        if (rosDot != null)
        {
            TextMeshProUGUI rd = rosDot.GetComponent<TextMeshProUGUI>();
            if (rd != null)
            {
                rd.text = rosCon ? "ROS2 ON" : "ROS2 OFF";
                rd.color = rosColor;
            }
        }
        int npcCount = FindObjectsOfType<SimpleAutoDrive>().Length;
SetHUDValue("HUDCurrentNode", autoDrive != null ? autoDrive.currentLaneId.ToString() : "N/A");
        SetHUDValue("HUDNextNode", autoDrive != null ? "计算中..." : "N/A"); 

        // 2. 动态读取底层的路网节点数 (安全反射防报错)
        int nodeCount = 0;
        if (WorldModel.Instance != null)
        {
            var nodeCountProp = WorldModel.Instance.GetType().GetProperty("NodeCount");
            if (nodeCountProp != null) nodeCount = (int)nodeCountProp.GetValue(WorldModel.Instance);
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
        SetHUDValue("HUDNodeCount", nodeCount.ToString());

        // 3. 读取基准高程
        float groundY = (WorldModel.Instance != null && carController != null) ? 
                        WorldModel.Instance.GetUnifiedHeight(carController.transform.position.x, carController.transform.position.z) : 0f;
        SetHUDValue("HUDGroundY", $"{groundY:F2} m");

    }

    void SetHUDValue(string key, string value)
    {
        if (hudTexts.TryGetValue(key, out TextMeshProUGUI txt) && txt != null)
        {
            txt.text = value;
        }
    }

    #endregion

    #region Key Display

    void RefreshAllKeyTexts()
    {
        if (RuntimeInputManager.Instance == null) return;
    }

    #endregion
}