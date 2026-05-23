using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// ROS2 AEB Bridge — 挂World上，动态追踪主车（isPlayerControlled=true的车）
/// Unity发 lidar_points + velocity → ROS2算TTC → 回传刹车指令
/// </summary>
public class ROS2BridgeV2 : MonoBehaviour
{
    [Header("ROS2 Connection")]
    public string rosIP = "172.21.16.202";
    public int rosPort = 10086;

    [Header("发送频率")]
    public float sendRate = 10f;

    [Header("AEB雷达配置")]
    public int lidarRayCount = 11;
    public float lidarFanAngle = 40f;
    public float lidarMaxRange = 50f;
    public LayerMask lidarLayerMask = ~0;

    [Header("安全与降级策略")]
    public float rosTimeout = 2.0f;

    // ===== 动态追踪主车 =====
    private SimpleCarController _carController;
    private SimpleAutoDrive _autoDrive;
    private Transform _carTransform;

    private float lastReceiveTime = 0f;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private Thread connectThread;
 
    private volatile bool _isConnected = false;
    public bool isConnected => _isConnected;
    public event Action<bool> OnConnectionChanged;
    private bool _lastKnownConnected = false;

    private float lastSendTime = 0f;

    private float rosLinearVelocity = 0f;
    private float rosAngularVelocity = 0f;
    private bool useRosControl = false;

    private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<byte[]> sendQueue = new ConcurrentQueue<byte[]>();
    private Thread sendThread;

    // LiDAR点云缓存
    private float[] _cachedLidarPoints = new float[0];
    private readonly object _lidarLock = new object();

    // ===== ROS2回传的全局状态（来自global_state消息） ===== 
    private string _rosAebState = "IDLE";
    private float _rosTTC = 0f;
    private float _rosMinDist = 0f;
    private float _rosSpeedKmh = 0f;

    // ===== 屏幕通知 =====
    private float _connectNotifyTimer = 0f;
    private const float connectNotifyDuration = 3f;
    private volatile bool _connectionJustEstablished = false;
    private float _aebWarningTimer = 0f;
    private const float aebWarningDuration = 2f;
    private bool _aebActive = false;

    // 主车切换检测
    private GameObject _lastMainCar = null;

    void Start()
    {
        ConnectToROS2();
    }

    // ==========================================
    // 每帧动态追踪主车
    // ==========================================
    void FindMainCar()
    {
        SimpleAutoDrive[] all = FindObjectsOfType<SimpleAutoDrive>();
        SimpleAutoDrive main = null;
        foreach (var ad in all)
        {
            if (ad != null && ad.isPlayerControlled)
            {
                main = ad;
                break;
            }
        }

        if (main == null)
        {
            _carController = null;
            _autoDrive = null;
            _carTransform = null;
            _lastMainCar = null;
            return;
        }

        // 主车切换了 → 重新绑定
        if (main.gameObject != _lastMainCar)
        {
            _lastMainCar = main.gameObject;
            _carController = main.GetComponent<SimpleCarController>();
            _autoDrive = main;
            _carTransform = main.transform;
            Debug.Log($"[ROS2Bridge] 主车切换 → {main.name}");
        }
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
                _isConnected = true;

                Debug.Log("[ROS2Bridge] AEB节点连接成功！");
                _connectionJustEstablished = true;

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
                _isConnected = false;
            }
        });

        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void Update()
    {
        // ★ 主线程收拢子线程的连接通知
        if (_connectionJustEstablished)
        {
            _connectionJustEstablished = false;
            _connectNotifyTimer = connectNotifyDuration;
        }

        // ★ 通知计时器衰减
        if (_connectNotifyTimer > 0f) _connectNotifyTimer -= Time.deltaTime;
        if (_aebWarningTimer > 0f) _aebWarningTimer -= Time.deltaTime;
        else _aebActive = false;

        // ★ 连接状态变更 → 通知UI
        if (_isConnected != _lastKnownConnected)
        {
            _lastKnownConnected = _isConnected;
            OnConnectionChanged?.Invoke(_isConnected);
        }

        // ★ 动态追踪主车（支持T键随时切换）
        FindMainCar();

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

            if (_autoDrive != null)
            {
                _autoDrive.enabled = true;
                _autoDrive.ResetNavigation();
            }
        }

        if (!isConnected) return;

        // 没有主车就不干活
        if (_carController == null || _carTransform == null) return;

        // 3. 生成LiDAR点云
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
            if (_autoDrive != null && _autoDrive.enabled)
                _autoDrive.enabled = false;

            bool isBraking = (rosLinearVelocity < 0f);
            if (_carController != null)
            {
                float maxSpd = _carController.maxSpeed > 0 ? _carController.maxSpeed : 20f;
                float targetThrottle = Mathf.Clamp(rosLinearVelocity / maxSpd, -1f, 1f);
                float targetSteering = Mathf.Clamp(-rosAngularVelocity / 1.5f, -1f, 1f);

                _carController.ApplyCommand(new VehicleCommand
                {
                    throttle = targetThrottle,
                    steering = targetSteering,
                    isBraking = isBraking
                });
            }

            if (isBraking)
            {
                _aebActive = true;
                _aebWarningTimer = aebWarningDuration;
            }
        }
        else
        {
            if (_autoDrive != null && !_autoDrive.enabled)
            {
                _autoDrive.enabled = true;
            }
        }
    }

    // ==========================================
    // LiDAR 点云生成
    // ==========================================
    void GenerateLidarPoints()
    {
        if (_carTransform == null) return;
        var points = new List<float>();
        Vector3 origin = _carTransform.position + Vector3.up * 0.8f;
        Vector3 fwd = _carTransform.forward;

        float halfAngle = lidarFanAngle * 0.5f;
        for (int i = 0; i < lidarRayCount; i++)
        {
            float angle = (lidarRayCount == 1) ? 0f
                : -halfAngle + (halfAngle * 2f * i / (lidarRayCount - 1));

            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fwd;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, lidarMaxRange, lidarLayerMask))
            {
                Vector3 localHit = _carTransform.InverseTransformPoint(hit.point);
                points.Add(localHit.x);
                points.Add(localHit.y);
                points.Add(localHit.z);
            }
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
        if (_carController == null) return;

        try
        {
            float[] lidarCopy;
            lock (_lidarLock)
            {
                lidarCopy = (float[])_cachedLidarPoints.Clone();
            }

            var state = new VehicleState
            {
                velocity = _carController.GetSpeed(),
                steering_angle = _carController.currentSteeringAngle,
                lidar_points = lidarCopy,
                auto_drive_state = (_autoDrive != null && _autoDrive.enabled)
                    ? _autoDrive.longState.ToString() : "ROS2_Controlled",
                timestamp = Time.time
            };

            string jsonData = JsonUtility.ToJson(state) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(jsonData);

            while (sendQueue.TryDequeue(out _)) { }
            sendQueue.Enqueue(data);
        }
        catch (Exception)
        {
            _isConnected = false;
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
                    _isConnected = false;
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
                _isConnected = false;
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
                    _isConnected = false;
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
            if (string.IsNullOrEmpty(jsonData)) return;

            // 区分消息类型：global_state 消息包含 "type" 字段
            if (jsonData.Contains("\"type\""))
            {
                GlobalState state = JsonUtility.FromJson<GlobalState>(jsonData);
                if (state != null)
                {
                    _rosAebState = state.aeb_state ?? "IDLE";
                    _rosTTC = state.ttc_s;
                    _rosMinDist = state.min_dist_m;
                    _rosSpeedKmh = state.speed_kmh;
                }
                return;
            }

            // 控制指令：linear_velocity / angular_velocity / enable_control
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
        _isConnected = false;

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

        if (_autoDrive != null && !_autoDrive.enabled)
        {
            _autoDrive.enabled = true;
            _autoDrive.ResetNavigation();
        }

        _lastMainCar = null;
    }

    public void Reconnect()
    {
        Debug.Log("[ROS2Bridge] 重连中...");
        _isConnected = false;

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
        _isConnected = false;
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
        public float velocity;
        public float steering_angle;
        public float[] lidar_points;
        public string auto_drive_state;
        public float timestamp;
    }

    [System.Serializable]
    public class ControlCommand
    {
        public float linear_velocity;
        public float angular_velocity;
        public bool enable_control;
    }

    [System.Serializable]
    public class GlobalState
    {
        public string type;
        public string aeb_state;
        public float ttc_s;
        public float min_dist_m;
        public float speed_kmh;
        public bool aeb;
        public bool hud;
        public bool cruise;
        public bool manual_override;
        public float tcp_hz;
    }

    // ==========================================
    // 屏幕通知
    // ==========================================
    void OnGUI()
    {
        // --- 连接成功通知（顶部居中） ---
        if (_connectNotifyTimer > 0f)
        {
            float alpha = Mathf.Min(1f, _connectNotifyTimer / 0.5f);
            GUI.color = new Color(0f, 1f, 0.5f, alpha);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 22;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.UpperCenter;
            style.normal.textColor = new Color(0f, 1f, 0.3f, alpha);

            Rect rect = new Rect(Screen.width / 2f - 200f, 15f, 400f, 40f);
            GUI.Label(rect, "[ROS2 AEB 已连接]", style);
            GUI.color = Color.white;
        }

        // --- AEB急刹警告 ---
        if (_aebActive && _aebWarningTimer > 0f)
        {
            bool flash = (Mathf.FloorToInt(Time.time * 8f) % 2 == 0);
            float alpha = flash ? 1f : 0.3f;
            GUI.color = new Color(1f, 0.05f, 0.05f, alpha);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 48;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = new Color(1f, 0.1f, 0.1f, alpha);

            Rect rect = new Rect(Screen.width / 2f - 250f, Screen.height / 2f - 60f, 500f, 120f);
            GUI.Label(rect, "AEB 紧急制动!", style);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label);
            subStyle.fontSize = 18;
            subStyle.alignment = TextAnchor.MiddleCenter;
            subStyle.normal.textColor = new Color(1f, 0.6f, 0.2f, alpha);
            Rect subRect = new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 60f, 500f, 30f);
            GUI.Label(subRect, "ROS2 触发急停", subStyle);

            GUI.color = Color.white;
        }

        // --- 主车状态 + 连接角标（右上角） ---
        string mainCarName = (_carController != null) ? _carController.name : "未追踪";
        string statusLine = isConnected
            ? $"ROS2 AEB: ONLINE | 主车: {mainCarName}"
            : $"ROS2 AEB: OFFLINE";

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 13;
        statusStyle.alignment = TextAnchor.UpperRight;
        statusStyle.normal.textColor = isConnected
            ? new Color(0.3f, 1f, 0.3f)
            : new Color(1f, 0.4f, 0.3f);
        Rect statusRect = new Rect(Screen.width - 360f, 8f, 350f, 22f);
        GUI.Label(statusRect, statusLine, statusStyle);

        // --- ROS2回传数据（TTC / 速度 / AEB状态） ---
        if (isConnected)
        {
            string aebColor = _rosAebState == "ACTIVE" ? "FF4444" : "88FF88";
            string ttcColor = _rosTTC < 2f ? "FF4444" : (_rosTTC < 5f ? "FFAA00" : "88FF88");
            string infoLine = $"TTC: <color=#{ttcColor}>{_rosTTC:F2}s</color>  |  距离: {_rosMinDist:F1}m  |  速度: {_rosSpeedKmh:F1}km/h  |  AEB: <color=#{aebColor}>{_rosAebState}</color>";

            GUIStyle infoStyle = new GUIStyle(GUI.skin.label);
            infoStyle.fontSize = 12;
            infoStyle.alignment = TextAnchor.UpperRight;
            infoStyle.richText = true;
            infoStyle.normal.textColor = new Color(0.7f, 0.9f, 0.7f);
            Rect infoRect = new Rect(Screen.width - 550f, 28f, 540f, 20f);
            GUI.Label(infoRect, infoLine, infoStyle);
        }
    }
}