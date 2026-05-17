using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MasterUIManager : MonoBehaviour
{
    [Header("=== Auto Setup ===")]
    public bool autoGenerateUI = true;

    [Header("=== Panel Reference (手动赋值可跳过自动生成) ===")]
    public GameObject settingsPanel;

    private SimpleCarController carController;
    private SimpleAutoDrive autoDrive;
    private TrafficLightManager trafficLightManager;
    private ROS2BridgeV2 ros2Bridge;
    private CameraController cameraController;
    private TrafficManager trafficManager;
    private RoadNetworkGenerator roadGen;

    private bool isRebinding = false;
    private bool panelVisible = false;

    // --- Debug Info Section ---
    private Dictionary<string, Text> debugTexts = new Dictionary<string, Text>();
    private float debugRefreshInterval = 0.15f;
    private float debugRefreshTimer = 0f;

    // FPS 计算
    private float fpsAccum = 0f;
    private int fpsFrameCount = 0;
    private float currentFps = 0f;
    private float fpsRefreshInterval = 0.5f;
    private float fpsRefreshTimer = 0f;

    private static readonly Vector2 refResolution = new Vector2(1920, 1080);

    void Start()
    {
        if (autoGenerateUI && settingsPanel == null)
        {
            GenerateUIPanel();
        }

        if (settingsPanel == null)
        {
            Debug.LogWarning("[MasterUIManager] 未找到设置面板，UI 系统已静默禁用");
            return;
        }

        FindAllComponents();
        BindAllEvents();
        SyncUIFromComponents();
        panelVisible = true;
        settingsPanel.SetActive(true);

        if (RuntimeInputManager.Instance != null)
        {
            RuntimeInputManager.Instance.OnKeyRebound += OnKeyRebound;
        }
    }

    void Update()
    {
        // FPS 累积计算（不受面板开关影响，始终记录）
        fpsAccum += Time.unscaledDeltaTime;
        fpsFrameCount++;
        fpsRefreshTimer += Time.unscaledDeltaTime;
        if (fpsRefreshTimer >= fpsRefreshInterval)
        {
            currentFps = fpsFrameCount > 0 ? fpsFrameCount / fpsAccum : 0f;
            fpsAccum = 0f;
            fpsFrameCount = 0;
            fpsRefreshTimer = 0f;
        }

        if (RuntimeInputManager.Instance == null) return;

        if (!isRebinding && RuntimeInputManager.Instance.GetKeyDown("ToggleUI"))
        {
            TogglePanel();
        }

        if (RuntimeInputManager.Instance.GetKeyDown("ToggleAuto"))
        {
            ToggleAutoDrive();
        }

        if (RuntimeInputManager.Instance.GetKey("Brake") && carController != null)
        {
            carController.SetAutoBrake(carController.brakeDeceleration);
        }

        // 调试信息定时刷新（仅在面板可见时）
        if (panelVisible && settingsPanel != null)
        {
            debugRefreshTimer += Time.deltaTime;
            if (debugRefreshTimer >= debugRefreshInterval)
            {
                RefreshDebugInfo();
                debugRefreshTimer = 0f;
            }
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
    }

    #region Panel Toggle & Shortcuts

    void TogglePanel()
    {
        panelVisible = !panelVisible;
        if (settingsPanel != null) settingsPanel.SetActive(panelVisible);
    }

    void ToggleAutoDrive()
    {
        if (autoDrive != null)
        {
            autoDrive.ToggleAutoDrive();
        }
    }

    #endregion

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

        if (carController == null) Debug.LogWarning("[MasterUIManager] 未找到 SimpleCarController");
        if (autoDrive == null) Debug.LogWarning("[MasterUIManager] 未找到 SimpleAutoDrive");
        if (trafficLightManager == null) Debug.LogWarning("[MasterUIManager] 未找到 TrafficLightManager");
        if (ros2Bridge == null) Debug.LogWarning("[MasterUIManager] 未找到 ROS2BridgeV2");
        if (cameraController == null) Debug.LogWarning("[MasterUIManager] 未找到 CameraController");
        if (trafficManager == null) Debug.LogWarning("[MasterUIManager] 未找到 TrafficManager");
        if (roadGen == null) Debug.LogWarning("[MasterUIManager] 未找到 RoadNetworkGenerator");
    }

    #endregion

    #region Event Binding

    void BindAllEvents()
    {
        SliderWithLabel("MaxSpeedSlider", (v) => { if (carController) carController.maxSpeed = v; });
        SliderWithLabel("SteeringSlider", (v) => { if (carController) carController.maxSteeringAngle = v; });
        SliderWithLabel("BrakeDecelSlider", (v) => { if (carController) carController.brakeDeceleration = v; });
        SliderWithLabel("AccelerationSlider", (v) => { if (carController) carController.acceleration = v; });

        SliderWithLabel("GreenDurationSlider", OnGreenDurationChanged);
        SliderWithLabel("RedDurationSlider", OnRedDurationChanged);
        SliderWithLabel("YellowDurationSlider", OnYellowDurationChanged);

        BindButton("ReconnectBtn", OnReconnectROS2);
        BindInputField("RosIPInput", OnRosIPChanged);
        BindInputField("RosPortInput", OnRosPortChanged);

        BindKeybindRow("BrakeKeyRow", "Brake");
        BindKeybindRow("ToggleAutoKeyRow", "ToggleAuto");
        BindKeybindRow("SwitchCamKeyRow", "SwitchCam");
        BindKeybindRow("ToggleUIKeyRow", "ToggleUI");

        BindButton("ResetKeysBtn", OnResetAllKeys);
        BindButton("ClosePanelBtn", () => { panelVisible = false; if (settingsPanel) settingsPanel.SetActive(false); });
    }

    void SliderWithLabel(string sliderName, System.Action<float> callback)
    {
        Transform t = settingsPanel.transform.Find(sliderName);
        if (t == null) return;
        Slider slider = t.GetComponent<Slider>();
        if (slider == null) return;

        Transform valTransform = t.Find("ValueText");
        Text valueText = valTransform != null ? valTransform.GetComponent<Text>() : null;

        slider.onValueChanged.AddListener((v) =>
        {
            callback(v);
            if (valueText != null) valueText.text = v.ToString("F0");
        });
    }

    void BindButton(string buttonName, System.Action callback)
    {
        Transform t = settingsPanel.transform.Find(buttonName);
        if (t == null) return;
        Button btn = t.GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(() => callback());
    }

    void BindInputField(string inputName, UnityEngine.Events.UnityAction<string> callback)
    {
        Transform t = settingsPanel.transform.Find(inputName);
        if (t == null) return;
        InputField input = t.GetComponent<InputField>();
        if (input != null) input.onEndEdit.AddListener(callback);
    }

    void BindKeybindRow(string rowName, string actionName)
    {
        Transform t = settingsPanel.transform.Find(rowName);
        if (t == null) return;

        Transform btnTransform = t.Find("RebindBtn");
        Transform txtTransform = t.Find("KeyText");

        if (btnTransform == null || txtTransform == null) return;

        Button rebindBtn = btnTransform.GetComponent<Button>();
        Text keyText = txtTransform.GetComponent<Text>();

        if (rebindBtn == null || keyText == null) return;

        if (RuntimeInputManager.Instance != null)
        {
            keyText.text = RuntimeInputManager.Instance.GetKeyCode(actionName).ToString();
        }

        rebindBtn.onClick.AddListener(() =>
        {
            if (!isRebinding && RuntimeInputManager.Instance != null)
            {
                isRebinding = true;
                StartCoroutine(RebindAndRestore(actionName, keyText));
            }
        });
    }

    IEnumerator RebindAndRestore(string actionName, Text keyText)
    {
        yield return StartCoroutine(RuntimeInputManager.Instance.RebindKey(actionName, keyText));
        isRebinding = false;
    }

    #endregion

    #region Slider Callbacks

    void OnGreenDurationChanged(float value)
    {
        if (trafficLightManager != null) trafficLightManager.greenDuration = value;
        foreach (var ctrl in FindObjectsOfType<TrafficLightController>())
            ctrl.greenDuration = value;
    }

    void OnRedDurationChanged(float value)
    {
        if (trafficLightManager != null) trafficLightManager.redDuration = value;
        foreach (var ctrl in FindObjectsOfType<TrafficLightController>())
            ctrl.redDuration = value;
    }

    void OnYellowDurationChanged(float value)
    {
        if (trafficLightManager != null) trafficLightManager.yellowDuration = value;
        foreach (var ctrl in FindObjectsOfType<TrafficLightController>())
            ctrl.yellowDuration = value;
    }

    #endregion

    #region ROS2 Callbacks

    void OnRosIPChanged(string value)
    {
        if (ros2Bridge != null && !string.IsNullOrWhiteSpace(value))
        {
            ros2Bridge.rosIP = value.Trim();
            Debug.Log($"[MasterUIManager] ROS2 IP 已更新: {ros2Bridge.rosIP}");
        }
    }

    void OnRosPortChanged(string value)
    {
        if (ros2Bridge != null && int.TryParse(value, out int port) && port > 0 && port < 65536)
        {
            ros2Bridge.rosPort = port;
            Debug.Log($"[MasterUIManager] ROS2 Port 已更新: {ros2Bridge.rosPort}");
        }
    }

    void OnReconnectROS2()
    {
        if (ros2Bridge != null)
        {
            ros2Bridge.Reconnect();
        }
        else
        {
            Debug.LogWarning("[MasterUIManager] 未找到 ROS2BridgeV2 组件，无法重连");
        }
    }

    #endregion

    #region Key Reset

    void OnResetAllKeys()
    {
        if (RuntimeInputManager.Instance != null)
        {
            RuntimeInputManager.Instance.ResetAllToDefault();
            RefreshAllKeyTexts();
        }
    }

    void RefreshAllKeyTexts()
    {
        if (RuntimeInputManager.Instance == null || settingsPanel == null) return;

        RefreshKeyText("BrakeKeyRow", "Brake");
        RefreshKeyText("ToggleAutoKeyRow", "ToggleAuto");
        RefreshKeyText("SwitchCamKeyRow", "SwitchCam");
        RefreshKeyText("ToggleUIKeyRow", "ToggleUI");
    }

    void RefreshKeyText(string rowName, string actionName)
    {
        Transform t = settingsPanel.transform.Find(rowName);
        if (t == null) return;
        Transform txtTransform = t.Find("KeyText");
        if (txtTransform == null) return;
        Text keyText = txtTransform.GetComponent<Text>();
        if (keyText != null)
        {
            keyText.text = RuntimeInputManager.Instance.GetKeyCode(actionName).ToString();
        }
    }

    #endregion

    #region Sync UI from Components

    void SyncUIFromComponents()
    {
        SyncSlider("MaxSpeedSlider", carController != null ? carController.maxSpeed : 30f);
        SyncSlider("SteeringSlider", carController != null ? carController.maxSteeringAngle : 45f);
        SyncSlider("BrakeDecelSlider", carController != null ? carController.brakeDeceleration : 10f);
        SyncSlider("AccelerationSlider", carController != null ? carController.acceleration : 4f);
        SyncSlider("GreenDurationSlider", trafficLightManager != null ? trafficLightManager.greenDuration : 8f);
        SyncSlider("RedDurationSlider", trafficLightManager != null ? trafficLightManager.redDuration : 8f);
        SyncSlider("YellowDurationSlider", trafficLightManager != null ? trafficLightManager.yellowDuration : 2f);

        SyncInputField("RosIPInput", ros2Bridge != null ? ros2Bridge.rosIP : "172.21.16.202");
        SyncInputField("RosPortInput", ros2Bridge != null ? ros2Bridge.rosPort.ToString() : "10086");
    }

    void SyncSlider(string name, float value)
    {
        if (settingsPanel == null) return;
        Transform t = settingsPanel.transform.Find(name);
        if (t == null) return;
        Slider slider = t.GetComponent<Slider>();
        if (slider != null) slider.value = value;

        Transform valTransform = t.Find("ValueText");
        if (valTransform != null)
        {
            Text valueText = valTransform.GetComponent<Text>();
            if (valueText != null) valueText.text = value.ToString("F0");
        }
    }

    void SyncInputField(string name, string value)
    {
        if (settingsPanel == null) return;
        Transform t = settingsPanel.transform.Find(name);
        if (t == null) return;
        InputField input = t.GetComponent<InputField>();
        if (input != null) input.text = value;
    }

    #endregion

    #region Debug Info

    /// <summary>
    /// 刷新所有调试信息只读字段（定时触发，约150ms间隔）
    /// 使用 FindObjectOfType 容错，找不到静默跳过
    /// </summary>
    void RefreshDebugInfo()
    {
        if (settingsPanel == null) return;

        // --- FPS ---
        SetDebugValue("DbgFPS", currentFps.ToString("F1"));

        // --- SimpleCarController ---
        // 注意：carController 已在 FindAllComponents 中缓存，但可能为 null
        // 也实时查找确保主车数据（因为可能有多个控制器，第一个为主车）
        SimpleCarController cc = carController;
        if (cc == null) { cc = FindObjectOfType<SimpleCarController>(); if (cc != null) carController = cc; }

        SetDebugValue("DbgSpeed", cc != null ? cc.currentSpeed.ToString("F1") + " m/s" : "N/A");
        SetDebugValue("DbgSteering", cc != null ? cc.currentSteeringAngle.ToString("F1") + " deg" : "N/A");
        SetDebugValue("DbgAutoMode", cc != null ? (cc.autoMode ? "Yes" : "No") : "N/A");
        SetDebugValue("DbgIsNPC", cc != null ? (cc.isNPC ? "Yes" : "No") : "N/A");

        // --- SimpleAutoDrive ---
        SimpleAutoDrive ad = autoDrive;
        if (ad == null) { ad = FindObjectOfType<SimpleAutoDrive>(); if (ad != null) autoDrive = ad; }

        SetDebugValue("DbgTargetSpeed", ad != null ? ad.targetSpeed.ToString("F1") : "N/A");
        SetDebugValue("DbgSafeDist", ad != null ? ad.safeDistance.ToString("F1") : "N/A");
        SetDebugValue("DbgDriveState", ad != null ? ad.currentState.ToString() : "N/A");
        SetDebugValue("DbgLaneId", ad != null ? ad.currentLaneId.ToString() : "N/A");
        SetDebugValue("DbgIntersection", ad != null ? ad.currentIntersectionState.ToString() : "N/A");
        SetDebugValue("DbgObstacle", ad != null ? (ad.obstacleDetected ? "Yes" : "No") : "N/A");
        SetDebugValue("DbgCurrentT", ad != null ? ad.currentT.ToString("F2") : "N/A");

        // --- TrafficManager ---
        TrafficManager tm = trafficManager;
        if (tm == null) { tm = FindObjectOfType<TrafficManager>(); if (tm != null) trafficManager = tm; }

        if (tm != null)
        {
            SetDebugValue("DbgNPCCount", tm.npcCount.ToString());
            // 私有字段 _hasSpawned 通过反射获取
            bool hasSpawned = false;
            try
            {
                var field = tm.GetType().GetField("_hasSpawned",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) hasSpawned = (bool)field.GetValue(tm);
            }
            catch { }
            SetDebugValue("DbgHasSpawned", hasSpawned ? "Yes" : "No");
        }
        else
        {
            SetDebugValue("DbgNPCCount", "N/A");
            SetDebugValue("DbgHasSpawned", "N/A");
        }

        // --- WorldModel ---
        WorldModel wm = WorldModel.Instance;
        if (wm != null)
        {
            SetDebugValue("DbgNodeCount", wm.NodeCount.ToString());
            SetDebugValue("DbgLaneCount", wm.GlobalLanes != null ? wm.GlobalLanes.Count.ToString() : "0");
            bool isCountry = false;
            if (wm.roadGenerator != null) isCountry = wm.roadGenerator.isCountryside;
            else if (roadGen != null) isCountry = roadGen.isCountryside;
            SetDebugValue("DbgWorldMode", isCountry ? "Countryside" : "City");
        }
        else
        {
            SetDebugValue("DbgNodeCount", "N/A");
            SetDebugValue("DbgLaneCount", "N/A");
            SetDebugValue("DbgWorldMode", "N/A");
        }

        // --- ROS2BridgeV2 ---
        ROS2BridgeV2 r2 = ros2Bridge;
        if (r2 == null) { r2 = FindObjectOfType<ROS2BridgeV2>(); if (r2 != null) ros2Bridge = r2; }

        if (r2 != null)
        {
            SetDebugValue("DbgROSConnected", r2.isConnected ? "Yes" : "No");
            // 私有字段 useRosControl 通过反射获取
            bool rosCtrl = false;
            try
            {
                var field = r2.GetType().GetField("useRosControl",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) rosCtrl = (bool)field.GetValue(r2);
            }
            catch { }
            SetDebugValue("DbgROSControl", rosCtrl ? "Active" : "Idle");
        }
        else
        {
            SetDebugValue("DbgROSConnected", "N/A");
            SetDebugValue("DbgROSControl", "N/A");
        }

        // --- CameraController ---
        CameraController cam = cameraController;
        if (cam == null) { cam = FindObjectOfType<CameraController>(); if (cam != null) cameraController = cam; }

        SetDebugValue("DbgCamModeKey", cam != null ? cam.modeSwitchKey.ToString() : "N/A");
        SetDebugValue("DbgCamTargetKey", cam != null ? cam.targetSwitchKey.ToString() : "N/A");
    }

    /// <summary>
    /// 安全设置调试文本值（找不到 Text 组件则静默跳过）
    /// </summary>
    void SetDebugValue(string rowName, string value)
    {
        if (settingsPanel == null) return;

        // 先从缓存查找
        Text txt;
        if (!debugTexts.TryGetValue(rowName, out txt))
        {
            Transform row = settingsPanel.transform.Find(rowName);
            if (row == null) return;
            Transform valTransform = row.Find("ValueText");
            if (valTransform == null) return;
            txt = valTransform.GetComponent<Text>();
            if (txt == null) return;
            debugTexts[rowName] = txt;
        }

        if (txt != null) txt.text = value;
    }

    #endregion

    #region UI Auto-Generation

    [ContextMenu("Generate UI Panel")]
    public void GenerateUIPanel()
    {
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

            canvasGO.AddComponent<GraphicRaycaster>();
        }

        Transform existing = canvas.transform.Find("MasterSettingsPanel");
        if (existing != null)
        {
            if (Application.isPlaying)
                Destroy(existing.gameObject);
            else
                DestroyImmediate(existing.gameObject);
        }

        GameObject panelGO = new GameObject("MasterSettingsPanel");
        panelGO.transform.SetParent(canvas.transform, false);

        RectTransform panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1, 1);
        panelRT.anchorMax = new Vector2(1, 1);
        panelRT.pivot = new Vector2(1, 1);
        panelRT.anchoredPosition = new Vector2(-20, -20);
        panelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 340);

        Image bgImage = panelGO.AddComponent<Image>();
        bgImage.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        VerticalLayoutGroup vlg = panelGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 12, 12);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = panelGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateTitle(panelGO, "A5 Master Control Panel");

        CreateSectionHeader(panelGO, "--- Vehicle Control ---");
        CreateSliderRow(panelGO, "MaxSpeedSlider", "Max Speed", 5f, 100f, 30f, "F0");
        CreateSliderRow(panelGO, "SteeringSlider", "Steering Angle", 10f, 90f, 45f, "F0");
        CreateSliderRow(panelGO, "BrakeDecelSlider", "Brake Force", 1f, 30f, 10f, "F0");
        CreateSliderRow(panelGO, "AccelerationSlider", "Acceleration", 1f, 15f, 4f, "F0");

        CreateSectionHeader(panelGO, "--- Traffic Light ---");
        CreateSliderRow(panelGO, "GreenDurationSlider", "Green (s)", 3f, 60f, 8f, "F0");
        CreateSliderRow(panelGO, "RedDurationSlider", "Red (s)", 3f, 60f, 8f, "F0");
        CreateSliderRow(panelGO, "YellowDurationSlider", "Yellow (s)", 1f, 5f, 2f, "F0");

        CreateSectionHeader(panelGO, "--- ROS2 Connection ---");
        CreateInputRow(panelGO, "RosIPInput", "IP", "172.21.16.202", InputField.ContentType.Standard);
        CreateInputRow(panelGO, "RosPortInput", "Port", "10086", InputField.ContentType.IntegerNumber);
        CreateButton(panelGO, "ReconnectBtn", "Reconnect");

        CreateSectionHeader(panelGO, "--- Key Bindings ---");
        CreateKeybindRow(panelGO, "BrakeKeyRow", "Brake", "Space");
        CreateKeybindRow(panelGO, "ToggleAutoKeyRow", "Toggle Auto", "T");
        CreateKeybindRow(panelGO, "SwitchCamKeyRow", "Switch Cam", "C");
        CreateKeybindRow(panelGO, "ToggleUIKeyRow", "Toggle UI", "Escape");
        CreateButton(panelGO, "ResetKeysBtn", "Reset All Keys");

        CreateButton(panelGO, "ClosePanelBtn", "Close Panel [Esc]");

        // 提示文字
        CreateHintText(panelGO, "按 ESC 切换面板  |  T 切换自动驾驶  |  N 重置AI导航");

        CreateSectionHeader(panelGO, "--- Debug Info ---");
        CreateDebugRow(panelGO, "DbgFPS", "FPS", "0.0");
        CreateSectionHeaderSub(panelGO, "-- Car Controller --");
        CreateDebugRow(panelGO, "DbgSpeed", "Speed", "0.0 m/s");
        CreateDebugRow(panelGO, "DbgSteering", "Steering", "0.0 deg");
        CreateDebugRow(panelGO, "DbgAutoMode", "Auto Mode", "No");
        CreateDebugRow(panelGO, "DbgIsNPC", "Is NPC", "No");
        CreateSectionHeaderSub(panelGO, "-- Auto Drive --");
        CreateDebugRow(panelGO, "DbgTargetSpeed", "Target Spd", "N/A");
        CreateDebugRow(panelGO, "DbgSafeDist", "Safe Dist", "N/A");
        CreateDebugRow(panelGO, "DbgDriveState", "Drive State", "N/A");
        CreateDebugRow(panelGO, "DbgLaneId", "Lane ID", "N/A");
        CreateDebugRow(panelGO, "DbgIntersection", "Intersection", "N/A");
        CreateDebugRow(panelGO, "DbgObstacle", "Obstacle", "N/A");
        CreateDebugRow(panelGO, "DbgCurrentT", "Current T", "N/A");
        CreateSectionHeaderSub(panelGO, "-- Traffic Mgr --");
        CreateDebugRow(panelGO, "DbgNPCCount", "NPC Count", "N/A");
        CreateDebugRow(panelGO, "DbgHasSpawned", "Has Spawned", "N/A");
        CreateSectionHeaderSub(panelGO, "-- World Model --");
        CreateDebugRow(panelGO, "DbgNodeCount", "Nodes", "N/A");
        CreateDebugRow(panelGO, "DbgLaneCount", "Lanes", "N/A");
        CreateDebugRow(panelGO, "DbgWorldMode", "Mode", "N/A");
        CreateSectionHeaderSub(panelGO, "-- ROS2 Bridge --");
        CreateDebugRow(panelGO, "DbgROSConnected", "Connected", "N/A");
        CreateDebugRow(panelGO, "DbgROSControl", "ROS Ctrl", "N/A");
        CreateSectionHeaderSub(panelGO, "-- Camera --");
        CreateDebugRow(panelGO, "DbgCamModeKey", "Mode Key", "N/A");
        CreateDebugRow(panelGO, "DbgCamTargetKey", "Target Key", "N/A");

        settingsPanel = panelGO;

        Debug.Log("[MasterUIManager] UI 面板已自动生成在 Canvas 下");
    }

    GameObject CreateTitle(GameObject parent, string text)
    {
        GameObject go = new GameObject("Title");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 30;
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 18;
        txt.fontStyle = FontStyle.Bold;
        txt.color = new Color(0.3f, 0.8f, 1f);
        txt.alignment = TextAnchor.MiddleCenter;
        return go;
    }

    GameObject CreateSectionHeader(GameObject parent, string text)
    {
        GameObject go = new GameObject("Header_" + text.GetHashCode());
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 24;
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 13;
        txt.fontStyle = FontStyle.Bold;
        txt.color = new Color(0.6f, 0.6f, 0.7f);
        txt.alignment = TextAnchor.MiddleLeft;
        return go;
    }

    GameObject CreateSliderRow(GameObject parent, string name, string label, float min, float max, float defaultValue, string format)
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
        hlg.spacing = 6;

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        Text labelTxt = labelGO.AddComponent<Text>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAnchor.MiddleLeft;
        labelGO.AddComponent<LayoutElement>().minWidth = 90;

        GameObject sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(row.transform, false);
        Slider slider = sliderGO.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = defaultValue;
        sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        RectTransform sliderRT = sliderGO.GetComponent<RectTransform>();
        sliderRT.sizeDelta = new Vector2(0, 20);

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.25f);
        slider.targetGraphic = bgImg;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(sliderGO.transform, false);
        Image fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.3f, 0.7f, 1f);
        RectTransform fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;
        slider.fillRect = fillRT;
        slider.targetGraphic = fillImg;

        GameObject handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(sliderGO.transform, false);
        Image handleImg = handleGO.AddComponent<Image>();
        handleImg.color = new Color(0.9f, 0.9f, 0.95f);
        RectTransform handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(12, 18);
        slider.handleRect = handleRT;

        GameObject valGO = new GameObject("ValueText");
        valGO.transform.SetParent(row.transform, false);
        Text valTxt = valGO.AddComponent<Text>();
        valTxt.text = defaultValue.ToString(format);
        valTxt.font = GetDefaultFont();
        valTxt.fontSize = 13;
        valTxt.color = new Color(0.3f, 0.8f, 1f);
        valTxt.alignment = TextAnchor.MiddleRight;
        valGO.AddComponent<LayoutElement>().minWidth = 36;

        return row;
    }

    GameObject CreateInputRow(GameObject parent, string name, string placeholder, string defaultValue, InputField.ContentType contentType)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 30;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 6;

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        Text labelTxt = labelGO.AddComponent<Text>();
        labelTxt.text = placeholder;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAnchor.MiddleLeft;
        labelGO.AddComponent<LayoutElement>().minWidth = 50;

        GameObject inputGO = new GameObject("Input");
        inputGO.transform.SetParent(row.transform, false);
        InputField input = inputGO.AddComponent<InputField>();
        input.text = defaultValue;
        input.contentType = contentType;
        inputGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(inputGO.transform, false);
        Text inputText = textGO.AddComponent<Text>();
        inputText.text = defaultValue;
        inputText.font = GetDefaultFont();
        inputText.fontSize = 13;
        inputText.color = Color.black;
        inputText.alignment = TextAnchor.MiddleLeft;
        inputText.supportRichText = false;
        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;
        input.textComponent = inputText;

        GameObject placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(inputGO.transform, false);
        Text placeholderTxt = placeholderGO.AddComponent<Text>();
        placeholderTxt.text = placeholder;
        placeholderTxt.font = GetDefaultFont();
        placeholderTxt.fontSize = 13;
        placeholderTxt.fontStyle = FontStyle.Italic;
        placeholderTxt.color = new Color(0.5f, 0.5f, 0.5f);
        placeholderTxt.alignment = TextAnchor.MiddleLeft;
        RectTransform phRT = placeholderGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.sizeDelta = Vector2.zero;
        input.placeholder = placeholderTxt;

        Image inputBg = inputGO.AddComponent<Image>();
        inputBg.color = Color.white;

        return row;
    }

    GameObject CreateKeybindRow(GameObject parent, string name, string label, string defaultKey)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 28;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 6;

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        Text labelTxt = labelGO.AddComponent<Text>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAnchor.MiddleLeft;
        labelGO.AddComponent<LayoutElement>().minWidth = 75;

        GameObject keyGO = new GameObject("KeyText");
        keyGO.transform.SetParent(row.transform, false);
        Text keyTxt = keyGO.AddComponent<Text>();
        keyTxt.text = defaultKey;
        keyTxt.font = GetDefaultFont();
        keyTxt.fontSize = 13;
        keyTxt.fontStyle = FontStyle.Bold;
        keyTxt.color = new Color(1f, 0.85f, 0.2f);
        keyTxt.alignment = TextAnchor.MiddleCenter;
        keyGO.AddComponent<LayoutElement>().minWidth = 70;

        GameObject btnGO = new GameObject("RebindBtn");
        btnGO.transform.SetParent(row.transform, false);
        Button btn = btnGO.AddComponent<Button>();
        btnGO.AddComponent<LayoutElement>().minWidth = 80;
        btnGO.AddComponent<LayoutElement>().minHeight = 24;

        Image btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.25f, 0.25f, 0.35f);
        btn.targetGraphic = btnImg;

        GameObject btnTextGO = new GameObject("Text");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        Text btnTxt = btnTextGO.AddComponent<Text>();
        btnTxt.text = "Rebind";
        btnTxt.font = GetDefaultFont();
        btnTxt.fontSize = 12;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform btnTxtRT = btnTextGO.GetComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero;
        btnTxtRT.anchorMax = Vector2.one;
        btnTxtRT.sizeDelta = Vector2.zero;

        return row;
    }

    GameObject CreateButton(GameObject parent, string name, string label)
    {
        GameObject btnGO = new GameObject(name);
        btnGO.transform.SetParent(parent.transform, false);
        btnGO.AddComponent<LayoutElement>().minHeight = 32;

        Button btn = btnGO.AddComponent<Button>();
        Image btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.4f, 0.6f);
        btn.targetGraphic = btnImg;

        GameObject btnTextGO = new GameObject("Text");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        Text btnTxt = btnTextGO.AddComponent<Text>();
        btnTxt.text = label;
        btnTxt.font = GetDefaultFont();
        btnTxt.fontSize = 14;
        btnTxt.fontStyle = FontStyle.Bold;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform btnTxtRT = btnTextGO.GetComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero;
        btnTxtRT.anchorMax = Vector2.one;
        btnTxtRT.sizeDelta = Vector2.zero;

        return btnGO;
    }

    /// <summary>
    /// 创建只读调试信息行：左侧标签 + 右侧动态值（由 RefreshDebugInfo 定时更新）
    /// </summary>
    GameObject CreateDebugRow(GameObject parent, string name, string label, string defaultVal)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        row.AddComponent<LayoutElement>().minHeight = 22;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 4;

        // 标签（左侧，偏灰白）
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        Text labelTxt = labelGO.AddComponent<Text>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 11;
        labelTxt.color = new Color(0.65f, 0.65f, 0.7f);
        labelTxt.alignment = TextAnchor.MiddleLeft;
        labelGO.AddComponent<LayoutElement>().minWidth = 80;

        // 值（右侧，亮色高亮）
        GameObject valGO = new GameObject("ValueText");
        valGO.transform.SetParent(row.transform, false);
        Text valTxt = valGO.AddComponent<Text>();
        valTxt.text = defaultVal;
        valTxt.font = GetDefaultFont();
        valTxt.fontSize = 11;
        valTxt.fontStyle = FontStyle.Bold;
        valTxt.color = new Color(0.4f, 0.9f, 0.6f);
        valTxt.alignment = TextAnchor.MiddleRight;
        valGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return row;
    }

    /// <summary>
    /// 创建子节标题（字体更小、颜色更淡，用于分组调试字段）
    /// </summary>
    GameObject CreateSectionHeaderSub(GameObject parent, string text)
    {
        GameObject go = new GameObject("SubHeader_" + text.GetHashCode());
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 18;
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 10;
        txt.fontStyle = FontStyle.Italic;
        txt.color = new Color(0.45f, 0.45f, 0.5f);
        txt.alignment = TextAnchor.MiddleLeft;
        return go;
    }

    /// <summary>
    /// 创建提示文字行（小号、灰色、居中）
    /// </summary>
    GameObject CreateHintText(GameObject parent, string text)
    {
        GameObject go = new GameObject("HintText");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 20;
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 11;
        txt.fontStyle = FontStyle.Normal;
        txt.color = new Color(0.5f, 0.5f, 0.55f);
        txt.alignment = TextAnchor.MiddleCenter;
        return go;
    }

    Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    #endregion
}