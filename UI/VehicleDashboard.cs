using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 车辆仪表盘 UI (优化版)
/// 功能：用于毕设 Demo 录制的专业数据展示
/// </summary>
public class VehicleDashboard : MonoBehaviour
{
    [Header("UI 组件引用")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI stateText;
    public TextMeshProUGUI frontDistanceText;
    public TextMeshProUGUI controlModeText;
    public TextMeshProUGUI pathInfoText;
    public TextMeshProUGUI trafficLightText; // 新增：红绿灯 UI

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

        UpdateSpeedAndGearDisplay();
        UpdateStateDisplay();
        UpdateSensorDisplay();
        UpdateControlModeDisplay();
        UpdatePathDisplay();
    }

    void FindReferences()
    {
        if (carController == null) carController = FindObjectOfType<SimpleCarController>();
        if (autoDrive == null) autoDrive = FindObjectOfType<SimpleAutoDrive>();
        if (sensor == null) sensor = FindObjectOfType<RaycastSensor>();
    }

    void UpdateSpeedAndGearDisplay()
    {
        if (speedText != null && carController != null)
        {
            float speed = carController.GetSpeed();
            
            // 挡位逻辑推断
            string gear = "P";
            if (Mathf.Abs(speed) > 0.1f)
            {
                gear = speed > 0 ? "D" : "R";
            }
            else if (autoDrive.currentState == SimpleAutoDrive.DriveState.Waiting || 
                     autoDrive.currentState == SimpleAutoDrive.DriveState.Stopping)
            {
                gear = "N"; // 等红灯或停止时显示 N 挡
            }

            // 速度取绝对值，避免倒车时显示负数速度
            float displaySpeed = Mathf.Abs(speed);
            speedText.text = $"[挡位: {gear}]  车速: {displaySpeed * 3.6f:F1} km/h";
            
            // 倒车时给个醒目的颜色
            speedText.color = gear == "R" ? new Color(1f, 0.5f, 0f) : Color.white;
        }
    }

    void UpdateStateDisplay()
    {
        if (stateText != null && autoDrive != null)
        {
            string state = autoDrive.GetCurrentState().ToString();
            string stateText_CN = "";
            Color stateColor = Color.white;

            switch (state)
            {
                case "Idle": stateText_CN = "空闲待机"; stateColor = Color.gray; break;
                case "Following": stateText_CN = "路径跟踪 (自动驾驶)"; stateColor = Color.green; break;
                case "Avoiding": stateText_CN = "⚠️ 避障脱困中"; stateColor = new Color(1f, 0.4f, 0f); break;
                case "Stopping": stateText_CN = "路口停车"; stateColor = Color.red; break;
                case "Waiting": stateText_CN = "等待指令"; stateColor = Color.yellow; break;
            }

            stateText.text = $"系统状态: {stateText_CN}";
            stateText.color = stateColor;
        }
    }

    void UpdateSensorDisplay()
    {
        if (frontDistanceText != null && sensor != null)
        {
            float distance = sensor.GetFrontDistance();
            
            if (distance > 0 && distance < autoDrive.safeDistance * 1.5f)
            {
                frontDistanceText.text = $"雷达: 前方障碍 {distance:F1} m";
                frontDistanceText.color = distance < autoDrive.safeDistance ? Color.red : Color.yellow;
            }
            else
            {
                frontDistanceText.text = "雷达: 前方安全畅通";
                frontDistanceText.color = Color.green;
            }
        }

        // 新增红绿灯状态显示
        if (trafficLightText != null && autoDrive != null)
        {
            string tlState = autoDrive.trafficLightState;
            if (tlState == "Red") { trafficLightText.text = "🚥 信号灯: 红灯"; trafficLightText.color = Color.red; }
            else if (tlState == "Green") { trafficLightText.text = "🚥 信号灯: 绿灯"; trafficLightText.color = Color.green; }
            else if (tlState == "Yellow") { trafficLightText.text = "🚥 信号灯: 黄灯"; trafficLightText.color = Color.yellow; }
            else { trafficLightText.text = "🚥 信号灯: 未检测到"; trafficLightText.color = Color.gray; }
        }
    }

    void UpdateControlModeDisplay()
    {
        if (controlModeText != null && carController != null)
        {
            string mode = carController.autoMode ? "AutoPilot (自动)" : "Manual (接管)";
            controlModeText.text = $"模式: {mode}";
            controlModeText.color = carController.autoMode ? Color.cyan : new Color(1f, 0.6f, 0f);
        }
    }

    void UpdatePathDisplay()
    {
        if (pathInfoText != null && autoDrive != null)
        {
            pathInfoText.text = $"导航节点: {autoDrive.currentWaypointIndex}\n" +
                              $"距下一节点: {autoDrive.distanceToNextWaypoint:F1} m";
        }
    }
}