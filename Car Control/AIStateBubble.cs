using UnityEngine;
using UnityEngine.UI;

public class AIStateBubble : MonoBehaviour
{
    public Vector3 worldOffset = new Vector3(0, 2.2f, 0);
    public float fontSize = 18;

    private Canvas bubbleCanvas;
    private Text stateText;
    private SimpleAutoDrive autoDrive;
    private SimpleCarController carController;

    void Start()
    {
        autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive == null) autoDrive = GetComponentInChildren<SimpleAutoDrive>();

        carController = GetComponent<SimpleCarController>();
        if (carController == null) carController = GetComponentInChildren<SimpleCarController>();

        GameObject canvasGO = new GameObject("AIStateBubble");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = worldOffset;
        canvasGO.transform.localRotation = Quaternion.identity;

        bubbleCanvas = canvasGO.AddComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        bubbleCanvas.sortingOrder = 200;

        CanvasScaler cs = canvasGO.AddComponent<CanvasScaler>();
        cs.dynamicPixelsPerUnit = 10;

        RectTransform rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 60);

        GameObject bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(canvasGO.transform, false);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.55f);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(canvasGO.transform, false);
        stateText = txtGO.AddComponent<Text>();
        stateText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stateText.fontSize = (int)fontSize;
        stateText.fontStyle = FontStyle.Bold;
        stateText.color = Color.green;
        stateText.alignment = TextAnchor.MiddleCenter;
        stateText.text = "IDLE";
        RectTransform txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(6, 4);
        txtRT.offsetMax = new Vector2(-6, -4);
    }

    void LateUpdate()
    {
        if (stateText == null) return;

        string state = "IDLE";
        Color col = Color.gray;

        if (autoDrive != null && autoDrive.enabled)
        {
            switch (autoDrive.GetCurrentState())
            {
                case SimpleAutoDrive.DriveState.Following:
                    state = "FOLLOW";
                    col = new Color(0.2f, 0.9f, 0.3f);
                    break;
                case SimpleAutoDrive.DriveState.Avoiding:
                    state = "AVOID";
                    col = new Color(1f, 0.7f, 0.1f);
                    break;
                case SimpleAutoDrive.DriveState.Stopping:
                    state = "STOP";
                    col = new Color(1f, 0.2f, 0.2f);
                    break;
                case SimpleAutoDrive.DriveState.Waiting:
                    state = "WAIT";
                    col = new Color(0.5f, 0.5f, 0.6f);
                    break;
                default:
                    state = "IDLE";
                    col = Color.gray;
                    break;
            }

            if (autoDrive.vehiclePriority == VehiclePriority.Emergency)
            {
                state = "EMERG " + state;
                col = new Color(1f, 0.1f, 0.1f);
            }
        }

        stateText.text = state;
        stateText.color = col;

        if (carController != null && carController.currentSpeed > 5f)
        {
            stateText.text += "\n" + carController.currentSpeed.ToString("F0") + " m/s";
        }
    }
}