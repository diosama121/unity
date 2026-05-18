using UnityEngine;
using UnityEngine.UI;

public class AIStateBubble : MonoBehaviour
{
    public Vector3 worldOffset = new Vector3(0, 2.2f, 0);
    public float fontSize = 18;

    private Canvas bubbleCanvas;
    private Text stateText;
    private Transform canvasTrans;
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
        canvasTrans = canvasGO.transform;
        canvasTrans.localScale = new Vector3(0.01f, 0.01f, 0.01f);

        bubbleCanvas = canvasGO.AddComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        bubbleCanvas.sortingOrder = 200;

        CanvasScaler cs = canvasGO.AddComponent<CanvasScaler>();
        cs.dynamicPixelsPerUnit = 10;

        RectTransform rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(240, 90);

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
        stateText.font = Resources.Load<Font>("simhei");
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
        if (canvasTrans != null && Camera.main != null)
        {
            canvasTrans.forward = Camera.main.transform.forward;
        }

        if (stateText == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

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

        sb.AppendLine(state);
        stateText.color = col;

        if (autoDrive != null && autoDrive.currentLaneId >= 0)
        {
            sb.AppendLine("<color=#88CCFF>Lane:" + autoDrive.currentLaneId + "</color>");
        }

        if (carController != null)
        {
            float kmh = carController.currentSpeed * 3.6f;
            sb.AppendLine(kmh.ToString("F0") + " km/h");
        }

        stateText.text = sb.ToString().TrimEnd();
    }
}