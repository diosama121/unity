using UnityEngine;

/// <summary>
/// SimpleCarController 玩家输入处理（partial class）
/// 包含 WASD 手动接管、N 键重置导航、R 键回归初始位置
/// </summary>
public partial class SimpleCarController : MonoBehaviour
{
    /// <summary>
    /// 处理玩家键盘输入：WASD临时接管、N键重置导航、R键回归初始位置
    /// 在 Update() 开头调用
    /// </summary>
    void HandlePlayerInput()
    {
        // WASD 手动操控：临时接管，不永久修改autoMode
        // 松开WASD后自动恢复之前的autoMode状态
        bool wasdActive = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        if (wasdActive)
        {
            if (!wasdOverride)
            {
                // 第一帧按下WASD：保存当前autoMode，临时切手动
                wasdOverride = true;
                autoModeBeforeOverride = autoMode;
                autoMode = false;
            }
            float t = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float s = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            SetAutoControl(t, s);
        }
        else if (wasdOverride)
        {
            // WASD松开：恢复之前的autoMode状态
            wasdOverride = false;
            autoMode = autoModeBeforeOverride;
        }

        // R键：回归初始位置
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetPosition();
        }

        // N键：重置导航路径
        if (Input.GetKeyDown(KeyCode.N))
        {
            wasdOverride = false;
            autoMode = true;
            SetAutoBrake(0f);
            SimpleAutoDrive autoDrive = GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = FindObjectOfType<SimpleAutoDrive>();
            if (autoDrive != null) autoDrive.ResetNavigation();
        }
    }
}