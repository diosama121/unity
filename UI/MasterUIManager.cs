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

        UIPanelBuilder.CreateTitle(panelGO, "A5 Master Control Panel");

        UIPanelBuilder.CreateSectionHeader(panelGO, "--- Vehicle Control ---");
        UIPanelBuilder.CreateSliderRow(panelGO, "MaxSpeedSlider", "Max Speed", 5f, 100f, 30f, "F0");
        UIPanelBuilder.CreateSliderRow(panelGO, "SteeringSlider", "Steering Angle", 10f, 90f, 45f, "F0");
        UIPanelBuilder.CreateSliderRow(panelGO, "BrakeDecelSlider", "Brake Force", 1f, 30f, 10f, "F0");
        UIPanelBuilder.CreateSliderRow(panelGO, "AccelerationSlider", "Acceleration", 1f, 15f, 4f, "F0");

        UIPanelBuilder.CreateSectionHeader(panelGO, "--- Traffic Light ---");
        UIPanelBuilder.CreateSliderRow(panelGO, "GreenDurationSlider", "Green (s)", 3f, 60f, 8f, "F0");
        UIPanelBuilder.CreateSliderRow(panelGO, "RedDurationSlider", "Red (s)", 3f, 60f, 8f, "F0");
        UIPanelBuilder.CreateSliderRow(panelGO, "YellowDurationSlider", "Yellow (s)", 1f, 5f, 2f, "F0");

        UIPanelBuilder.CreateSectionHeader(panelGO, "--- ROS2 Connection ---");
        UIPanelBuilder.CreateInputRow(panelGO, "RosIPInput", "IP", "172.21.16.202", InputField.ContentType.Standard);
        UIPanelBuilder.CreateInputRow(panelGO, "RosPortInput", "Port", "10086", InputField.ContentType.IntegerNumber);
        UIPanelBuilder.CreateButton(panelGO, "ReconnectBtn", "Reconnect");

        UIPanelBuilder.CreateSectionHeader(panelGO, "--- Key Bindings ---");
        UIPanelBuilder.CreateKeybindRow(panelGO, "BrakeKeyRow", "Brake", "Space");
        UIPanelBuilder.CreateKeybindRow(panelGO, "ToggleAutoKeyRow", "Toggle Auto", "T");
        UIPanelBuilder.CreateKeybindRow(panelGO, "SwitchCamKeyRow", "Switch Cam", "C");
        UIPanelBuilder.CreateKeybindRow(panelGO, "ToggleUIKeyRow", "Toggle UI", "Escape");
        UIPanelBuilder.CreateButton(panelGO, "ResetKeysBtn", "Reset All Keys");

        UIPanelBuilder.CreateButton(panelGO, "ClosePanelBtn", "Close Panel [Esc]");

        // 提示文字
        UIPanelBuilder.CreateHintText(panelGO, "按 ESC 切换面板  |  T 切换自动驾驶  |  N 重置AI导航");

        UIPanelBuilder.CreateSectionHeader(panelGO, "--- Debug Info ---");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgFPS", "FPS", "0.0");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- Car Controller --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgSpeed", "Speed", "0.0 m/s");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgSteering", "Steering", "0.0 deg");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgAutoMode", "Auto Mode", "No");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgIsNPC", "Is NPC", "No");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- Auto Drive --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgTargetSpeed", "Target Spd", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgSafeDist", "Safe Dist", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgDriveState", "Drive State", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgLaneId", "Lane ID", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgIntersection", "Intersection", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgObstacle", "Obstacle", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgCurrentT", "Current T", "N/A");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- Traffic Mgr --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgNPCCount", "NPC Count", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgHasSpawned", "Has Spawned", "N/A");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- World Model --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgNodeCount", "Nodes", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgLaneCount", "Lanes", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgWorldMode", "Mode", "N/A");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- ROS2 Bridge --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgROSConnected", "Connected", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgROSControl", "ROS Ctrl", "N/A");
        UIPanelBuilder.CreateSectionHeaderSub(panelGO, "-- Camera --");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgCamModeKey", "Mode Key", "N/A");
        UIPanelBuilder.CreateDebugRow(panelGO, "DbgCamTargetKey", "Target Key", "N/A");

        settingsPanel = panelGO;

        Debug.Log("[MasterUIManager] UI 面板已自动生成在 Canvas 下");
    }

    #endregion
}