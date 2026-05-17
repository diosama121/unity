using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI面板构建工具类 —— 纯静态方法，从 MasterUIManager 中提取
/// 用于在 Canvas 下动态生成设置面板的各种 UI 控件
/// </summary>
public static class UIPanelBuilder
{
    public static GameObject CreateTitle(GameObject parent, string text)
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

    public static GameObject CreateSectionHeader(GameObject parent, string text)
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
    public static GameObject CreateSectionHeaderSub(GameObject parent, string text)
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
    public static GameObject CreateHintText(GameObject parent, string text)
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

    public static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}