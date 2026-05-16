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

    private bool isRebinding = false;
    private bool panelVisible = false;

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
        settingsPanel.SetActive(false);

        if (RuntimeInputManager.Instance != null)
        {
            RuntimeInputManager.Instance.OnKeyRebound += OnKeyRebound;
        }
    }

    void Update()
    {
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

        if (carController == null) Debug.LogWarning("[MasterUIManager] 未找到 SimpleCarController");
        if (autoDrive == null) Debug.LogWarning("[MasterUIManager] 未找到 SimpleAutoDrive");
        if (trafficLightManager == null) Debug.LogWarning("[MasterUIManager] 未找到 TrafficLightManager");
        if (ros2Bridge == null) Debug.LogWarning("[MasterUIManager] 未找到 ROS2BridgeV2");
        if (cameraController == null) Debug.LogWarning("[MasterUIManager] 未找到 CameraController");
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

    void BindInputField(string inputName, System.Action<string> callback)
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

    Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    #endregion
}