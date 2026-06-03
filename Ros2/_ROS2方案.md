# ROS2 + Unity 自动驾驶仿真联合方案

---

## 一、整体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                         ROS2 端 (Python)                             │
│                                                                      │
│  ┌──────────────────┐   ┌──────────────────┐   ┌────────────────┐  │
│  │  aeb_ttc_node    │   │  traffic_light    │   │  rviz2         │  │
│  │  - 接收LiDAR点云  │   │  _node            │   │  - 可视化点云  │  │
│  │  - 计算TTC        │   │  - 红绿灯相位同步  │   │  - 车辆状态    │  │
│  │  - 发布AEB指令    │   │  - 发布相位状态    │   │  - AEB告警     │  │
│  └────────┬─────────┘   └────────┬─────────┘   └────────────────┘  │
│           │                       │                                   │
│           └───────────┬───────────┘                                   │
│                       │                                               │
│              ┌────────▼────────┐                                     │
│              │  unity_bridge   │  ← TCP Server (端口10086)            │
│              │  - JSON协议解析  │                                     │
│              │  - 双向消息路由  │                                     │
│              │  - 心跳/超时管理 │                                     │
│              └────────┬────────┘                                     │
└───────────────────────┼───────────────────────────────────────────────┘
                        │  TCP Socket (JSON Lines)
┌───────────────────────┼───────────────────────────────────────────────┐
│                       │              Unity 端 (C#)                     │
│              ┌────────▼────────┐                                     │
│              │  ROS2BridgeV2   │  ← TCP Client (挂World)              │
│              │  - 发送LiDAR点云 │                                     │
│              │  - 接收控制指令  │                                     │
│              │  - 接收全局状态  │                                     │
│              └──┬──────────┬───┘                                     │
│                 │          │                                           │
│    ┌────────────▼──┐  ┌───▼──────────────┐                           │
│    │SpecialSituations│  │ MasterUIManager  │                           │
│    │ - 外部AEB触发   │  │ - ROS2状态显示   │                           │
│    │ - 优先级最高    │  │ - AEB警告指示    │                           │
│    └───────┬────────┘  └──────────────────┘                           │
│            │                                                          │
│   ┌────────▼────────┐                                                │
│   │ SimpleAutoDrive │  ← 主车AI控制器                                 │
│   │ - extCmd覆盖    │                                                │
│   │   Subsumption   │                                                │
│   └────────┬────────┘                                                │
│            │                                                          │
│   ┌────────▼────────┐                                                │
│   │SubsumptionEngine│  ← 包容式架构 (L3/L2/L1/L0)                    │
│   └─────────────────┘                                                │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 二、当前状态分析

### 2.1 Unity侧已完成
| 模块 | 文件 | 状态 |
|------|------|------|
| TCP桥接 | `Ros2(waiting)/ROS2BridgeV2.cs` | ✅ 基础通信OK |
| 外部AEB网关 | `Car Control/SpecialSituations.cs` | ✅ 接收ROS2 AEB指令 |
| AEB整合到控制流 | `Car Control/SimpleAutoDrive.cs` L421-426 | ✅ 覆盖包容式架构 |
| ROS2 UI状态 | `UI/MasterUIManager.cs` | ✅ 连接状态+AEB指示灯 |
| 数据导出(含ROS2字段) | `SystemDataManager.cs` | ✅ 报告含ROS2状态 |

### 2.2 当前通信协议
**Unity → ROS2 (VehicleState JSON):**
```json
{
  "velocity": 12.5,
  "steering_angle": 0.3,
  "lidar_points": [x1,y1,z1, x2,y2,z2, ...],
  "auto_drive_state": "FreeDrive",
  "timestamp": 123.45
}
```

**ROS2 → Unity (两种消息):**
- ControlCommand: `{"linear_velocity": -5.0, "angular_velocity": 0.0, "enable_control": true}`
- GlobalState: `{"type": "global_state", "aeb_state": "BRAKING", "ttc_s": 1.2, "min_dist_m": 3.5, "speed_kmh": 45.0, ...}`

### 2.3 待完成
- ROS2端Python节点还没写
- 红绿灯相位同步未实现
- 行人检测数据未发送
- 紧急车辆状态未同步
- 多车LiDAR数据未支持

---

## 三、ROS2端方案 (Python)

### 3.1 文件结构
```
ros2_ws/src/unity_bridge/
├── unity_bridge/
│   ├── __init__.py
│   ├── tcp_server.py          # TCP服务器（与Unity通信）
│   ├── aeb_ttc_node.py        # AEB TTC计算节点
│   ├── traffic_light_node.py  # 红绿灯相位发布节点
│   └── vehicle_state_pub.py   # 车辆状态发布（ROS2 topic）
├── msg/
│   ├── VehicleState.msg
│   ├── AEBCommand.msg
│   ├── LidarPoints.msg
│   └── TrafficLightPhase.msg
├── launch/
│   └── unity_bridge.launch.py
├── setup.py
└── package.xml
```

### 3.2 核心节点

#### 3.2.1 tcp_server.py — TCP桥接服务器
```
职责：
- 监听端口 10086，接受Unity连接
- 解析JSON Lines协议
- 分发消息到各处理节点（通过ROS2 topic）
- 聚合ROS2 topic消息，回传Unity
- 心跳检测（2s超时断连）

输入topic：
  /unity/vehicle_state      ← 车辆状态（从TCP解析）
  /unity/lidar_points       ← LiDAR点云（从TCP解析）

输出topic：
  /unity/aeb_command        → AEB控制指令
  /unity/global_state       → 全局状态
  /unity/traffic_lights     → 红绿灯相位
```

#### 3.2.2 aeb_ttc_node.py — AEB/TTC计算
```
职责：
- 订阅 /unity/lidar_points
- 计算TTC（Time to Collision）
- 计算最小距离
- 发布AEB状态（IDLE / WARNING / BRAKING）

输入topic：
  /unity/lidar_points
  /unity/vehicle_state

输出topic：
  /unity/aeb_command
  /unity/global_state

算法：
  TTC = min_dist / relative_velocity
  if TTC < 1.0s → BRAKING (紧急制动)
  if TTC < 2.5s → WARNING (预警)
  else → IDLE (正常)
```

#### 3.2.3 traffic_light_node.py — 红绿灯相位同步
```
职责：
- 接收Unity发送的红绿灯相位状态
- 发布到ROS2 /traffic_lights topic
- 供RViz2可视化和其他规划节点使用

输入topic：
  /unity/traffic_lights

输出topic：
  /traffic_lights/phase
```

### 3.3 自定义消息类型

**VehicleState.msg:**
```
float32 velocity
float32 steering_angle
float32[] lidar_points
string auto_drive_state
float32 timestamp
```

**AEBCommand.msg:**
```
float32 linear_velocity
float32 angular_velocity
bool enable_control
```

**LidarPoints.msg:**
```
std_msgs/Header header
float32[] points       # 扁平数组 [x1,y1,z1, x2,y2,z2, ...]
int32 point_count
```

**TrafficLightPhase.msg:**
```
int32 node_id
int32 phase_id
string state           # RED / YELLOW / GREEN
```

---

## 四、Unity侧方案

### 4.1 当前问题
1. `ROS2BridgeV2.cs` 放在 `Ros2(waiting)/` 目录 — 名字暗示"等待中"
2. 缺少红绿灯相位上报
3. 缺少行人检测数据上报
4. 缺少紧急车辆状态上报
5. 缺少消息类型标记（type字段），解析靠contains猜测

### 4.2 改进计划

#### 4.2.1 目录整理
```
Ros2(waiting)/  →  Ros2/   （正式启用）
```

#### 4.2.2 消息协议增强
每条消息增加 `"msg_type"` 字段，明确区分消息类型：

| msg_type | 方向 | 说明 |
|----------|------|------|
| vehicle_state | Unity→ROS2 | 车辆状态+LiDAR点云 |
| traffic_lights | Unity→ROS2 | 红绿灯相位状态 |
| pedestrian_state | Unity→ROS2 | 行人位置/速度 |
| emergency_vehicle | Unity→ROS2 | 紧急车辆状态 |
| control_command | ROS2→Unity | 控制指令(AEB等) |
| global_state | ROS2→Unity | 全局状态(TTC/AEB) |
| heartbeat | 双向 | 心跳保活 |

#### 4.2.3 新增发送内容

**红绿灯相位上报** (新增到 `SendVehicleState` 或独立发送):
```json
{
  "msg_type": "traffic_lights",
  "phases": [
    {"node_id": 5, "phase_id": 50, "state": "RED"},
    {"node_id": 5, "phase_id": 51, "state": "GREEN"}
  ]
}
```

**行人检测数据** (新增):
```json
{
  "msg_type": "pedestrian_state",
  "pedestrians": [
    {"id": 0, "x": 10.5, "z": 20.3, "vx": 1.2, "vz": 0.0}
  ]
}
```

#### 4.2.4 代码改造清单

| 文件 | 改动 |
|------|------|
| `Ros2/ROS2BridgeV2.cs` | 1. 添加msg_type字段 2. 新增红绿灯/行人上报 3. 心跳机制 |
| `Ros2/ROS2Messages.cs` | 新建：统一消息结构体定义 |
| `Car Control/SpecialSituations.cs` | 无需改动（已完善） |
| `Car Control/SimpleAutoDrive.cs` | 无需改动（已整合） |
| `UI/MasterUIManager.cs` | 添加ROS2心跳指示/延迟显示 |

---

## 五、实施步骤

### Phase 1: 基础通信稳定（当前）
- [x] TCP连接/重连
- [x] 车辆状态发送
- [x] LiDAR点云发送
- [x] AEB指令接收
- [x] 全局状态接收
- [x] AEB整合到控制流
- [x] UI状态显示

### Phase 2: ROS2端搭建（下一步）
- [ ] 创建ROS2 package
- [ ] 实现 tcp_server.py
- [ ] 实现 aeb_ttc_node.py
- [ ] 编写 launch 文件
- [ ] 端到端测试：Unity ↔ ROS2

### Phase 3: 功能扩展
- [ ] 红绿灯相位同步到ROS2
- [ ] 行人数据上报
- [ ] 紧急车辆状态同步
- [ ] 多车LiDAR支持
- [ ] RViz2可视化

### Phase 4: 性能优化
- [ ] JSON → MessagePack/Protobuf 二进制协议
- [ ] 发送频率自适应（根据网络延迟）
- [ ] 数据压缩（LiDAR点云）

---

## 六、通信协议详细规范

### 6.1 传输层
- 协议：TCP
- 端口：10086（可配置）
- 编码：UTF-8 JSON Lines（每条消息以 `\n` 分隔）
- 心跳：每1秒发送 `{"msg_type":"heartbeat"}`

### 6.2 消息格式

**Unity → ROS2:**
```json
{"msg_type":"vehicle_state","velocity":12.5,"steering_angle":0.3,"lidar_points":[...],"auto_drive_state":"FreeDrive","timestamp":123.45}
{"msg_type":"traffic_lights","phases":[{"node_id":5,"phase_id":50,"state":"RED"}]}
{"msg_type":"pedestrian_state","pedestrians":[{"id":0,"x":10.5,"z":20.3,"vx":1.2,"vz":0.0}]}
{"msg_type":"heartbeat"}
```

**ROS2 → Unity:**
```json
{"msg_type":"control_command","linear_velocity":-5.0,"angular_velocity":0.0,"enable_control":true}
{"msg_type":"global_state","aeb_state":"BRAKING","ttc_s":1.2,"min_dist_m":3.5,"speed_kmh":45.0}
{"msg_type":"heartbeat"}
```

---

## 七、控制流优先级（最终形态）

```
ROS2 AEB 指令 (最高优先级)
    ↓
SpecialSituations.Handle()
    ↓ (extCmd.HasValue → 覆盖)
SubsumptionEngine.Resolve()
    ├── L3: 紧急制动 (A3)
    ├── L2: 跟车避障 (A2)
    ├── L1: 交通规则 (A1)
    └── L0: 基础巡航 (A0)
    ↓
SimpleAutoDrive.Update()
    ↓
SimpleCarController.ApplyCommand()
```

---

## 八、下一步行动

1. **立即**: 将 `Ros2(waiting)/` 重命名为 `Ros2/`，正式启用
2. **立即**: 在 `ROS2BridgeV2.cs` 中添加 `msg_type` 字段
3. **短期**: 开始编写 ROS2 端 Python 代码
4. **中期**: 红绿灯相位同步 + 行人数据上报
5. **长期**: 二进制协议 + 性能优化