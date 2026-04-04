using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// ROS2 Bridge (更新版)
/// 功能：Unity ↔ ROS2 通信，支持自动驾驶系统
/// </summary>
public class ROS2BridgeV2 : MonoBehaviour
{
    [Header("ROS2 Connection")]
    public string rosIP = "172.21.16.202";
    public int rosPort = 10086;

    [Header("Vehicle Components")]
    public SimpleCarController carController;
    public SimpleAutoDrive autoDrive;
    public RaycastSensor sensor;

    [Header("发送频率")]
    [Tooltip("每秒发送数据次数")]
    public float sendRate = 10f;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
    private float lastSendTime = 0f;

    // ROS2 控制指令
    private float rosLinearVelocity = 0f;
    private float rosAngularVelocity = 0f;
    private bool useRosControl = false;

    void Start()
    {
        FindComponents();
        ConnectToROS2();
    }

    void FindComponents()
    {
        if (carController == null)
            carController = GetComponent<SimpleCarController>();
        
        if (autoDrive == null)
            autoDrive = GetComponent<SimpleAutoDrive>();
        
        if (sensor == null)
            sensor = GetComponent<RaycastSensor>();
    }

    void ConnectToROS2()
    {
        try
        {
            Debug.Log($"🔌 连接到 ROS2: {rosIP}:{rosPort}...");
            
            client = new TcpClient();
            client.Connect(rosIP, rosPort);
            stream = client.GetStream();
            isConnected = true;

            Debug.Log($"✅ ROS2 连接成功");

            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ ROS2 连接失败: {e.Message}");
            isConnected = false;
        }
    }

    void Update()
    {
        if (!isConnected) return;

        // 定时发送数据到 ROS2
        if (Time.time - lastSendTime > 1f / sendRate)
        {
            SendVehicleState();
            lastSendTime = Time.time;
        }

        // 如果启用 ROS 控制，应用控制指令
        if (useRosControl && carController != null)
        {
            carController.SetAutoControl(rosLinearVelocity, rosAngularVelocity);
        }
    }

    void SendVehicleState()
    {
        if (!isConnected || stream == null || !stream.CanWrite)
            return;

        try
        {
            // 构建车辆状态 JSON
            var state = new VehicleState
            {
                position = new float[] {
                    transform.position.x,
                    transform.position.y,
                    transform.position.z
                },
                rotation = new float[] {
                    transform.eulerAngles.x,
                    transform.eulerAngles.y,
                    transform.eulerAngles.z
                },
                velocity = carController != null ? carController.GetSpeed() : 0f,
                steering_angle = carController != null ? carController.currentSteeringAngle : 0f,
                auto_drive_state = autoDrive != null ? autoDrive.GetCurrentState().ToString() : "Unknown",
                front_obstacle_distance = sensor != null ? sensor.GetFrontDistance() : -1f,
                timestamp = Time.time
            };

            string jsonData = JsonUtility.ToJson(state) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"发送数据失败: {e.Message}");
            isConnected = false;
        }
    }

    void ReceiveData()
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuffer = new StringBuilder();

        while (isConnected)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuffer.Append(data);

                    string bufferString = messageBuffer.ToString();
                    int newlineIndex;

                    while ((newlineIndex = bufferString.IndexOf('\n')) != -1)
                    {
                        string message = bufferString.Substring(0, newlineIndex);
                        bufferString = bufferString.Substring(newlineIndex + 1);

                        if (!string.IsNullOrEmpty(message))
                        {
                            ProcessControlCommand(message);
                        }
                    }

                    messageBuffer.Clear();
                    messageBuffer.Append(bufferString);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"接收数据失败: {e.Message}");
                isConnected = false;
                break;
            }
        }
    }

    void ProcessControlCommand(string jsonData)
    {
        try
        {
            ControlCommand cmd = JsonUtility.FromJson<ControlCommand>(jsonData);
            
            rosLinearVelocity = cmd.linear_velocity;
            rosAngularVelocity = cmd.angular_velocity;

            if (cmd.enable_control)
            {
                useRosControl = true;
            }

            Debug.Log($"📡 收到 ROS2 控制: Linear={rosLinearVelocity:F2}, Angular={rosAngularVelocity:F2}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"解析控制指令失败: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        isConnected = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
        }

        if (stream != null) stream.Close();
        if (client != null) client.Close();

        Debug.Log("🔌 ROS2 连接已断开");
    }

    // ========== 数据结构 ==========

    [System.Serializable]
    public class VehicleState
    {
        public float[] position;
        public float[] rotation;
        public float velocity;
        public float steering_angle;
        public string auto_drive_state;
        public float front_obstacle_distance;
        public float timestamp;
    }

    [System.Serializable]
    public class ControlCommand
    {
        public float linear_velocity;
        public float angular_velocity;
        public bool enable_control;
    }

    // ========== 公共接口 ==========

    public bool IsConnected()
    {
        return isConnected;
    }

    public void EnableRosControl(bool enable)
    {
        useRosControl = enable;
    }
}
