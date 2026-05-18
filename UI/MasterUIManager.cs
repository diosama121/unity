using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MasterUIManager : MonoBehaviour
{
    [Header("=== Auto Setup ===")]
    public bool autoGenerateUI = true;
    public Font customFont;

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

    private Dictionary<string, Text> hudTexts = new Dictionary<string, Text>();
    private Dictionary<string, Text> keyTexts = new Dictionary<string, Text>();
    private Dictionary<string, InputField> inputFields = new Dictionary<string, InputField>();
    private Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private Dictionary<string, Dropdown> dropdowns = new Dictionary<string, Dropdown>();

    private float hudRefreshInterval = 0.2f;
    private float hudRefreshTimer = 0f;

    private static readonly Vector2 refResolution = new Vector2(1920, 1080);

    private bool isMinimalMode = false;

    private GameObject minimapRawImage;
    private GameObject torOverlay;
    private Text torOverlayText;
    private bool isTORFlashing = false;
    private float torFlashTimer = 0f;
    private float torFlashDuration = 0f;

    private GameObject thoughtStreamPanel;
    private Text thoughtStreamText;
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

        if (RuntimeInputManager.Instance == null) return;

        if (!isRebinding)
        {
            if (RuntimeInputManager.Instance.GetKeyDown("ToggleUI"))
            {
                ToggleMinimalMode();
            }

            if (RuntimeInputManager.Instance.GetKeyDown("ToggleAuto"))
            {
                ToggleAutoDrive();
            }

            if (RuntimeInputManager.Instance.GetKey("Brake") && carController != null)
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

        hudRefreshTimer += Time.deltaTime;
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

    void BuildTOROverlay(GameObject root, Font font)
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
        torOverlayText = torTextGO.AddComponent<Text>();
        torOverlayText.text = "TAKE OVER REQUEST\nEMERGENCY BRAKE";
        torOverlayText.font = font;
        torOverlayText.fontSize = 48;
        torOverlayText.resizeTextForBestFit = true;
        torOverlayText.resizeTextMinSize = 18;
        torOverlayText.resizeTextMaxSize = 48;
        torOverlayText.fontStyle = FontStyle.Bold;
        torOverlayText.color = Color.white;
        torOverlayText.alignment = TextAnchor.MiddleCenter;
        RectTransform ttxtRT = torTextGO.GetComponent<RectTransform>();
        ttxtRT.anchorMin = Vector2.zero;
        ttxtRT.anchorMax = Vector2.one;
        ttxtRT.offsetMin = new Vector2(16, 16);
        ttxtRT.offsetMax = new Vector2(-16, -16);

        torOverlay.SetActive(false);
    }

    void BuildThoughtStream(GameObject root, Font font)
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
        thoughtStreamText = textChild.AddComponent<Text>();
        thoughtStreamText.font = font;
        thoughtStreamText.fontSize = 11;
        thoughtStreamText.resizeTextForBestFit = true;
        thoughtStreamText.resizeTextMinSize = 7;
        thoughtStreamText.resizeTextMaxSize = 11;
        thoughtStreamText.color = new Color(0.3f, 0.9f, 0.5f);
        thoughtStreamText.alignment = TextAnchor.LowerLeft;
        thoughtStreamText.horizontalOverflow = HorizontalWrapMode.Overflow;
        thoughtStreamText.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform ttsRT = textChild.GetComponent<RectTransform>();
        ttsRT.anchorMin = Vector2.zero;
        ttsRT.anchorMax = Vector2.one;
        ttsRT.offsetMin = new Vector2(6, 4);
        ttsRT.offsetMax = new Vector2(-6, -4);
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

    void ToggleMinimalMode()
    {
        isMinimalMode = !isMinimalMode;
        if (rightPanel != null) rightPanel.SetActive(!isMinimalMode);
        DebugPanel dbg = FindObjectOfType<DebugPanel>();
        if (dbg != null) dbg.gameObject.SetActive(!isMinimalMode);
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
        Font font = customFont != null ? customFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    void BuildTopBar(GameObject root, Font font)
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

        CreateNavButton(topBar, "NavHUD", "HUD", font, () => SwitchToModule(moduleBaseSettings));
        CreateNavButton(topBar, "NavTerrain", "Terrain", font, () => SwitchToModule(moduleTerrain));
        CreateNavButton(topBar, "NavTraffic", "Traffic", font, () => SwitchToModule(moduleTraffic));
        CreateNavButton(topBar, "NavSystem", "System", font, () => SwitchToModule(moduleSystem));
        CreateNavButton(topBar, "NavCamera", "Camera", font, () => SwitchToModule(moduleCamera));

        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(topBar.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1;

        GameObject genWorldBtn = new GameObject("BtnGenerateWorld");
        genWorldBtn.transform.SetParent(topBar.transform, false);
        genWorldBtn.AddComponent<LayoutElement>().minWidth = 160;
        genWorldBtn.AddComponent<LayoutElement>().minHeight = 36;
        Button genBtn = genWorldBtn.AddComponent<Button>();
        Image genImg = genWorldBtn.AddComponent<Image>();
        genImg.color = new Color(0.85f, 0.25f, 0.25f);
        genBtn.targetGraphic = genImg;
        GameObject genTxtGO = new GameObject("Text");
        genTxtGO.transform.SetParent(genWorldBtn.transform, false);
        Text genTxt = genTxtGO.AddComponent<Text>();
        genTxt.text = "Generate World";
        genTxt.font = font;
        genTxt.fontSize = 14;
        genTxt.resizeTextForBestFit = true;
        genTxt.resizeTextMinSize = 8;
        genTxt.resizeTextMaxSize = 14;
        genTxt.fontStyle = FontStyle.Bold;
        genTxt.color = Color.white;
        genTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform genTxtRT = genTxtGO.GetComponent<RectTransform>();
        genTxtRT.anchorMin = Vector2.zero;
        genTxtRT.anchorMax = Vector2.one;
        genTxtRT.sizeDelta = Vector2.zero;
        genBtn.onClick.AddListener(() =>
        {
            if (WorldModel.Instance != null)
            {
                WorldModel.Instance.TriggerWorldGeneration();
            }
        });

        GameObject rosDotGO = new GameObject("RosStatusDot");
        rosDotGO.transform.SetParent(topBar.transform, false);
        rosDotGO.AddComponent<LayoutElement>().minWidth = 20;
        rosDotGO.AddComponent<LayoutElement>().minHeight = 20;
        Text rosDot = rosDotGO.AddComponent<Text>();
        rosDot.text = "ROS2";
        rosDot.font = font;
        rosDot.fontSize = 11;
        rosDot.resizeTextForBestFit = true;
        rosDot.resizeTextMinSize = 7;
        rosDot.resizeTextMaxSize = 11;
        rosDot.color = new Color(0.5f, 0.5f, 0.5f);
        rosDot.alignment = TextAnchor.MiddleCenter;
        rosDotGO.name = "RosStatusDot";

        GameObject timeLabelGO = new GameObject("TimeLabel");
        timeLabelGO.transform.SetParent(topBar.transform, false);
        timeLabelGO.AddComponent<LayoutElement>().minWidth = 80;
        Text timeLabel = timeLabelGO.AddComponent<Text>();
        timeLabel.text = "Time x1";
        timeLabel.font = font;
        timeLabel.fontSize = 12;
        timeLabel.resizeTextForBestFit = true;
        timeLabel.resizeTextMinSize = 7;
        timeLabel.resizeTextMaxSize = 12;
        timeLabel.color = new Color(0.3f, 0.8f, 1f);
        timeLabel.alignment = TextAnchor.MiddleCenter;
        timeLabelGO.name = "TimeLabel";
    }

    void CreateNavButton(GameObject parent, string name, string label, Font font, UnityEngine.Events.UnityAction callback)
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
        Text txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.font = font;
        txt.fontSize = 13;
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = 8;
        txt.resizeTextMaxSize = 13;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        RectTransform txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        btn.onClick.AddListener(callback);
    }

    void BuildLeftPanel(GameObject root, Font font)
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
        scVLG.childControlHeight = false;
        scVLG.childForceExpandWidth = true;
        scVLG.childForceExpandHeight = false;

        ContentSizeFitter scCSF = scrollContent.AddComponent<ContentSizeFitter>();
        scCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRT;
        scrollRect.content = scRT;

        BuildHUDPanel(scrollContent, font);
        BuildKeyPanel(scrollContent, font);
    }

    void BuildHUDPanel(GameObject parent, Font font)
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
        hudVLG.childControlHeight = false;
        hudVLG.childForceExpandWidth = true;
        hudVLG.childForceExpandHeight = false;

        UIPanelBuilder.CreateTitle(hudPanel, "Vehicle HUD");

        GameObject minimapGO = new GameObject("Minimap");
        minimapGO.transform.SetParent(hudPanel.transform, false);
        minimapGO.AddComponent<LayoutElement>().minHeight = 100;
        minimapGO.AddComponent<LayoutElement>().minWidth = 200;
        RawImage rawImg = minimapGO.AddComponent<RawImage>();
        rawImg.color = new Color(0.85f, 0.85f, 0.85f, 0.3f);
        minimapRawImage = minimapGO;

        hudTexts["HUDSpeed"] = CreateHUDLabel(hudPanel, "Speed", "0.0 m/s", font);
        hudTexts["HUDSteering"] = CreateHUDLabel(hudPanel, "Steering", "0.0 deg", font);
        hudTexts["HUDAutoMode"] = CreateHUDLabel(hudPanel, "Auto Mode", "No", font);
        hudTexts["HUDState"] = CreateHUDLabel(hudPanel, "State", "Idle", font);
        hudTexts["HUDLaneId"] = CreateHUDLabel(hudPanel, "Lane ID", "N/A", font);
        hudTexts["HUDYielding"] = CreateHUDLabel(hudPanel, "Yielding", "No", font);
        hudTexts["HUDCoords"] = CreateHUDLabel(hudPanel, "Position", "0,0,0", font);
        hudTexts["HUDTimeScale"] = CreateHUDLabel(hudPanel, "Time Scale", "x1", font);
        hudTexts["HUDFPS"] = CreateHUDLabel(hudPanel, "FPS", "0", font);
        hudTexts["HUDVehicleCount"] = CreateHUDLabel(hudPanel, "Vehicles", "0", font);
        hudTexts["HUDRosStatus"] = CreateHUDLabel(hudPanel, "ROS2", "OFF", font);
    }

    Text CreateHUDLabel(GameObject parent, string label, string defaultValue, Font font)
    {
        GameObject row = new GameObject("HUD_" + label);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 20;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 8;

        GameObject lGO = new GameObject("Label");
        lGO.transform.SetParent(row.transform, false);
        Text lTxt = lGO.AddComponent<Text>();
        lTxt.text = label + ":";
        lTxt.font = font;
        lTxt.fontSize = 11;
        lTxt.resizeTextForBestFit = true;
        lTxt.resizeTextMinSize = 7;
        lTxt.resizeTextMaxSize = 11;
        lTxt.color = new Color(0.7f, 0.7f, 0.75f);
        lTxt.alignment = TextAnchor.MiddleLeft;
        lGO.AddComponent<LayoutElement>().minWidth = 65;

        GameObject vGO = new GameObject("Value");
        vGO.transform.SetParent(row.transform, false);
        Text vTxt = vGO.AddComponent<Text>();
        vTxt.text = defaultValue;
        vTxt.font = font;
        vTxt.fontSize = 12;
        vTxt.resizeTextForBestFit = true;
        vTxt.resizeTextMinSize = 7;
        vTxt.resizeTextMaxSize = 12;
        vTxt.fontStyle = FontStyle.Bold;
        vTxt.color = new Color(0.2f, 0.9f, 0.5f);
        vTxt.alignment = TextAnchor.MiddleLeft;
        vGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return vTxt;
    }

    void BuildKeyPanel(GameObject parent, Font font)
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
        kpVLG.childControlHeight = false;
        kpVLG.childForceExpandWidth = true;
        kpVLG.childForceExpandHeight = false;

        UIPanelBuilder.CreateTitle(keyPanel, "Key Bindings");

        keyTexts["W"] = CreateKeyDisplay(keyPanel, "W / Up", "Forward", font);
        keyTexts["S"] = CreateKeyDisplay(keyPanel, "S / Down", "Reverse", font);
        keyTexts["A"] = CreateKeyDisplay(keyPanel, "A", "Turn Left", font);
        keyTexts["D"] = CreateKeyDisplay(keyPanel, "D", "Turn Right", font);
        keyTexts["N"] = CreateKeyDisplay(keyPanel, "N", "Reset Nav", font);
        keyTexts["R"] = CreateKeyDisplay(keyPanel, "R", "Reset Pos", font);
        keyTexts["Space"] = CreateKeyDisplay(keyPanel, "Space", "Brake", font);
    }

    Text CreateKeyDisplay(GameObject parent, string key, string desc, Font font)
    {
        GameObject row = new GameObject("Key_" + key);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 19;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 8;

        GameObject kGO = new GameObject("KeyLabel");
        kGO.transform.SetParent(row.transform, false);
        Text kTxt = kGO.AddComponent<Text>();
        kTxt.text = key;
        kTxt.font = font;
        kTxt.fontSize = 12;
        kTxt.resizeTextForBestFit = true;
        kTxt.resizeTextMinSize = 7;
        kTxt.resizeTextMaxSize = 12;
        kTxt.fontStyle = FontStyle.Bold;
        kTxt.color = new Color(1f, 0.85f, 0.2f);
        kTxt.alignment = TextAnchor.MiddleLeft;
        kGO.AddComponent<LayoutElement>().minWidth = 70;

        GameObject dGO = new GameObject("DescLabel");
        dGO.transform.SetParent(row.transform, false);
        Text dTxt = dGO.AddComponent<Text>();
        dTxt.text = desc;
        dTxt.font = font;
        dTxt.fontSize = 11;
        dTxt.resizeTextForBestFit = true;
        dTxt.resizeTextMinSize = 7;
        dTxt.resizeTextMaxSize = 11;
        dTxt.color = new Color(0.55f, 0.55f, 0.6f);
        dTxt.alignment = TextAnchor.MiddleLeft;
        dGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return dTxt;
    }

    void BuildRightPanel(GameObject root, Font font)
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
        scVLG.childControlHeight = false;
        scVLG.childForceExpandWidth = true;
        scVLG.childForceExpandHeight = false;

        ContentSizeFitter scCSF = rightScrollContent.AddComponent<ContentSizeFitter>();
        scCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRT;
        scrollRect.content = scRT;

        BuildAllModules(rightScrollContent, font);
    }

    void BuildAllModules(GameObject content, Font font)
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

    GameObject BuildFoldoutSection(GameObject parent, string title, Font font)
    {
        GameObject section = new GameObject("Foldout_" + title);
        section.transform.SetParent(parent.transform, false);
        section.AddComponent<LayoutElement>().minHeight = 30;

        VerticalLayoutGroup secVLG = section.AddComponent<VerticalLayoutGroup>();
        secVLG.padding = new RectOffset(0, 0, 0, 0);
        secVLG.spacing = 0;
        secVLG.childAlignment = TextAnchor.UpperCenter;
        secVLG.childControlWidth = true;
        secVLG.childControlHeight = false;
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
        Text arrowTxt = arrowGO.AddComponent<Text>();
        arrowTxt.text = "v";
        arrowTxt.font = font;
        arrowTxt.fontSize = 14;
        arrowTxt.resizeTextForBestFit = true;
        arrowTxt.resizeTextMinSize = 8;
        arrowTxt.resizeTextMaxSize = 14;
        arrowTxt.color = new Color(0.3f, 0.8f, 1f);
        arrowTxt.alignment = TextAnchor.MiddleCenter;
        arrowGO.AddComponent<LayoutElement>().minWidth = 20;

        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(headerGO.transform, false);
        Text titleTxt = titleGO.AddComponent<Text>();
        titleTxt.text = title;
        titleTxt.font = font;
        titleTxt.fontSize = 14;
        titleTxt.resizeTextForBestFit = true;
        titleTxt.resizeTextMinSize = 8;
        titleTxt.resizeTextMaxSize = 14;
        titleTxt.fontStyle = FontStyle.Bold;
        titleTxt.color = new Color(0.85f, 0.85f, 0.9f);
        titleTxt.alignment = TextAnchor.MiddleLeft;
        titleGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        GameObject contentGO = new GameObject("FoldoutContent");
        contentGO.transform.SetParent(section.transform, false);
        contentGO.AddComponent<LayoutElement>().minHeight = 20;

        VerticalLayoutGroup cntVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        cntVLG.padding = new RectOffset(8, 8, 6, 6);
        cntVLG.spacing = 3;
        cntVLG.childAlignment = TextAnchor.UpperCenter;
        cntVLG.childControlWidth = true;
        cntVLG.childControlHeight = false;
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

    GameObject BuildModule1BaseSettings(GameObject parent, Font font)
    {
        GameObject module = new GameObject("Module_BaseSettings");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = false;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "Base Settings");

        GameObject foldContent = BuildFoldoutSection(module, "Road Network", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "CellSizeInput", "Cell Size", "80", InputField.ContentType.DecimalNumber), "CellSize");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "GridWidthInput", "Grid Width", "5", InputField.ContentType.IntegerNumber), "GridWidth");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "GridHeightInput", "Grid Height", "5", InputField.ContentType.IntegerNumber), "GridHeight");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "RandOffsetInput", "Rand Offset", "5", InputField.ContentType.DecimalNumber), "RandOffset");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "SeedInput", "Seed", "42", InputField.ContentType.IntegerNumber), "Seed");

        GameObject foldRoad = BuildFoldoutSection(module, "Road Mesh", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "RoadWidthInput", "Road Width", "6", InputField.ContentType.DecimalNumber), "RoadWidth");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "MeshResInput", "Mesh Res", "2", InputField.ContentType.DecimalNumber), "MeshRes");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "HeightOffInput", "Height Off", "0.15", InputField.ContentType.DecimalNumber), "HeightOff");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "UVScaleInput", "UV Scale", "0.1", InputField.ContentType.DecimalNumber), "UVScale");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldRoad, "TangentLenInput", "Tangent Len", "0.3", InputField.ContentType.DecimalNumber), "TangentLen");

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule2Terrain(GameObject parent, Font font)
    {
        GameObject module = new GameObject("Module_Terrain");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = false;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "Terrain & Scene");

        GameObject modeRow = new GameObject("ModeDropdownRow");
        modeRow.transform.SetParent(module.transform, false);
        modeRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup mhlg = modeRow.AddComponent<HorizontalLayoutGroup>();
        mhlg.padding = new RectOffset(4, 4, 0, 0);
        mhlg.spacing = 8;
        mhlg.childAlignment = TextAnchor.MiddleLeft;
        mhlg.childControlWidth = true;
        mhlg.childControlHeight = true;
        mhlg.childForceExpandWidth = false;
        mhlg.childForceExpandHeight = true;

        GameObject modeLabel = new GameObject("Label");
        modeLabel.transform.SetParent(modeRow.transform, false);
        Text modeLabelTxt = modeLabel.AddComponent<Text>();
        modeLabelTxt.text = "Mode:";
        modeLabelTxt.font = font;
        modeLabelTxt.fontSize = 13;
        modeLabelTxt.resizeTextForBestFit = true;
        modeLabelTxt.resizeTextMinSize = 8;
        modeLabelTxt.resizeTextMaxSize = 13;
        modeLabelTxt.color = Color.white;
        modeLabelTxt.alignment = TextAnchor.MiddleLeft;
        modeLabel.AddComponent<LayoutElement>().minWidth = 55;

        GameObject modeDropdownGO = new GameObject("Dropdown");
        modeDropdownGO.transform.SetParent(modeRow.transform, false);
        modeDropdownGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        Dropdown modeDropdown = modeDropdownGO.AddComponent<Dropdown>();
        modeDropdown.options = new List<Dropdown.OptionData>
        {
            new Dropdown.OptionData("City"),
            new Dropdown.OptionData("Countryside")
        };
        modeDropdown.value = 0;

        Image ddImg = modeDropdownGO.AddComponent<Image>();
        ddImg.color = new Color(0.2f, 0.22f, 0.3f);

        GameObject ddLabelGO = new GameObject("Label");
        ddLabelGO.transform.SetParent(modeDropdownGO.transform, false);
        Text ddLabel = ddLabelGO.AddComponent<Text>();
        ddLabel.text = "City";
        ddLabel.font = font;
        ddLabel.fontSize = 13;
        ddLabel.resizeTextForBestFit = true;
        ddLabel.resizeTextMinSize = 8;
        ddLabel.resizeTextMaxSize = 13;
        ddLabel.color = Color.white;
        ddLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform ddLRT = ddLabelGO.GetComponent<RectTransform>();
        ddLRT.anchorMin = Vector2.zero;
        ddLRT.anchorMax = Vector2.one;
        ddLRT.offsetMin = new Vector2(8, 0);
        ddLRT.offsetMax = new Vector2(-20, 0);
        modeDropdown.captionText = ddLabel;

        GameObject ddItemLabelGO = new GameObject("ItemLabel");
        ddItemLabelGO.transform.SetParent(modeDropdownGO.transform, false);
        Text ddItemLabel = ddItemLabelGO.AddComponent<Text>();
        ddItemLabel.text = "";
        ddItemLabel.font = font;
        ddItemLabel.fontSize = 13;
        ddItemLabel.resizeTextForBestFit = true;
        ddItemLabel.resizeTextMinSize = 8;
        ddItemLabel.resizeTextMaxSize = 13;
        ddItemLabel.color = Color.black;
        ddItemLabel.alignment = TextAnchor.MiddleLeft;
        modeDropdown.itemText = ddItemLabel;

        dropdowns["CityMode"] = modeDropdown;

        citySubPanel = BuildFoldoutSection(module, "City Settings", font);
        countrysideSubPanel = BuildFoldoutSection(module, "Countryside Settings", font);

        GameObject weatherRow = new GameObject("WeatherDropdownRow");
        weatherRow.transform.SetParent(module.transform, false);
        weatherRow.AddComponent<LayoutElement>().minHeight = 36;
        HorizontalLayoutGroup whlg = weatherRow.AddComponent<HorizontalLayoutGroup>();
        whlg.padding = new RectOffset(4, 4, 8, 4);
        whlg.spacing = 8;
        whlg.childAlignment = TextAnchor.MiddleLeft;
        whlg.childControlWidth = true;
        whlg.childControlHeight = true;
        whlg.childForceExpandWidth = false;
        whlg.childForceExpandHeight = true;

        GameObject weatherLabel = new GameObject("Label");
        weatherLabel.transform.SetParent(weatherRow.transform, false);
        Text weatherLabelTxt = weatherLabel.AddComponent<Text>();
        weatherLabelTxt.text = "Weather:";
        weatherLabelTxt.font = font;
        weatherLabelTxt.fontSize = 13;
        weatherLabelTxt.resizeTextForBestFit = true;
        weatherLabelTxt.resizeTextMinSize = 8;
        weatherLabelTxt.resizeTextMaxSize = 13;
        weatherLabelTxt.color = Color.white;
        weatherLabelTxt.alignment = TextAnchor.MiddleLeft;
        weatherLabel.AddComponent<LayoutElement>().minWidth = 70;

        GameObject weatherDropdownGO = new GameObject("Dropdown");
        weatherDropdownGO.transform.SetParent(weatherRow.transform, false);
        weatherDropdownGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        Dropdown weatherDropdown = weatherDropdownGO.AddComponent<Dropdown>();
        weatherDropdown.options = new List<Dropdown.OptionData>
        {
            new Dropdown.OptionData("Sunny"),
            new Dropdown.OptionData("Rain/Snow")
        };
        weatherDropdown.value = 0;

        Image wdImg = weatherDropdownGO.AddComponent<Image>();
        wdImg.color = new Color(0.2f, 0.22f, 0.3f);

        GameObject wdLabelGO = new GameObject("Label");
        wdLabelGO.transform.SetParent(weatherDropdownGO.transform, false);
        Text wdLabel = wdLabelGO.AddComponent<Text>();
        wdLabel.text = "Sunny";
        wdLabel.font = font;
        wdLabel.fontSize = 13;
        wdLabel.resizeTextForBestFit = true;
        wdLabel.resizeTextMinSize = 8;
        wdLabel.resizeTextMaxSize = 13;
        wdLabel.color = Color.white;
        wdLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform wdLRT = wdLabelGO.GetComponent<RectTransform>();
        wdLRT.anchorMin = Vector2.zero;
        wdLRT.anchorMax = Vector2.one;
        wdLRT.offsetMin = new Vector2(8, 0);
        wdLRT.offsetMax = new Vector2(-20, 0);
        weatherDropdown.captionText = wdLabel;

        GameObject wdItemLabelGO = new GameObject("ItemLabel");
        wdItemLabelGO.transform.SetParent(weatherDropdownGO.transform, false);
        Text wdItemLabel = wdItemLabelGO.AddComponent<Text>();
        wdItemLabel.text = "";
        wdItemLabel.font = font;
        wdItemLabel.fontSize = 13;
        wdItemLabel.resizeTextForBestFit = true;
        wdItemLabel.resizeTextMinSize = 8;
        wdItemLabel.resizeTextMaxSize = 13;
        wdItemLabel.color = Color.black;
        wdItemLabel.alignment = TextAnchor.MiddleLeft;
        weatherDropdown.itemText = wdItemLabel;

        dropdowns["Weather"] = weatherDropdown;

        RegisterToggle(CreateToggleRow(citySubPanel, "GenCityToggle", "Generate Buildings", true, font), "GenCity");
        RegisterInputField(UIPanelBuilder.CreateInputRow(citySubPanel, "BldHeightInput", "Bld Height", "10", InputField.ContentType.DecimalNumber), "BldHeight");
        RegisterInputField(UIPanelBuilder.CreateInputRow(citySubPanel, "SidewalkInput", "Sidewalk Width", "2", InputField.ContentType.DecimalNumber), "Sidewalk");
        RegisterToggle(CreateToggleRow(citySubPanel, "TrafficLightToggle", "Traffic Lights", true, font), "TrafficLights");
        RegisterInputField(UIPanelBuilder.CreateInputRow(citySubPanel, "TLChanceInput", "TL Frequency", "0.6", InputField.ContentType.DecimalNumber), "TLChance");
        RegisterToggle(CreateToggleRow(citySubPanel, "PedestrianToggle", "Pedestrians", false, font), "Pedestrians");
        RegisterInputField(UIPanelBuilder.CreateInputRow(citySubPanel, "PedSpawnInput", "Ped Spawn Rate", "1.0", InputField.ContentType.DecimalNumber), "PedSpawn");

        RegisterToggle(CreateToggleRow(countrysideSubPanel, "CountryUniformToggle", "Uniform Materials", true, font), "CountryUniform");

        modeDropdown.onValueChanged.AddListener((idx) =>
        {
            bool isCity = (idx == 0);
            if (citySubPanel != null) citySubPanel.SetActive(isCity);
            if (countrysideSubPanel != null) countrysideSubPanel.SetActive(!isCity);
        });

        citySubPanel.SetActive(true);
        countrysideSubPanel.SetActive(false);

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule3Traffic(GameObject parent, Font font)
    {
        GameObject module = new GameObject("Module_Traffic");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = false;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "Traffic & NPC");

        GameObject foldContent = BuildFoldoutSection(module, "NPC Configuration", font);

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCCountInput", "NPC Count", "3", InputField.ContentType.IntegerNumber), "NPCCount");

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
        Text nmLabelTxt = nmLabel.AddComponent<Text>();
        nmLabelTxt.text = "Drive Mode:";
        nmLabelTxt.font = font;
        nmLabelTxt.fontSize = 13;
        nmLabelTxt.resizeTextForBestFit = true;
        nmLabelTxt.resizeTextMinSize = 8;
        nmLabelTxt.resizeTextMaxSize = 13;
        nmLabelTxt.color = Color.white;
        nmLabelTxt.alignment = TextAnchor.MiddleLeft;
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
        nmDDLabel.font = font;
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
        nmItemLabel.font = font;
        nmItemLabel.fontSize = 13;
        nmItemLabel.resizeTextForBestFit = true;
        nmItemLabel.resizeTextMinSize = 8;
        nmItemLabel.resizeTextMaxSize = 13;
        nmItemLabel.color = Color.black;
        nmItemLabel.alignment = TextAnchor.MiddleLeft;
        npcModeDropdown.itemText = nmItemLabel;

        dropdowns["NPCMode"] = npcModeDropdown;

        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCMaxSpeedInput", "NPC Max Speed", "30", InputField.ContentType.DecimalNumber), "NPCMaxSpeed");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCSafeDistInput", "Safe Distance", "8", InputField.ContentType.DecimalNumber), "NPCSafeDist");
        RegisterInputField(UIPanelBuilder.CreateInputRow(foldContent, "NPCLookAheadInput", "Look Ahead T", "0.02", InputField.ContentType.DecimalNumber), "NPCLookAhead");

        GameObject spawnBtn = UIPanelBuilder.CreateButton(foldContent, "SpawnNPCsBtn", "Spawn NPCs");
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

        GameObject emergencyBtn = UIPanelBuilder.CreateButton(foldContent, "EmergencyBtn", "Summon Emergency Vehicle");
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

    GameObject BuildModule4System(GameObject parent, Font font)
    {
        GameObject module = new GameObject("Module_System");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = false;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "System & Debug");

        GameObject foldContent = BuildFoldoutSection(module, "System Settings", font);

        RegisterToggle(CreateToggleRow(foldContent, "ROS2Toggle", "ROS2 Bridge", false, font), "ROS2Bridge");
        RegisterToggle(CreateToggleRow(foldContent, "SplineGizmoToggle", "Raycast/Spline Gizmos", false, font), "SplineGizmos");
        RegisterToggle(CreateToggleRow(foldContent, "MinimalModeToggle", "Minimal UI Mode", false, font), "MinimalMode");

        UIPanelBuilder.CreateSectionHeader(foldContent, "--- Traffic Light Status ---");
        GameObject tlStatusRow = UIPanelBuilder.CreateDebugRow(foldContent, "TLStatus", "TL State", "N/A");
        Text tlStatusTxt = tlStatusRow != null ? tlStatusRow.GetComponentInChildren<Text>() : null;
        hudTexts["TLStatus"] = tlStatusTxt;

        module.SetActive(false);
        return module;
    }

    GameObject BuildModule5Camera(GameObject parent, Font font)
    {
        GameObject module = new GameObject("Module_Camera");
        module.transform.SetParent(parent.transform, false);
        module.AddComponent<LayoutElement>().minHeight = 100;
        VerticalLayoutGroup mVLG = module.AddComponent<VerticalLayoutGroup>();
        mVLG.padding = new RectOffset(0, 0, 0, 0);
        mVLG.spacing = 2;
        mVLG.childAlignment = TextAnchor.UpperCenter;
        mVLG.childControlWidth = true;
        mVLG.childControlHeight = false;
        mVLG.childForceExpandWidth = true;
        mVLG.childForceExpandHeight = false;
        ContentSizeFitter mCSF = module.AddComponent<ContentSizeFitter>();
        mCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIPanelBuilder.CreateTitle(module, "Camera & Time");

        GameObject foldContent = BuildFoldoutSection(module, "Settings", font);

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
        Text cmLabelTxt = cmLabel.AddComponent<Text>();
        cmLabelTxt.text = "Cam Mode:";
        cmLabelTxt.font = font;
        cmLabelTxt.fontSize = 13;
        cmLabelTxt.resizeTextForBestFit = true;
        cmLabelTxt.resizeTextMinSize = 8;
        cmLabelTxt.resizeTextMaxSize = 13;
        cmLabelTxt.color = Color.white;
        cmLabelTxt.alignment = TextAnchor.MiddleLeft;
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
        cmDDLabel.font = font;
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
        cmItemLabel.font = font;
        cmItemLabel.fontSize = 13;
        cmItemLabel.resizeTextForBestFit = true;
        cmItemLabel.resizeTextMinSize = 8;
        cmItemLabel.resizeTextMaxSize = 13;
        cmItemLabel.color = Color.black;
        cmItemLabel.alignment = TextAnchor.MiddleLeft;
        camModeDropdown.itemText = cmItemLabel;

        dropdowns["CamMode"] = camModeDropdown;

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
        Text tmLabelTxt = tmLabel.AddComponent<Text>();
        tmLabelTxt.text = "Time Scale:";
        tmLabelTxt.font = font;
        tmLabelTxt.fontSize = 13;
        tmLabelTxt.resizeTextForBestFit = true;
        tmLabelTxt.resizeTextMinSize = 8;
        tmLabelTxt.resizeTextMaxSize = 13;
        tmLabelTxt.color = Color.white;
        tmLabelTxt.alignment = TextAnchor.MiddleLeft;
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
        tmDDLabel.font = font;
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
        tmItemLabel.font = font;
        tmItemLabel.fontSize = 13;
        tmItemLabel.resizeTextForBestFit = true;
        tmItemLabel.resizeTextMinSize = 8;
        tmItemLabel.resizeTextMaxSize = 13;
        tmItemLabel.color = Color.black;
        tmItemLabel.alignment = TextAnchor.MiddleLeft;
        timeModeDropdown.itemText = tmItemLabel;

        dropdowns["TimeMode"] = timeModeDropdown;

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

        module.SetActive(false);
        return module;
    }

    void BuildModule6GlobalButtons(GameObject parent, Font font)
    {
        GameObject btnSection = new GameObject("Module_GlobalBtns");
        btnSection.transform.SetParent(parent.transform, false);
        btnSection.AddComponent<LayoutElement>().minHeight = 60;

        VerticalLayoutGroup bvl = btnSection.AddComponent<VerticalLayoutGroup>();
        bvl.padding = new RectOffset(8, 8, 8, 8);
        bvl.spacing = 6;
        bvl.childAlignment = TextAnchor.UpperCenter;
        bvl.childControlWidth = true;
        bvl.childControlHeight = false;
        bvl.childForceExpandWidth = true;
        bvl.childForceExpandHeight = false;

        GameObject applyBtn = UIPanelBuilder.CreateButton(btnSection, "ApplyRegenBtn", "Apply Config & Regenerate World");
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

        GameObject resetBtn = UIPanelBuilder.CreateButton(btnSection, "ResetDefaultsBtn", "Reset All Defaults");
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

    GameObject CreateToggleRow(GameObject parent, string name, string label, bool defaultValue, Font font)
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
        Text labelTxt = labelGO.AddComponent<Text>();
        labelTxt.text = label;
        labelTxt.font = font;
        labelTxt.fontSize = 13;
        labelTxt.resizeTextForBestFit = true;
        labelTxt.resizeTextMinSize = 8;
        labelTxt.resizeTextMaxSize = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAnchor.MiddleLeft;
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
        SyncInputFieldValue("TLChance", trafficLightManager != null ? trafficLightManager.placementChance.ToString("F2") : "0.6");
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
        SyncDropdownValue("Weather", 0);

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

        BindInputFieldEvent("BldHeight", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.buildingHeight = f; });
        BindInputFieldEvent("Sidewalk", (v) => { if (roadBuilder != null && float.TryParse(v, out float f)) roadBuilder.sidewalkWidth = f; });
        BindInputFieldEvent("TLChance", (v) => { if (trafficLightManager != null && float.TryParse(v, out float f)) trafficLightManager.placementChance = f; });

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

        BindToggleEvent("GenCity", (on) => { if (roadBuilder != null) roadBuilder.generateCity = on; });
        BindToggleEvent("TrafficLights", (on) =>
        {
            if (trafficLightManager != null) trafficLightManager.enabled = on;
        });
        BindToggleEvent("CountryUniform", (on) => { if (roadBuilder != null) roadBuilder.useCountrysideUniformMaterials = on; });
        BindToggleEvent("SplineGizmos", (on) => { if (roadBuilder != null) roadBuilder.showSplineGizmos = on; });
        BindToggleEvent("ROS2Bridge", (on) =>
        {
            if (ros2Bridge != null) ros2Bridge.enabled = on;
        });
        BindToggleEvent("MinimalMode", (on) =>
        {
            isMinimalMode = on;
            if (rightPanel != null) rightPanel.SetActive(!on);
        });

        if (dropdowns.TryGetValue("CityMode", out Dropdown cityDD))
        {
            cityDD.onValueChanged.AddListener((idx) =>
            {
                if (roadGen != null) roadGen.isCountryside = (idx == 1);
                if (citySubPanel != null) citySubPanel.SetActive(idx == 0);
                if (countrysideSubPanel != null) countrysideSubPanel.SetActive(idx == 1);
            });
        }

        if (dropdowns.TryGetValue("Weather", out Dropdown weatherDD))
        {
            weatherDD.onValueChanged.AddListener((idx) =>
            {
                WeatherManager weather = FindObjectOfType<WeatherManager>();
                if (weather != null) weather.SetWeather(idx == 0 ? WeatherType.Sunny : WeatherType.RainSnow);
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
        SyncInputFieldValue("TLChance", "0.6");
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
        SyncDropdownValue("Weather", 0);

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
        SetHUDValue("HUDAutoMode", carController != null ? (carController.autoMode ? "Yes" : "No") : "N/A");
        SetHUDValue("HUDState", autoDrive != null ? autoDrive.currentState.ToString() : "N/A");
        SetHUDValue("HUDLaneId", autoDrive != null ? autoDrive.currentLaneId.ToString() : "N/A");
        SetHUDValue("HUDYielding", autoDrive != null ? (autoDrive.isYielding ? "YES <<<" : "No") : "N/A");

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
        if (hudTexts.TryGetValue("HUDRosStatus", out Text rosVal) && rosVal != null) rosVal.color = rosColor;

        string ts = "x" + Time.timeScale.ToString("F0");
        SetHUDValue("HUDTimeScale", ts);

        Transform timeLabel = topBar != null ? topBar.transform.Find("TimeLabel") : null;
        if (timeLabel != null)
        {
            Text tl = timeLabel.GetComponent<Text>();
            if (tl != null) tl.text = "Time " + ts;
        }

        Transform rosDot = topBar != null ? topBar.transform.Find("RosStatusDot") : null;
        if (rosDot != null)
        {
            Text rd = rosDot.GetComponent<Text>();
            if (rd != null)
            {
                rd.text = rosCon ? "ROS2 ON" : "ROS2 OFF";
                rd.color = rosColor;
            }
        }

        SetHUDValue("TLStatus", trafficLightManager != null ? "Active" : "Inactive");
    }

    void SetHUDValue(string key, string value)
    {
        if (hudTexts.TryGetValue(key, out Text txt) && txt != null)
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