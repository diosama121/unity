using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ROS2Bridge : MonoBehaviour
{
    [Header("ROS2 Connection")]
    public string rosIP = "172.21.16.202";  // WSL 使用 localhost
    public int rosPort = 10086;

    [Header("Vehicle Status (Send to ROS2)")]
    public Vector3 vehiclePosition;
    public Vector3 vehicleVelocity;

    [Header("Control Command (Receive from ROS2)")]
    public float linearVelocity = 0f;
    public float angularVelocity = 0f;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
private float sendInterval = 0.1f;  // 添加这行到类的开头
private float lastSendTime = 0f;    // 添加这行到类的开头


    void Start()
    {
        ConnectToROS2();
    }

   void ConnectToROS2()
{
    try
    {
        Debug.Log($"🔌 Attempting to connect to {rosIP}:{rosPort}...");
        
        client = new TcpClient();
        client.Connect(rosIP, rosPort);
        stream = client.GetStream();
        isConnected = true;

        Debug.Log($"✅ TCP connection established to {rosIP}:{rosPort}");

        // 启动接收线程
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
        
        Debug.Log("📡 Receive thread started");
    }
    catch (Exception e)
    {
        Debug.LogError($" Failed to connect to ROS2: {e.Message}");
    }
}


void Update()
{
    if (isConnected && Time.time - lastSendTime > sendInterval)
    {
        // 每 0.1 秒发送一次车辆状态到 ROS2
        SendVehicleStatus();
        lastSendTime = Time.time;
    }
}
  void SendVehicleStatus()
{
    if (!isConnected || stream == null || !stream.CanWrite)
    {
        Debug.LogWarning("Stream not available");
        return;
    }
    
    try
    {
        // 构建 JSON 数据
        string jsonData = $@"{{""position"":{{""x"":{vehiclePosition.x},""y"":{vehiclePosition.y},""z"":{vehiclePosition.z}}},""velocity"":{{""x"":{vehicleVelocity.x},""y"":{vehicleVelocity.y},""z"":{vehicleVelocity.z}}}}}";

        // 添加换行符
        jsonData += "\n";

        byte[] data = Encoding.UTF8.GetBytes(jsonData);
        stream.Write(data, 0, data.Length);
        stream.Flush();  // 添加这行，强制发送
    }
    catch (Exception e)
    {
        Debug.LogError($"Error sending data: {e.Message}");
        isConnected = false;
    }
}

    void ReceiveData()
    {
        byte[] buffer = new byte[1024];
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

                    // 处理完整的 JSON 消息（以换行符分隔）
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
                Debug.LogError($"Error receiving data: {e.Message}");
                isConnected = false;
                break;
            }
        }
    }

    void ProcessControlCommand(string jsonData)
    {
        try
        {
            // 简单的 JSON 解析（生产环境建议用 JsonUtility 或 Newtonsoft.Json）
            if (jsonData.Contains("linear_velocity"))
            {
                // 提取数值
                int linearStart = jsonData.IndexOf("linear_velocity") + 18;
                int linearEnd = jsonData.IndexOf(",", linearStart);
                string linearStr = jsonData.Substring(linearStart, linearEnd - linearStart);
                linearVelocity = float.Parse(linearStr);

                int angularStart = jsonData.IndexOf("angular_velocity") + 19;
                int angularEnd = jsonData.IndexOf("}", angularStart);
                string angularStr = jsonData.Substring(angularStart, angularEnd - angularStart);
                angularVelocity = float.Parse(angularStr);

                Debug.Log($"📡 Received Control: Linear={linearVelocity}, Angular={angularVelocity}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing control command: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        isConnected = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
        }

        if (stream != null)
        {
            stream.Close();
        }

        if (client != null)
        {
            client.Close();
        }

        Debug.Log("🔌 Disconnected from ROS2");
    }
}
