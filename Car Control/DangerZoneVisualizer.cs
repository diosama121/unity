using UnityEngine;

public class DangerZoneVisualizer : MonoBehaviour
{
    public float zoneDistance = 3f;
    public float zoneWidth = 2.5f;
    public float zoneHeight = 0.8f;
    public Color safeColor = new Color(1f, 0.3f, 0.1f, 0.25f);
    public Color dangerColor = new Color(1f, 0.05f, 0.05f, 0.5f);

    private GameObject zonePlane;
    private Material zoneMat;
    private SimpleAutoDrive autoDrive;
    private SimpleCarController carController;

    void Start()
    {
        autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive == null) autoDrive = GetComponentInChildren<SimpleAutoDrive>();

        carController = GetComponent<SimpleCarController>();
        if (carController == null) carController = GetComponentInChildren<SimpleCarController>();

        zonePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        zonePlane.name = "DangerZone";
        zonePlane.transform.SetParent(transform, false);
        zonePlane.transform.localPosition = new Vector3(0, 0.15f, zoneDistance);
        zonePlane.transform.localRotation = Quaternion.Euler(90, 0, 0);
        zonePlane.transform.localScale = new Vector3(zoneWidth * 0.1f, 1f, zoneHeight * 0.1f);

        Collider col = zonePlane.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer rend = zonePlane.GetComponent<Renderer>();
        if (rend != null)
        {
            zoneMat = new Material(Shader.Find("Sprites/Default"));
            zoneMat.color = safeColor;
            rend.material = zoneMat;
        }
    }

    void Update()
    {
        if (zoneMat == null || zonePlane == null) return;

        float speed = carController != null ? carController.currentSpeed : 0f;
        float safeDist = autoDrive != null ? autoDrive.safeDistance : 8f;
        bool obstacleClose = autoDrive != null && autoDrive.obstacleDetected;

        float t = Mathf.Clamp01(speed / (safeDist * 2f));

        if (obstacleClose)
        {
            zoneMat.color = dangerColor;
            zonePlane.transform.localScale = new Vector3(zoneWidth * 0.15f, 1f, zoneHeight * 0.15f);
        }
        else
        {
            zoneMat.color = Color.Lerp(
                new Color(1f, 0.7f, 0.1f, 0.15f),
                new Color(1f, 0.1f, 0.05f, 0.35f), t);
            zonePlane.transform.localScale = new Vector3(
                Mathf.Lerp(zoneWidth * 0.08f, zoneWidth * 0.12f, t),
                1f,
                Mathf.Lerp(zoneHeight * 0.08f, zoneHeight * 0.15f, t));
        }

        zonePlane.transform.localPosition = new Vector3(0, 0.15f, Mathf.Lerp(zoneDistance, zoneDistance * 1.5f, t));
    }

    void OnDestroy()
    {
        if (zoneMat != null) Destroy(zoneMat);
    }
}