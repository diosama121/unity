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
    public float rosTimeout = 2.0f;
    private float lastReceiveTime = 0f;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private Thread connectThread;

    public volatile bool isConnected = false;
    private float lastSendTime = 0f;

    private float rosLinearVelocity = 0f;
    private float rosAngularVelocity = 0f;
    private bool useRosControl = false;

    private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<byte[]> sendQueue = new ConcurrentQueue<byte[]>();
    private Thread sendThread;

    void Start()
    {
        if (gameObject.name.Contains("NPC") || gameObject.name.Contains("Clone"))
        {
            Debug.Log($"🚫 {gameObject.name} 是 NPC 车辆，已关闭其 ROS2 连接节点。");
            this.enabled = false;
            return;
        }

        FindComponents();
        ConnectToROS2();
    }

    void FindComponents()
    {
        if (carController == null) carController = GetComponent<SimpleCarController>();
        if (carController == null) carController = GetComponentInChildren<SimpleCarController>();

        if (carController == null)
        {
            SimpleCarController[] allCars = FindObjectsOfType<SimpleCarController>();
            foreach (var car in allCars)
            {
                string objName = car.gameObject.name.ToLower();
                if (!objName.Contains("npc") && !objName.Contains("clone") && !objName.Contains("traffic"))
                {
                    carController = car;
                    break;
                }
            }
        }

        if (autoDrive == null && carController != null)
            autoDrive = carController.GetComponent<SimpleAutoDrive>();

        if (carController == null)
        {
            Debug.LogError("❌ 找不到主车底盘！请确保主车名字中不包含 NPC/Clone。");
        }
        else
        {
            Debug.Log($"🎯 ROS2 专属桥接成功！已锁定主车: {carController.gameObject.name}");
        }
    }

    void ConnectToROS2()
    {
        string cleanIP = rosIP.Trim();
        Debug.Log($"🔌 正在后台尝试连接到 ROS2: {cleanIP}:{rosPort}...");

        connectThread = new Thread(() =>
        {
            try
            {
                client = new TcpClient();
                client.NoDelay = true;
                client.SendTimeout = 2000;
                client.ReceiveTimeout = 2000;

                client.Connect(cleanIP, rosPort);
                stream = client.GetStream();
                isConnected = true;

                Debug.Log($"✅ ROS2 连接成功！");

                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                sendThread = new Thread(SendLoop);
                sendThread.IsBackground = true;
                sendThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"❌ ROS2 连接失败: {e.Message}");
                isConnected = false;
            }
        });

        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void Update()
    {
        bool receivedThisFrame = false;
        while (commandQueue.TryDequeue(out string jsonData))
        {
            ProcessControlCommand(jsonData);
            receivedThisFrame = true;
        }

        if (receivedThisFrame)
        {
            lastReceiveTime = Time.time;
        }

        bool isTimeout = (Time.time - lastReceiveTime > rosTimeout);
        
        if (useRosControl && (!isConnected || isTimeout))
        {
            Debug.LogWarning("⚠️ ROS2 连接断开或指令超时！触发安全降级，交还本地 AI 控制...");
            useRosControl = false;
            rosLinearVelocity = 0f;
            rosAngularVelocity = 0f;

            if (autoDrive != null && !autoDrive.enabled)
            {
                autoDrive.enabled = true;
            }
        }

        if (!isConnected) return;

        if (Time.time - lastSendTime > 1f / sendRate)
        {
            SendVehicleState();
            lastSendTime = Time.time;
        }

        if (useRosControl)
        {
            if (autoDrive != null && autoDrive.enabled)
            {
                autoDrive.enabled = false;
            }

            if (carController != null)
            {
                float maxSpd = carController.maxSpeed > 0 ? carController.maxSpeed : 20f;
                float targetThrottle = Mathf.Clamp(rosLinearVelocity / maxSpd, -1f, 1f);
                float targetSteering = Mathf.Clamp(-rosAngularVelocity / 1.5f, -1f, 1f);

                carController.ApplyCommand(new VehicleCommand { throttle = targetThrottle, steering = targetSteering });
            }
        }
        else
        {
            if (autoDrive != null && !autoDrive.enabled)
            {
                autoDrive.enabled = true;
            }
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
                lane_id = autoDrive != null ? autoDrive.currentLaneId : -1,
                stopline_distance = -1f,
                phase_state = "Uncontrolled",
                timestamp = Time.time
            };
///
      //      if (autoDrive != null && WorldModel.Instance != null )//autoDrive.currentDestinationNodeId这里之前有东西的，是个判断的，重构过程中先删一下//
      //      {
      //          var stopLine =WorldModel.Instance.GetNearestStopLine(autoDrive.currentDestinationNodeId, transform.position);
      //         if (stopLine != null)
      //          {
//state.stopline_distance = Vector3.Distance(transform.position, stopLine.Position);
       //             int phaseId = stopLine.AssociatedPhaseId;
  //                 state.phase_state = WorldModel.Instance.GetPhaseState(phaseId).ToString();
    //            }
   //         }
///
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

                    string currentBuffer = messageBuffer.ToString();
                    string[] messages = currentBuffer.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < messages.Length; i++)
                    {
                        if (i == messages.Length - 1 && !currentBuffer.EndsWith("\n"))
                        {
                            messageBuffer.Clear();
                            messageBuffer.Append(messages[i]);
                            break;
                        }
                        commandQueue.Enqueue(messages[i]);
                    }

                    if (currentBuffer.EndsWith("\n")) messageBuffer.Clear();
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
            jsonData = jsonData.Trim();
            ControlCommand cmd = JsonUtility.FromJson<ControlCommand>(jsonData);
            if (cmd != null)
            {
                rosLinearVelocity = cmd.linear_velocity;
                rosAngularVelocity = cmd.angular_velocity;
                useRosControl = true;
            }
            Debug.Log($"🧩 JSON解析结果: 提取到的速度 = {rosLinearVelocity}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"JSON 解析异常: {e.Message} | 数据: {jsonData}");
        }
    }

    // ===== 新增 Disconnect 方法 =====
    public void Disconnect()
    {
        Debug.Log("🔌 ROS2 Bridge 正在断开连接...");
        isConnected = false;

        // 安全终止后台线程（与项目中其他线程终止方式保持一致）
        try
        {
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
            if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
            if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"断开连接时线程终止异常: {e.Message}");
        }

        // 释放网络资源
        if (stream != null) { stream.Close(); stream = null; }
        if (client != null) { client.Close(); client = null; }

        // 重置控制状态
        lastReceiveTime = 0f;
        useRosControl = false;
        rosLinearVelocity = 0f;
        rosAngularVelocity = 0f;

        // 如果当前处于远程控制状态，交还本地 AI
        if (autoDrive != null && !autoDrive.enabled)
        {
            autoDrive.enabled = true;
        }
    }
    // ==============================

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