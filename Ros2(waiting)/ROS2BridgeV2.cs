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
    [Header("发送频率")]
    public float sendRate = 10f;
    [Header("安全与降级策略")]
    public float rosTimeout = 2.0f; // 超过 2 秒没收到数据，认为 ROS2 掉线
    private float lastReceiveTime = 0f; // 记录最后一次收到数据的时间
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private Thread connectThread; // 新增：专门用于连接的后台线程

    public volatile bool isConnected = false;
    private float lastSendTime = 0f;

    // ROS2 控制指令
    private float rosLinearVelocity = 0f;
    private float rosAngularVelocity = 0f;
    private bool useRosControl = false;

    // 线程安全的并发队列
    private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<byte[]> sendQueue = new ConcurrentQueue<byte[]>();
    private Thread sendThread;

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
        // 1. 先尝试在自己或子物体身上找（最安全的做法）
        if (carController == null) carController = GetComponent<SimpleCarController>();
        if (carController == null) carController = GetComponentInChildren<SimpleCarController>();

        // 2. 如果必须全图搜索，启动【NPC 排除过滤】！
        if (carController == null)
        {
            SimpleCarController[] allCars = FindObjectsOfType<SimpleCarController>();
            foreach (var car in allCars)
            {
                string objName = car.gameObject.name.ToLower();
                // 核心过滤逻辑：只要名字里带有 npc、clone、traffic 的，统统跳过！
                if (!objName.Contains("npc") && !objName.Contains("clone") && !objName.Contains("traffic"))
                {
                    carController = car;
                    break; // 找到主车，立刻锁定！
                }
            }
        }

        // 3. 顺藤摸瓜，找到主车的另外两个组件
        if (autoDrive == null && carController != null)
            autoDrive = carController.GetComponent<SimpleAutoDrive>();

      

        // 4. 状态汇报
        if (carController == null)
        {
            Debug.LogError("❌ 找不到主车底盘！请确保主车名字中不包含 NPC/Clone。");
        }
        else
        {
            Debug.Log($"🎯 ROS2 专属桥接成功！已锁定主车: {carController.gameObject.name}，完美排除所有 NPC。");
        }
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

                sendThread = new Thread(SendLoop);
                sendThread.IsBackground = true;
                sendThread.Start();
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
        // 1. 解析队列中的指令
        bool receivedThisFrame = false;
        while (commandQueue.TryDequeue(out string jsonData))
        {
            ProcessControlCommand(jsonData);
            receivedThisFrame = true;
        }

        // 🌟 记录最后一次收到正确指令的时间
        if (receivedThisFrame)
        {
            lastReceiveTime = Time.time;
        }

        // 🌟 【核心修复】：降级机制必须在 `return` 之前执行！
        // 触发条件：超过两秒没数据，或者 TCP 连接被强制断开（比如 Ctrl+C）
        bool isTimeout = (Time.time - lastReceiveTime > rosTimeout);
        
        if (useRosControl && (!isConnected || isTimeout))
        {
            Debug.LogWarning("⚠️ ROS2 连接断开或指令超时！触发安全降级，瞬间交还控制权给本地 AI...");
            useRosControl = false;
            rosLinearVelocity = 0f;
            rosAngularVelocity = 0f;

            // 唤醒本地的 SimpleAutoDrive 寻路 AI
            if (autoDrive != null && autoDrive.currentState == SimpleAutoDrive.DriveState.RemoteControlled)
            {
                autoDrive.currentState = SimpleAutoDrive.DriveState.Following;
            }
        }

        // 如果网络断了，不执行后面的发送数据的代码
        if (!isConnected) return;

        // 2. 定时发送状态
        if (Time.time - lastSendTime > 1f / sendRate)
        {
            SendVehicleState();
            lastSendTime = Time.time;
        }

        // 3. 应用 ROS2 控制 (只有在没超时且连着的时候执行)
        if (useRosControl)
        {
            if (autoDrive != null && autoDrive.currentState != SimpleAutoDrive.DriveState.RemoteControlled)
            {
                autoDrive.currentState = SimpleAutoDrive.DriveState.RemoteControlled;
            } 

            if (carController != null)
            {
                carController.autoMode = true; 
                float maxSpd = carController.maxSpeed > 0 ? carController.maxSpeed : 20f;
                float targetThrottle = Mathf.Clamp(rosLinearVelocity / maxSpd, -1f, 1f);
                float targetSteering = Mathf.Clamp(-rosAngularVelocity / 1.5f, -1f, 1f);

                carController.SetAutoControl(targetThrottle, targetSteering);
            }
        }
       }    void SendVehicleState()
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
                // 【Phase 4】语义感知数据
                lane_id = autoDrive != null ? autoDrive.currentLaneId : -1,
                stopline_distance = -1f,
                phase_state = "Uncontrolled",
                timestamp = Time.time
            };

            // 【Phase 4】填充 stopline_distance 和 phase_state
            if (autoDrive != null && WorldModel.Instance != null && autoDrive.currentDestinationNodeId >= 0)
            {
                var stopLine = WorldModel.Instance.GetNearestStopLine(autoDrive.currentDestinationNodeId, transform.position);
                if (stopLine != null)
                {
                    state.stopline_distance = Vector3.Distance(transform.position, stopLine.Position);
                    int phaseId = stopLine.AssociatedPhaseId;
                    state.phase_state = WorldModel.Instance.GetPhaseState(phaseId).ToString();
                }
            }

            string jsonData = JsonUtility.ToJson(state) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            while (sendQueue.TryDequeue(out _)) { }
            sendQueue.Enqueue(data);
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

    void SendLoop()
    {
        while (isConnected)
        {
            while (sendQueue.TryDequeue(out byte[] data))
            {
                try
                {
                    if (stream != null && stream.CanWrite)
                    {
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                }
                catch (Exception)
                {
                    isConnected = false;
                    break;
                }
            }
            Thread.Sleep(5);
        }
    }

    void ProcessControlCommand(string jsonData)
    {
        try
        {
            // 清除多余换行符，防止 JSON 解析器报错罢工
            jsonData = jsonData.Trim();

            ControlCommand cmd = JsonUtility.FromJson<ControlCommand>(jsonData);
            if (cmd != null)
            {
                rosLinearVelocity = cmd.linear_velocity;
                rosAngularVelocity = cmd.angular_velocity;

                // 霸道逻辑：无视其他状态，只要 ROS2 发来了数据，无脑强行接管方向盘！
                useRosControl = true;
            }
            Debug.Log($"🧩 JSON解析结果: 提取到的速度 = {rosLinearVelocity}");
        }
        catch (Exception e)
        {
            // 如果解析失败，在控制台静默提示，绝不卡死
            Debug.LogWarning($"JSON 解析异常: {e.Message} | 数据: {jsonData}");
        }
    }
    public void Reconnect()
    {
        Debug.Log("🔄 ROS2 Bridge 正在重连...");
        isConnected = false;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
        if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        if (stream != null) { stream.Close(); stream = null; }
        if (client != null) { client.Close(); client = null; }
        lastReceiveTime = 0f;
        useRosControl = false;
        rosLinearVelocity = 0f;
        rosAngularVelocity = 0f;
        ConnectToROS2();
    }

    void OnApplicationQuit()
    {
        isConnected = false;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
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
        public int lane_id;
        public float stopline_distance;
        public string phase_state;
    }

    [System.Serializable]
    public class ControlCommand
    {
        public float linear_velocity;
        public float angular_velocity;
        public bool enable_control;
    }
}