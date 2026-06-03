#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
ROS2 - Unity AEB Bridge
最终论文答辩演示版

功能：
1. Unity <-> ROS2 TCP通信
2. LiDAR点云接收
3. AEB碰撞检测
4. TTC(Time To Collision)计算
5. teleop_twist_keyboard 遥操作支持
6. 自动接管控制
7. HUD实时监控
8. ROS2 Topic发布
9. TCP实时频率统计
10. 自动重连支持
11. 全局状态同步包 (aeb/hud/cruise/manual_override)

推荐搭配：
ros2 run teleop_twist_keyboard teleop_twist_keyboard
"""

import rclpy
from rclpy.node import Node

from geometry_msgs.msg import Twist
from std_msgs.msg import String

import socket
import json
import threading
import traceback
import math
import time
import sys
import termios
import tty

# =========================================================
# 控制台颜色
# =========================================================

COLOR_GREEN  = '\033[92m'
COLOR_RED    = '\033[91m'
COLOR_YELLOW = '\033[93m'
COLOR_CYAN   = '\033[96m'
COLOR_RESET  = '\033[0m'

# =========================================================
# 主节点
# =========================================================

class UnityAEBBridge(Node):

    def __init__(self):

        super().__init__('unity_aeb_bridge')

        # =====================================================
        # stdout 线程锁 —— 防止 HUD 与键盘输入互相覆盖
        # =====================================================

        self._print_lock = threading.Lock()

        # =====================================================
        # TCP配置
        # =====================================================

        self.host = '0.0.0.0'
        self.port = 10086

        self.server_socket  = None
        self.client_socket  = None
        self.client_address = None

        self.is_connected = False

        # =====================================================
        # ROS2 Topic
        # =====================================================

        self.state_publisher = self.create_publisher(
            String,
            '/unity/vehicle_state',
            10
        )

        self.cmd_subscriber = self.create_subscription(
            Twist,
            '/cmd_vel',
            self.cmd_callback,
            10
        )

        # =====================================================
        # 控制模式
        # =====================================================

        self.control_mode = "MANUAL"

        self.enable_aeb = True

        self.manual_linear  = 0.0
        self.manual_angular = 0.0

        # =====================================================
        # AEB参数
        # =====================================================

        self.aeb_state = "SAFE"

        self.warning_ttc = 3.0
        self.brake_ttc   = 1.5

        self.cruise_speed = 8.0

        self.min_distance = 999.0
        self.current_speed = 0.0
        self.current_ttc   = 999.0

        self.hit_count = 0

        # =====================================================
        # HUD参数
        # =====================================================

        self.last_hud_time = 0.0

        self.packet_counter = 0
        self.last_fps_time  = time.time()
        self.current_hz     = 0

        self.show_hud = True

        # =====================================================
        # 巡航控制
        # =====================================================

        self.cruise_enabled = False

        # =====================================================
        # 全局状态同步 —— 上一次发给 Unity 的状态快照
        # 只有状态真正改变时才重新发送，避免刷包
        # =====================================================

        self._last_sent_global_state = {}

        # 全局状态定时发送间隔（变化量每间隔发送一次，避免每帧都发）
        self._last_global_state_time = 0.0
        self._global_state_interval = 0.5  # 秒

        # =====================================================
        # 启动TCP服务器
        # =====================================================

        self.start_tcp_server()

        # =====================================================
        # 键盘模式切换线程（无回显，不干扰 HUD）
        # =====================================================

        threading.Thread(
            target=self.keyboard_input_loop,
            daemon=True
        ).start()

        # =====================================================
        # 启动信息
        # =====================================================

        self.get_logger().info(
            f'{COLOR_GREEN} Unity AEB Bridge Started @ {self.host}:{self.port}{COLOR_RESET}\n'
            f'{COLOR_CYAN}启动 teleop 键盘控制：\n'
            f'ros2 run teleop_twist_keyboard teleop_twist_keyboard{COLOR_RESET}'
        )

    # =========================================================
    # 线程安全打印
    # =========================================================

    def _safe_print(self, *args, **kwargs):
        with self._print_lock:
            print(*args, **kwargs)

    # =========================================================
    # TCP服务器
    # =========================================================

    def start_tcp_server(self):

        try:

            self.server_socket = socket.socket(
                socket.AF_INET,
                socket.SOCK_STREAM
            )

            self.server_socket.setsockopt(
                socket.SOL_SOCKET,
                socket.SO_REUSEADDR,
                1
            )

            self.server_socket.bind((self.host, self.port))
            self.server_socket.listen(1)

            threading.Thread(
                target=self.accept_connections,
                daemon=True
            ).start()

        except Exception as e:

            self.get_logger().error(f'Server start failed: {e}')
            traceback.print_exc()

    # =========================================================
    # 接受连接
    # =========================================================

    def accept_connections(self):

        self.get_logger().info('⏳ Waiting for Unity connection...')

        while rclpy.ok():

            try:

                client_socket, client_address = (
                    self.server_socket.accept()
                )

                self.client_socket  = client_socket
                self.client_address = client_address
                self.is_connected   = True

                self.get_logger().info(
                    f'{COLOR_GREEN}✅ Unity Connected: {client_address}{COLOR_RESET}'
                )

                # 连接后立刻推送一次全局状态
                self._push_global_state(force=True)

                self.receive_data()

            except Exception as e:
                self.get_logger().error(f'Accept Error: {e}')

    # =========================================================
    # 接收Unity数据
    # =========================================================

    def receive_data(self):

        buffer = ""

        while rclpy.ok() and self.is_connected:

            try:

                data = self.client_socket.recv(4096)

                if not data:
                    break

                buffer += data.decode('utf-8')

                while '\n' in buffer:

                    line, buffer = buffer.split('\n', 1)
                    line = line.strip()

                    if line:
                        self.process_unity_data(line)

            except ConnectionResetError:
                break

            except Exception as e:

                self.get_logger().error(f'Receive Error: {e}')
                traceback.print_exc()
                break

        self.cleanup_connection()

    # =========================================================
    # 处理Unity数据
    # =========================================================

    def process_unity_data(self, data):

        try:

            self.packet_counter += 1

            # -------------------------------------------------
            # JSON解析
            # -------------------------------------------------

            state = json.loads(data)

            # -------------------------------------------------
            # ★ 消息路由：根据 msg_type 分发
            # -------------------------------------------------

            msg_type = state.get('msg_type', '')

            # 心跳：只回传时间戳，不污染车辆状态
            if msg_type == 'heartbeat':
                self._send_json({
                    'msg_type': 'heartbeat',
                    'timestamp': state.get('timestamp', time.time())
                })
                return

            # 新消息类型：暂不处理，只发ROS2 Topic
            if msg_type in ('traffic_lights', 'pedestrian_state', 'emergency_vehicle'):
                ros_msg      = String()
                ros_msg.data = data
                self.state_publisher.publish(ros_msg)
                return

            # -------------------------------------------------
            # 以下是车辆状态消息（vehicle_state 或无 msg_type）
            # -------------------------------------------------

            # ROS2发布
            ros_msg      = String()
            ros_msg.data = data
            self.state_publisher.publish(ros_msg)

            # -------------------------------------------------
            # 读取车辆状态
            # -------------------------------------------------

            self.current_speed = float(state.get('velocity', 0.0)) * 3.6

            points = state.get('lidar_points', [])

            # -------------------------------------------------
            # 计算最近障碍距离
            # -------------------------------------------------

            distances      = []
            self.hit_count = 0

            for i in range(0, len(points), 3):

                if i + 2 >= len(points):
                    break

                x = points[i]
                y = points[i + 1]
                z = points[i + 2]

                dist = math.sqrt(x * x + y * y + z * z)
                distances.append(dist)
                self.hit_count += 1

            self.min_distance = min(distances) if distances else 999.0

            # -------------------------------------------------
            # TTC计算
            # -------------------------------------------------

            speed_ms       = max(self.current_speed / 3.6, 0.01)
            self.current_ttc = self.min_distance / speed_ms
            if self.current_ttc > 999.0:
                self.current_ttc = 999.0

            # -------------------------------------------------
            # AEB状态机
            # -------------------------------------------------

            prev_aeb_state = self.aeb_state

            if self.enable_aeb and self.current_ttc < self.brake_ttc:

                self.aeb_state    = "BRAKING"
                self.control_mode = "AEB"

            elif self.current_ttc < self.warning_ttc:

                self.aeb_state    = "WARNING"
                self.control_mode = "MANUAL"

            else:

                self.aeb_state    = "SAFE"
                self.control_mode = "MANUAL"

            # -------------------------------------------------
            # 统计TCP频率
            # -------------------------------------------------

            now = time.time()

            if now - self.last_fps_time >= 1.0:

                self.current_hz     = self.packet_counter
                self.packet_counter = 0
                self.last_fps_time  = now

            # -------------------------------------------------
            # 打印HUD（有锁保护）
            # -------------------------------------------------

            if now - self.last_hud_time > 0.2:

                if self.show_hud:
                    self.print_hud()

                self.last_hud_time = now

            # -------------------------------------------------
            # 下发控制指令
            # -------------------------------------------------

            self.send_control_to_unity()

            # -------------------------------------------------
            # 全局状态同步（AEB状态变化时立刻推送，否则定时推送）
            # -------------------------------------------------

            if self.aeb_state != prev_aeb_state:
                self._push_global_state(force=True)
            else:
                self._push_global_state(force=False)

        except json.JSONDecodeError:
            pass

        except Exception as e:

            self.get_logger().error(f'Process Error: {e}')
            traceback.print_exc()

    # =========================================================
    # ROS2 teleop输入
    # =========================================================

    def cmd_callback(self, msg):

        self.manual_linear  = max(min(float(msg.linear.x), 8.0), -3.0)
        self.manual_angular = float(msg.angular.z)

    # =========================================================
    # 发送控制到Unity
    # =========================================================

    def send_control_to_unity(self):

        if not self.is_connected or self.client_socket is None:
            return

        try:

            # -------------------------------------------------
            # AEB接管
            # -------------------------------------------------

            if self.control_mode == "AEB":

                linear_velocity  = -3.0
                angular_velocity = 0.0
                enable_control   = True

            # -------------------------------------------------
            # teleop / 巡航控制
            # -------------------------------------------------

            else:

                if self.cruise_enabled:
                    linear_velocity  = self.cruise_speed
                    angular_velocity = 0.0
                else:
                    linear_velocity  = self.manual_linear
                    angular_velocity = self.manual_angular

                enable_control = True

            # -------------------------------------------------
            # 控制指令 JSON
            # -------------------------------------------------

            cmd = {
                "linear_velocity":  linear_velocity,
                "angular_velocity": angular_velocity,
                "enable_control":   enable_control
            }

            self._send_json(cmd)

        except BrokenPipeError:
            self.cleanup_connection()

        except Exception as e:
            self.get_logger().error(f'Send Error: {e}')
            self.cleanup_connection()

    # =========================================================
    # 全局状态同步包
    # =========================================================
    #
    #  Unity 每帧可以读这个包来同步所有模块状态：
    #
    #  {
    #    "type":            "global_state",   ← 包类型标识
    #    "aeb":             bool,             ← AEB 是否启用
    #    "aeb_state":       str,              ← SAFE / WARNING / BRAKING
    #    "hud":             bool,             ← HUD 是否显示
    #    "cruise":          bool,             ← 巡航是否开启
    #    "manual_override": bool,             ← 人工接管（teleop）
    #    "speed_kmh":       float,            ← 当前速度 km/h
    #    "min_dist_m":      float,            ← 最近障碍距离 m
    #    "ttc_s":           float,            ← TTC 秒
    #    "tcp_hz":          int               ← 当前 TCP 频率
    #  }
    #
    # =========================================================

    def _build_global_state(self) -> dict:

        return {
            "type":            "global_state",
            "aeb":             self.enable_aeb,
            "aeb_state":       self.aeb_state,
            "hud":             self.show_hud,
            "cruise":          self.cruise_enabled,
            "manual_override": (
                self.control_mode == "MANUAL" and
                not self.cruise_enabled
            ),
            "speed_kmh":       round(self.current_speed, 2),
            "min_dist_m":      round(self.min_distance, 3),
            "ttc_s":           round(self.current_ttc, 3),
            "tcp_hz":          self.current_hz
        }

    def _push_global_state(self, force: bool = False):
        """
        向 Unity 发送全局状态包。
        force=True  → 无论状态是否改变都立刻发
        force=False → 定时发送（节省带宽），且状态变化时发送
        """

        if not self.is_connected or self.client_socket is None:
            return

        state = self._build_global_state()

        # 定时发送：每 _global_state_interval 秒发一次
        now = time.time()
        if not force and now - self._last_global_state_time < self._global_state_interval:
            return

        # 非强制模式下，状态没变就不发
        if not force and state == self._last_sent_global_state:
            return

        self._last_sent_global_state = state.copy()
        self._last_global_state_time = now

        try:
            self._send_json(state)

        except Exception as e:
            self.get_logger().error(f'GlobalState Send Error: {e}')

    # =========================================================
    # 底层 JSON 发送（复用）
    # =========================================================

    def _send_json(self, obj: dict):
        """将 dict 序列化为 JSON+换行后发往 Unity。"""
        message = json.dumps(obj) + '\n'
        self.client_socket.sendall(message.encode('utf-8'))

    # =========================================================
    # 键盘模式切换（无回显 + 有锁，不干扰 HUD）
    # =========================================================

    def keyboard_input_loop(self):

        # 打印帮助信息时先拿锁
        with self._print_lock:
            print("\n==============================")
            print("Bridge Keyboard Control")
            print("==============================")
            print("a = 开启AEB")
            print("m = 关闭AEB（纯人工驾驶）")
            print("c = 一键巡航")
            print("x = 取消巡航")
            print("h = 开关HUD")
            print("q = 退出Bridge")
            print("==============================\n")

        # 保存原始终端设置
        fd = sys.stdin.fileno()

        try:
            old_settings = termios.tcgetattr(fd)
        except Exception:
            # 非 TTY 环境（如重定向）退回普通 input() 模式
            self._keyboard_fallback_loop()
            return

        try:
            # 切换为 cbreak 模式：每次读一个字符，不需要回车，不回显
            tty.setcbreak(fd)

            while True:

                try:
                    cmd = sys.stdin.read(1).strip().lower()
                except Exception:
                    break

                prev_state = (
                    self.enable_aeb,
                    self.cruise_enabled,
                    self.show_hud
                )

                # AEB
                if cmd == 'a':
                    self.enable_aeb = True
                    self._safe_print(f"\n{COLOR_GREEN}[MODE] AEB ENABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                # 纯人工
                elif cmd == 'm':
                    self.enable_aeb   = False
                    self.control_mode = "MANUAL"
                    self._safe_print(f"\n{COLOR_YELLOW}[MODE] MANUAL ONLY{COLOR_RESET}")
                    self._push_global_state(force=True)

                # 巡航
                elif cmd == 'c':
                    self.cruise_enabled = True
                    self._safe_print(f"\n{COLOR_CYAN}[CRUISE] ENABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                # 取消巡航
                elif cmd == 'x':
                    self.cruise_enabled = False
                    self._safe_print(f"\n{COLOR_YELLOW}[CRUISE] DISABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                # HUD开关
                elif cmd == 'h':
                    self.show_hud = not self.show_hud
                    self._safe_print(
                        f"\n{COLOR_CYAN}[HUD] {'ON' if self.show_hud else 'OFF'}{COLOR_RESET}"
                    )
                    self._push_global_state(force=True)

                # 退出
                elif cmd == 'q':
                    self._safe_print(
                        f"\n{COLOR_RED}[SYSTEM] EXIT BRIDGE{COLOR_RESET}"
                    )
                    self.destroy_node()
                    if rclpy.ok():
                        rclpy.shutdown()
                    sys.exit(0)

        finally:
            # 无论如何都还原终端设置
            termios.tcsetattr(fd, termios.TCSADRAIN, old_settings)

    def _keyboard_fallback_loop(self):
        """非 TTY 环境下的备用键盘循环（使用 input()）。"""
        while True:
            try:
                cmd = input().strip().lower()

                if cmd == 'a':
                    self.enable_aeb = True
                    self._safe_print(f"{COLOR_GREEN}[MODE] AEB ENABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                elif cmd == 'm':
                    self.enable_aeb   = False
                    self.control_mode = "MANUAL"
                    self._safe_print(f"{COLOR_YELLOW}[MODE] MANUAL ONLY{COLOR_RESET}")
                    self._push_global_state(force=True)

                elif cmd == 'c':
                    self.cruise_enabled = True
                    self._safe_print(f"{COLOR_CYAN}[CRUISE] ENABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                elif cmd == 'x':
                    self.cruise_enabled = False
                    self._safe_print(f"{COLOR_YELLOW}[CRUISE] DISABLED{COLOR_RESET}")
                    self._push_global_state(force=True)

                elif cmd == 'h':
                    self.show_hud = not self.show_hud
                    self._safe_print(
                        f"{COLOR_CYAN}[HUD] {'ON' if self.show_hud else 'OFF'}{COLOR_RESET}"
                    )
                    self._push_global_state(force=True)

                elif cmd == 'q':
                    self._safe_print(f"{COLOR_RED}[SYSTEM] EXIT BRIDGE{COLOR_RESET}")
                    self.destroy_node()
                    if rclpy.ok():
                        rclpy.shutdown()
                    sys.exit(0)

            except Exception:
                pass

    # =========================================================
    # HUD打印（有锁保护）
    # =========================================================

    def print_hud(self):

        # 风险评分
        risk_score = max(0, min(100, int(100 - self.min_distance * 8)))

        # 状态颜色
        if self.aeb_state == "SAFE":
            color = COLOR_GREEN
        elif self.aeb_state == "WARNING":
            color = COLOR_YELLOW
        else:
            color = COLOR_RED

        # 连接状态
        connection_text = (
            f"{COLOR_GREEN}CONNECTED{COLOR_RESET}"
            if self.is_connected else
            f"{COLOR_RED}DISCONNECTED{COLOR_RESET}"
        )

        # 模式状态
        mode_text = (
            f"{COLOR_GREEN}AEB ENABLED{COLOR_RESET}"
            if self.enable_aeb else
            f"{COLOR_YELLOW}MANUAL ONLY{COLOR_RESET}"
        )

        # 指令状态
        if self.aeb_state == "BRAKING":
            cmd_text = f"{COLOR_RED}EMERGENCY BRAKE{COLOR_RESET}"
        elif self.aeb_state == "WARNING":
            cmd_text = f"{COLOR_YELLOW}WARNING / SLOWDOWN{COLOR_RESET}"
        else:
            cmd_text = f"{COLOR_GREEN}SAFE CRUISE{COLOR_RESET}"

        # 全局状态小摘要（方便确认 Unity 收到的内容）
        gs = self._build_global_state()
        global_state_line = (
            f'  aeb={gs["aeb"]}  '
            f'cruise={gs["cruise"]}  '
            f'manual_override={gs["manual_override"]}  '
            f'hud={gs["hud"]}'
        )

        # ── 拿锁后一次性写完整个 HUD，避免与键盘线程交错 ──
        with self._print_lock:

            print("\033[2J\033[H", end="")   # 清屏并移到左上角

            print("=================================================")
            print("          ROS2 - UNITY AEB MONITOR")
            print("=================================================\n")

            print(f"Connection  : {connection_text}")
            print(f"TCP Rate    : {self.current_hz} Hz")
            print(f"Mode        : {mode_text}")
            print(f"AEB State   : {color}{self.aeb_state}{COLOR_RESET}\n")
            print(f"Speed       : {self.current_speed:.1f} km/h")
            print(f"Min Dist    : {self.min_distance:.2f} m")
            print(f"TTC         : {self.current_ttc:.2f} s")
            print(f"Risk Score  : {risk_score}/100")
            print(f"LiDAR Hits  : {self.hit_count}\n")

            print(f"Command     : {cmd_text}")

            print("\n── Global State → Unity ─────────────────────────")
            print(global_state_line)
            print("─────────────────────────────────────────────────")

            print("\n=================================================")
            print("Keyboard Control")
            print("=================================================\n")
            print("teleop:  i/j/k/l = drive vehicle\n")
            print("bridge:  a=AEB on | m=manual | c=cruise | x=no cruise | h=HUD | q=quit")
            print("\n=================================================")

            sys.stdout.flush()

    # =========================================================
    # 清理连接
    # =========================================================

    def cleanup_connection(self):

        self.is_connected = False

        if self.client_socket:

            try:
                self.client_socket.close()
            except Exception:
                pass

            self.client_socket = None

        self.get_logger().warn(
            '⚠️  Unity Disconnected. Waiting Reconnect...'
        )

    # =========================================================
    # 销毁节点
    # =========================================================

    def destroy_node(self):

        self.is_connected = False

        if self.client_socket:
            try:
                self.client_socket.close()
            except Exception:
                pass

        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception:
                pass

        super().destroy_node()

# =============================================================
# Main
# =============================================================

def main(args=None):

    rclpy.init(args=args)

    node = UnityAEBBridge()

    try:
        rclpy.spin(node)

    except KeyboardInterrupt:
        pass

    finally:

        node.destroy_node()

        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()