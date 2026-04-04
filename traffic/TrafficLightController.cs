using UnityEngine;

/// <summary>
/// 红绿灯控制器
/// 功能：自动循环切换红绿灯状态
/// </summary>
public class TrafficLightController : MonoBehaviour
{
    [Header("灯光配置")]
    public Light redLight;
    public Light yellowLight;
    public Light greenLight;

    [Header("时间配置（秒）")]
    public float redDuration = 10f;
    public float yellowDuration = 3f;
    public float greenDuration = 10f;

    [Header("当前状态")]
    public string currentState = "Red";

    private float timer = 0f;

    void Start()
    {
        SetState("Red");
    }

    void Update()
    {
        timer += Time.deltaTime;

        switch (currentState)
        {
            case "Red":
                if (timer >= redDuration)
                {
                    SetState("Green");
                    timer = 0f;
                }
                break;

            case "Yellow":
                if (timer >= yellowDuration)
                {
                    SetState("Red");
                    timer = 0f;
                }
                break;

            case "Green":
                if (timer >= greenDuration)
                {
                    SetState("Yellow");
                    timer = 0f;
                }
                break;
        }
    }

    void SetState(string state)
    {
        currentState = state;

        // 关闭所有灯
        if (redLight != null) redLight.enabled = false;
        if (yellowLight != null) yellowLight.enabled = false;
        if (greenLight != null) greenLight.enabled = false;

        // 开启对应的灯
        switch (state)
        {
            case "Red":
                if (redLight != null) redLight.enabled = true;
                break;
            case "Yellow":
                if (yellowLight != null) yellowLight.enabled = true;
                break;
            case "Green":
                if (greenLight != null) greenLight.enabled = true;
                break;
        }

        Debug.Log($"红绿灯状态切换为：{state}");
    }

    /// <summary>
    /// 获取当前状态（供传感器调用）
    /// </summary>
    public string GetCurrentState()
    {
        return currentState;
    }

    /// <summary>
    /// 手动设置状态（调试用）
    /// </summary>
    public void ManualSetState(string state)
    {
        SetState(state);
        timer = 0f;
    }
}
