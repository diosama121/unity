using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// ROS2 AEB Bridge — 水论文专用：只做紧急制动决策
/// Unity发 lidar_points + velocity → ROS2算安全距离 → 回传刹车指令
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

    [Header("AEB雷达配置")]
    public int lidarRayCount = 11;           // 前方扇形射线数（奇数）
    public float lidarFanAngle = 40f;        // 水平扫描角度
    public float lidarMaxRange = 50f;        // 最大探测距离
    public LayerMask lidarLayerMask = ~0;    // 默认检测所有层

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

    // LiDAR点云缓存（主线程生成，发送线程消费）
    private float[] _cachedLidarPoints = new float[0];
    private readonly object _lidarLock = new object();

    void Start()
    {
        if (autoDrive == null) autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive != null && !autoDrive.isPlayerControlled)
        {
            Debug.Log($"[ROS2Bridge] {gameObject.name} 非主车，跳过ROS2连接");
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
        if (autoDrive == null) autoDrive = GetComponent<SimpleAutoDrive>();

        if (carController == null)
            Debug.LogError("[ROS2Bridge] 未找到 SimpleCarController！");
        else
            Debug.Log($"[ROS2Bridge] 已锁定主车: {carController.gameObject.name}");
    }

    void ConnectToROS2()
    {
        string cleanIP = rosIP.Trim();
        Debug.Log($"[ROS2Bridge] 正在后台连接 ROS2 AEB节点 {cleanIP}:{rosPort}...");

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

                Debug.Log("[ROS2Bridge] AEB节点连接成功！");

                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                sendThread = new Thread(SendLoop);
                sendThread.IsBackground = true;
                sendThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ROS2Bridge] 连接失败: {e.Message}");
                isConnected = false;
            }
        });

        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void Update()
    {
        // 1. 消费ROS2下行指令
        bool receivedThisFrame = false;
        while (commandQueue.TryDequeue(out string jsonData))
        {
            ProcessControlCommand(jsonData);
            receivedThisFrame = true;
        }
        if (receivedThisFrame)
            lastReceiveTime = Time.time;

        // 2. 超时/断连 → 安全降级
        bool isTimeout = (Time.time - lastReceiveTime > rosTimeout);
        if (useRosControl && (!isConnected || isTimeout))
        {
            Debug.LogWarning("[ROS2Bridge] AEB连接超时！交还本地AI控制...");
            useRosControl = false;
            rosLinearVelocity = 0f;
            rosAngularVelocity = 0f;

            if (autoDrive != null)
            {
                autoDrive.enabled = true;
                autoDrive.ResetNavigation(); // 降级后触发重新寻路
            }
        }

        if (!isConnected) return;

        // 3. 生成LiDAR点云（主线程做Physics，线程安全）
        GenerateLidarPoints();

        // 4. 发送车辆状态
        if (Time.time - lastSendTime > 1f / sendRate)
        {
            SendVehicleState();
            lastSendTime = Time.time;
        }

        // 5. 应用ROS2控制指令
        if (useRosControl)
        {
            if (autoDrive != null && autoDrive.enabled)
                autoDrive.enabled = false;

            if (carController != null)
            {
                float maxSpd = carController.maxSpeed > 0 ? carController.maxSpeed : 20f;
                float targetThrottle = Mathf.Clamp(rosLinearVelocity / maxSpd, -1f, 1f);
                float targetSteering = Mathf.Clamp(-rosAngularVelocity / 1.5f, -1f, 1f);

                carController.ApplyCommand(new VehicleCommand
                {
                    throttle = targetThrottle,
                    steering = targetSteering,
                    isBraking = (rosLinearVelocity < 0f)  // ROS2发负速度=刹车
                });
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

    // ==========================================
    // LiDAR 点云生成（前方扇形多射线扫描）
    // ==========================================
    void GenerateLidarPoints()
    {
        var points = new List<float>();
        Vector3 origin = transform.position + Vector3.up * 0.5f; // 车顶高度
        Vector3 fwd = transform.forward;

        float halfAngle = lidarFanAngle * 0.5f;
        for (int i = 0; i < lidarRayCount; i++)
        {
            float angle = (lidarRayCount == 1) ? 0f
                : -halfAngle + (halfAngle * 2f * i / (lidarRayCount - 1));

            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fwd;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, lidarMaxRange, lidarLayerMask))
            {
                // 转为车辆本地坐标（ROS2侧更方便处理）
                Vector3 localHit = transform.InverseTransformPoint(hit.point);
                points.Add(localHit.x);
                points.Add(localHit.y);
                points.Add(localHit.z);
            }
            // 无命中则不添加该点
        }

        lock (_lidarLock)
        {
            _cachedLidarPoints = points.ToArray();
        }
    }

    // ==========================================
    // 发送车辆状态到ROS2
    // ==========================================
    void SendVehicleState()
    {
        if (!isConnected || stream == null || !stream.CanWrite) return;

        try
        {
            float[] lidarCopy;
            lock (_lidarLock)
            {
                lidarCopy = (float[])_cachedLidarPoints.Clone();
            }

            var state = new VehicleState
            {
                velocity = carController != null ? carController.GetSpeed() : 0f,
                steering_angle = carController != null ? carController.currentSteeringAngle : 0f,
                lidar_points = lidarCopy,
                auto_drive_state = (autoDrive != null && autoDrive.enabled)
                    ? autoDrive.longState.ToString() : "ROS2_Controlled",
                timestamp = Time.time
            };

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

    // ==========================================
    // 接收线程
    // ==========================================
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
                    string[] messages = currentBuffer.Split(
                        new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

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
                useRosControl = cmd.enable_control;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ROS2Bridge] JSON解析异常: {e.Message}");
        }
    }

    // ==========================================
    // 连接管理
    // ==========================================
    public void Disconnect()
    {
        Debug.Log("[ROS2Bridge] 断开连接...");
        isConnected = false;

        try
        {
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
            if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
            if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ROS2Bridge] 线程终止异常: {e.Message}");
        }

        if (stream != null) { stream.Close(); stream = null; }
        if (client != null) { client.Close(); client = null; }

        lastReceiveTime = 0f;
        useRosControl = false;
        rosLinearVelocity = 0f;
        rosAngularVelocity = 0f;

        if (autoDrive != null && !autoDrive.enabled)
        {
            autoDrive.enabled = true;
            autoDrive.ResetNavigation();
        }
    }

    public void Reconnect()
    {
        Debug.Log("[ROS2Bridge] 重连中...");
        isConnected = false;

        try
        {
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
            if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
            if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        }
        catch { }

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
        try
        {
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
            if (sendThread != null && sendThread.IsAlive) sendThread.Abort();
            if (connectThread != null && connectThread.IsAlive) connectThread.Abort();
        }
        catch { }
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }

    // ==========================================
    // JSON序列化结构体
    // ==========================================
    [System.Serializable]
    public class VehicleState
    {
        public float velocity;                   // m/s, ROS2侧 *3.6 转 km/h
        public float steering_angle;             // 方向盘角度
        public float[] lidar_points;              // [x,y,z, x,y,z,...] 本地坐标点云
        public string auto_drive_state;           // 自动驾驶状态
        public float timestamp;                   // Unity时间戳
    }

    [System.Serializable]
    public class ControlCommand
    {
        public float linear_velocity;    // m/s, 负值=倒车/刹车
        public float angular_velocity;   // rad/s, 正值=左转
        public bool enable_control;      // true=ROS2接管控制
    }
}