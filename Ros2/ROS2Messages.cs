using UnityEngine;

/// <summary>
/// ROS2 通信协议 — 统一消息结构体定义
/// 每条消息以 msg_type 字段区分类型，JSON Lines 编码（\n 分隔）
/// 
/// 消息方向：
///   Unity → ROS2: vehicle_state, traffic_lights, pedestrian_state, heartbeat
///   ROS2 → Unity: control_command, global_state, heartbeat
/// </summary>

// ============================================================
// 消息类型枚举
// ============================================================
public enum ROS2MessageType
{
    Unknown,
    vehicle_state,       // Unity → ROS2: 车辆状态+LiDAR点云
    traffic_lights,      // Unity → ROS2: 红绿灯相位状态
    pedestrian_state,    // Unity → ROS2: 行人位置/速度
    emergency_vehicle,   // Unity → ROS2: 紧急车辆状态
    control_command,     // ROS2 → Unity: 控制指令(AEB等)
    global_state,        // ROS2 → Unity: 全局状态(TTC/AEB)
    heartbeat            // 双向: 心跳保活
}

// ============================================================
// 基础消息包装（所有消息共用 msg_type 字段）
// ============================================================
[System.Serializable]
public class ROS2BaseMessage
{
    public string msg_type;
}

// ============================================================
// Unity → ROS2: 车辆状态 + LiDAR点云
// ============================================================
[System.Serializable]
public class ROS2VehicleState : ROS2BaseMessage
{
    public float velocity;
    public float steering_angle;
    public float[] lidar_points;
    public string auto_drive_state;
    public float timestamp;
}

// ============================================================
// Unity → ROS2: 红绿灯相位状态
// ============================================================
[System.Serializable]
public class ROS2TrafficLightPhase
{
    public int node_id;
    public int phase_id;
    public string state; // RED / YELLOW / GREEN
}

[System.Serializable]
public class ROS2TrafficLights : ROS2BaseMessage
{
    public ROS2TrafficLightPhase[] phases;
}

// ============================================================
// Unity → ROS2: 行人检测数据
// ============================================================
[System.Serializable]
public class ROS2Pedestrian
{
    public int id;
    public float x;
    public float z;
    public float vx;
    public float vz;
}

[System.Serializable]
public class ROS2PedestrianState : ROS2BaseMessage
{
    public ROS2Pedestrian[] pedestrians;
}

// ============================================================
// ROS2 → Unity: 控制指令（AEB）
// ============================================================
[System.Serializable]
public class ROS2ControlCommand : ROS2BaseMessage
{
    public float linear_velocity;
    public float angular_velocity;
    public bool enable_control;
}

// ============================================================
// ROS2 → Unity: 全局状态（TTC / AEB / 速度）
// ============================================================
[System.Serializable]
public class ROS2GlobalState : ROS2BaseMessage
{
    public string aeb_state;      // IDLE / WARNING / BRAKING
    public float ttc_s;
    public float min_dist_m;
    public float speed_kmh;
    public bool aeb;
    public bool hud;
    public bool cruise;
    public bool manual_override;
    public float tcp_hz;
}

// ============================================================
// Unity → ROS2: 紧急车辆状态
// ============================================================
[System.Serializable]
public class ROS2EmergencyVehicleState : ROS2BaseMessage
{
    public bool active;
    public float x;
    public float z;
    public float speed;
    public float heading;
}

// ============================================================
// 双向: 心跳保活
// ============================================================
[System.Serializable]
public class ROS2Heartbeat : ROS2BaseMessage
{
    public float timestamp;
}

// ============================================================
// ★ 兼容旧协议（无 msg_type 字段的旧消息，过渡期保留）
// ============================================================

/// <summary>旧版 VehicleState（无 msg_type，兼容旧ROS2端）</summary>
[System.Serializable]
public class VehicleState
{
    public float velocity;
    public float steering_angle;
    public float[] lidar_points;
    public string auto_drive_state;
    public float timestamp;
}

/// <summary>旧版 ControlCommand（无 msg_type，兼容旧ROS2端）</summary>
[System.Serializable]
public class ControlCommand
{
    public float linear_velocity;
    public float angular_velocity;
    public bool enable_control;
}

/// <summary>旧版 GlobalState（type 字段 = "global_state"，兼容旧ROS2端）</summary>
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