using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 车辆仪表盘 UI
/// 功能：显示速度、状态、传感器数据等信息
/// </summary>
public class VehicleDashboard : MonoBehaviour
{
    [Header("UI 组件引用")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI stateText;
    public TextMeshProUGUI frontDistanceText;
    public TextMeshProUGUI controlModeText;
    public TextMeshProUGUI pathInfoText;

    [Header("车辆组件引用")]
    public SimpleCarController carController;
    public SimpleAutoDrive autoDrive;
    public RaycastSensor sensor;

    void Update()
    {
        if (carController == null || autoDrive == null || sensor == null)
        {
            FindReferences();
            return;
        }

        UpdateSpeedDisplay();
        UpdateStateDisplay();
        UpdateSensorDisplay();
        UpdateControlModeDisplay();
        UpdatePathDisplay();
    }

    void FindReferences()
    {
        if (carController == null)
            carController = FindObjectOfType<SimpleCarController>();
        
        if (autoDrive == null)
            autoDrive = FindObjectOfType<SimpleAutoDrive>();
        
        if (sensor == null)
            sensor = FindObjectOfType<RaycastSensor>();
    }

    void UpdateSpeedDisplay()
    {
        if (speedText != null && carController != null)
        {
            float speed = carController.GetSpeed();
            speedText.text = $"速度: {speed:F1} m/s ({speed * 3.6f:F1} km/h)";
        }
    }

    void UpdateStateDisplay()
    {
        if (stateText != null && autoDrive != null)
        {
            string state = autoDrive.GetCurrentState().ToString();
            string stateText_CN = "";

            switch (state)
            {
                case "Idle": stateText_CN = "空闲"; break;
                case "Following": stateText_CN = "路径跟踪"; break;
                case "Avoiding": stateText_CN = "避障中"; break;
                case "Stopping": stateText_CN = "停止"; break;
                case "Waiting": stateText_CN = "等待"; break;
            }

            stateText.text = $"状态: {stateText_CN}";
        }
    }

    void UpdateSensorDisplay()
    {
        if (frontDistanceText != null && sensor != null)
        {
            float distance = sensor.GetFrontDistance();
            
            if (distance > 0)
            {
                frontDistanceText.text = $"前方障碍物: {distance:F1} m";
                frontDistanceText.color = distance < 10f ? Color.red : Color.green;
            }
            else
            {
                frontDistanceText.text = "前方障碍物: 无";
                frontDistanceText.color = Color.green;
            }
        }
    }

    void UpdateControlModeDisplay()
    {
        if (controlModeText != null && carController != null)
        {
            string mode = carController.autoMode ? "自动驾驶" : "手动控制";
            controlModeText.text = $"控制模式: {mode}";
            controlModeText.color = carController.autoMode ? Color.cyan : Color.yellow;
        }
    }

    void UpdatePathDisplay()
    {
        if (pathInfoText != null && autoDrive != null)
        {
            pathInfoText.text = $"当前路点: {autoDrive.currentWaypointIndex}\n" +
                              $"距下一路点: {autoDrive.distanceToNextWaypoint:F1} m";
        }
    }
}
