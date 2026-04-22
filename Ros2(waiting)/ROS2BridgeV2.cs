using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

/// <summary>
/// ROS2 Bridge (异步防卡死终极版)
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
    public float sendRate = 10f;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private Thread connectThread; // 新增：专门用于连接的后台线程
    
    private volatile bool isConnected = false;
    private float lastSendTime = 0f;

    // ROS2 控制指令
    private float rosLinearVelocity = 0f;
    private float rosAngularVelocity = 0f;
    private bool useRosControl = false;

    // 线程安全的并发队列
    private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

   void Start()
    {
        // 【核心修复】防止生成的 NPC 车辆也去抢占 ROS2 端口
        // 假设你的 NPC 名字里带有 "NPC" 或者 "Clone"
        if (gameObject.name.Contains("NPC") || gameObject.name.Contains("Clone"))
        {
            Debug.Log($"🚫 {gameObject.name} 是 NPC 车辆，已关闭其 ROS2 连接节点。");
            this.enabled = false; // 直接禁用本脚本
            return;
        }

        FindComponents();
        ConnectToROS2();
    }

    void FindComponents()
    {
        if (carController == null) carController = GetComponent<SimpleCarController>();
        if (autoDrive == null) autoDrive = GetComponent<SimpleAutoDrive>();
        if (sensor == null) sensor = GetComponent<RaycastSensor>();
    }

    void ConnectToROS2()
    {
        string cleanIP = rosIP.Trim();
        Debug.Log($"🔌 正在后台尝试连接到 ROS2: {cleanIP}:{rosPort}...");

        // 【核心修复 1】将连接过程打包丢进后台线程！从此再也不会卡死 Unity！
        connectThread = new Thread(() =>
        {
            try
            {
                client = new TcpClient();
                client.NoDelay = true; // 禁用延迟算法，拒绝粘包
                client.SendTimeout = 2000;
                client.ReceiveTimeout = 2000;

                // 这一步即使卡20秒，也只会卡在这个看不见的后台线程里，游戏画面依然流畅
                client.Connect(cleanIP, rosPort); 
                stream = client.GetStream();
                isConnected = true;
                
                Debug.Log($"✅ ROS2 连接成功！");

                // 连上之后，启动接收线程
                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"❌ ROS2 连接失败 (可能 IP 有误或未启动): {e.Message}");
                isConnected = false;
            }
        });
        
        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void Update()
    {
        // 安全地从后台队列里取出 ROS2 指令并解析
        while (commandQueue.TryDequeue(out string jsonData))
        {
            ProcessControlCommand(jsonData);
        }

        // 如果没连上，直接退出 Update 的网络部分，让车子保持现有状态
        if (!isConnected) return;

        // 定时发送数据到 ROS2
        if (Time.time - lastSendTime > 1f / sendRate)
        {
            SendVehicleState();
            lastSendTime = Time.time;
        }

        // 应用 ROS 控制指令
        if (useRosControl && carController != null)
        {
            if (autoDrive != null && autoDrive.enabled)
            {
                autoDrive.enabled = false; 
                Debug.Log("🤖 已将车辆控制权完全移交给 ROS2 网络！");
            }

            float targetThrottle = Mathf.Clamp(rosLinearVelocity / carController.maxSpeed, -1f, 1f);
            float targetSteering = Mathf.Clamp(-rosAngularVelocity / 1.5f, -1f, 1f);

            carController.SetAutoControl(targetThrottle, targetSteering);
        }
    }

   void SendVehicleState()
    {
        if (!isConnected || stream == null || !stream.CanWrite) return;

        try
        {
            var state = new VehicleState
            {
                position = new float[] { transform.position.x, transform.position.y, transform.position.z },
                rotation = new float[] { transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z },
                velocity = carController != null ? carController.GetSpeed() : 0f,
                steering_angle = carController != null ? carController.currentSteeringAngle : 0f,
                auto_drive_state = (autoDrive != null && autoDrive.enabled) ? autoDrive.GetCurrentState().ToString() : "ROS2_Controlled",
                front_obstacle_distance = sensor != null ? sensor.GetFrontDistance() : -1f,
                timestamp = Time.time
            };

            string jsonData = JsonUtility.ToJson(state) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            
            // 【核心修复】摒弃 BeginWrite，恢复为强制同步写入！
            // 只要加上 Flush()，数据就会像子弹一样瞬间打到 ROS2 端
            stream.Write(data, 0, data.Length);
            stream.Flush(); 
        }
        catch (Exception)
        {
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
                if (bytesRead == 0)
                {
                    isConnected = false;
                    break;
                }

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
                            commandQueue.Enqueue(message);
                        }
                    }
                    messageBuffer.Clear();
                    messageBuffer.Append(bufferString);
                }
            }
            catch (Exception)
            {
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
            if (cmd.enable_control) useRosControl = true;
        }
        catch (Exception) { /* 忽略解析错误 */ }
    }

    void OnApplicationQuit()
    {
        isConnected = false;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }

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
}