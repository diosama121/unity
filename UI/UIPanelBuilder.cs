using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class UIPanelBuilder
{
    public static TMP_FontAsset SharedFont { get; set; }

    public static GameObject CreateTitle(GameObject parent, string text)
    {
        GameObject go = new GameObject("Title");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 30;
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 18;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 10;
        txt.fontSizeMax = 18;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(0.3f, 0.8f, 1f);
        txt.alignment = TextAlignmentOptions.Center;
        return go;
    }

    public static GameObject CreateSectionHeader(GameObject parent, string text)
    {
        GameObject go = new GameObject("Header_" + text.GetHashCode());
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 24;
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 13;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 8;
        txt.fontSizeMax = 13;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(0.6f, 0.6f, 0.7f);
        txt.alignment = TextAlignmentOptions.Left;
        return go;
    }

    public static GameObject CreateSliderRow(GameObject parent, string name, string label, float min, float max, float defaultValue, string format)
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
        TextMeshProUGUI labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.enableAutoSizing = true;
        labelTxt.fontSizeMin = 8;
        labelTxt.fontSizeMax = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAlignmentOptions.Left;
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
        TextMeshProUGUI valTxt = valGO.AddComponent<TextMeshProUGUI>();
        valTxt.text = defaultValue.ToString(format);
        valTxt.font = GetDefaultFont();
        valTxt.fontSize = 13;
        valTxt.enableAutoSizing = true;
        valTxt.fontSizeMin = 7;
        valTxt.fontSizeMax = 13;
        valTxt.color = new Color(0.3f, 0.8f, 1f);
        valTxt.alignment = TextAlignmentOptions.Right;
        valGO.AddComponent<LayoutElement>().minWidth = 36;

        return row;
    }

    public static GameObject CreateInputRow(GameObject parent, string name, string placeholder, string defaultValue, InputField.ContentType contentType)
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
        TextMeshProUGUI labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        labelTxt.text = placeholder;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.enableAutoSizing = true;
        labelTxt.fontSizeMin = 8;
        labelTxt.fontSizeMax = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAlignmentOptions.Left;
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
        inputText.font = Resources.Load<Font>("NotoSansSC-Regular");
        inputText.fontSize = 13;
        inputText.resizeTextForBestFit = true;
        inputText.resizeTextMinSize = 8;
        inputText.resizeTextMaxSize = 13;
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
        placeholderTxt.font = Resources.Load<Font>("NotoSansSC-Regular");
        placeholderTxt.fontSize = 13;
        placeholderTxt.resizeTextForBestFit = true;
        placeholderTxt.resizeTextMinSize = 8;
        placeholderTxt.resizeTextMaxSize = 13;
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

    public static GameObject CreateKeybindRow(GameObject parent, string name, string label, string defaultKey)
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
        TextMeshProUGUI labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 13;
        labelTxt.enableAutoSizing = true;
        labelTxt.fontSizeMin = 8;
        labelTxt.fontSizeMax = 13;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelGO.AddComponent<LayoutElement>().minWidth = 75;

        GameObject keyGO = new GameObject("KeyText");
        keyGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI keyTxt = keyGO.AddComponent<TextMeshProUGUI>();
        keyTxt.text = defaultKey;
        keyTxt.font = GetDefaultFont();
        keyTxt.fontSize = 13;
        keyTxt.enableAutoSizing = true;
        keyTxt.fontSizeMin = 7;
        keyTxt.fontSizeMax = 13;
        keyTxt.fontStyle = FontStyles.Bold;
        keyTxt.color = new Color(1f, 0.85f, 0.2f);
        keyTxt.alignment = TextAlignmentOptions.Center;
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
        TextMeshProUGUI btnTxt = btnTextGO.AddComponent<TextMeshProUGUI>();
        btnTxt.text = "Rebind";
        btnTxt.font = GetDefaultFont();
        btnTxt.fontSize = 12;
        btnTxt.enableAutoSizing = true;
        btnTxt.fontSizeMin = 7;
        btnTxt.fontSizeMax = 12;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAlignmentOptions.Center;
        RectTransform btnTxtRT = btnTextGO.GetComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero;
        btnTxtRT.anchorMax = Vector2.one;
        btnTxtRT.sizeDelta = Vector2.zero;

        return row;
    }

    public static GameObject CreateButton(GameObject parent, string name, string label)
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
        TextMeshProUGUI btnTxt = btnTextGO.AddComponent<TextMeshProUGUI>();
        btnTxt.text = label;
        btnTxt.font = GetDefaultFont();
        btnTxt.fontSize = 14;
        btnTxt.enableAutoSizing = true;
        btnTxt.fontSizeMin = 8;
        btnTxt.fontSizeMax = 14;
        btnTxt.fontStyle = FontStyles.Bold;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAlignmentOptions.Center;
        RectTransform btnTxtRT = btnTextGO.GetComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero;
        btnTxtRT.anchorMax = Vector2.one;
        btnTxtRT.sizeDelta = Vector2.zero;

        return btnGO;
    }

    /// <summary>
    /// 创建只读调试信息行：左侧标签 + 右侧动态值（由 RefreshDebugInfo 定时更新）
    /// </summary>
    public static GameObject CreateDebugRow(GameObject parent, string name, string label, string defaultVal)
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
        TextMeshProUGUI labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.font = GetDefaultFont();
        labelTxt.fontSize = 11;
        labelTxt.enableAutoSizing = true;
        labelTxt.fontSizeMin = 7;
        labelTxt.fontSizeMax = 11;
        labelTxt.color = new Color(0.65f, 0.65f, 0.7f);
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelGO.AddComponent<LayoutElement>().minWidth = 80;

        GameObject valGO = new GameObject("ValueText");
        valGO.transform.SetParent(row.transform, false);
        TextMeshProUGUI valTxt = valGO.AddComponent<TextMeshProUGUI>();
        valTxt.text = defaultVal;
        valTxt.font = GetDefaultFont();
        valTxt.fontSize = 11;
        valTxt.enableAutoSizing = true;
        valTxt.fontSizeMin = 7;
        valTxt.fontSizeMax = 11;
        valTxt.fontStyle = FontStyles.Bold;
        valTxt.color = new Color(0.4f, 0.9f, 0.6f);
        valTxt.alignment = TextAlignmentOptions.Right;
        valGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        return row;
    }

    /// <summary>
    /// 创建子节标题（字体更小、颜色更淡，用于分组调试字段）
    /// </summary>
    public static GameObject CreateSectionHeaderSub(GameObject parent, string text)
    {
        GameObject go = new GameObject("SubHeader_" + text.GetHashCode());
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 18;
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 10;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 7;
        txt.fontSizeMax = 10;
        txt.fontStyle = FontStyles.Italic;
        txt.color = new Color(0.45f, 0.45f, 0.5f);
        txt.alignment = TextAlignmentOptions.Left;
        return go;
    }

    /// <summary>
    /// 创建提示文字行（小号、灰色、居中）
    /// </summary>
    public static GameObject CreateHintText(GameObject parent, string text)
    {
        GameObject go = new GameObject("HintText");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().minHeight = 20;
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.font = GetDefaultFont();
        txt.fontSize = 11;
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 7;
        txt.fontSizeMax = 11;
        txt.fontStyle = FontStyles.Normal;
        txt.color = new Color(0.5f, 0.5f, 0.55f);
        txt.alignment = TextAlignmentOptions.Center;
        return go;
    }

    public static TMP_FontAsset GetDefaultFont()
    {
        if (SharedFont != null) return SharedFont;
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("NotoSansSC-Regular SDF");
        if (font != null) font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        return font;
    }
}