using UnityEngine;

/// <summary>
/// 全局天气与路面物理系统
/// 功能：模拟雨雪天气下的路面摩擦力衰减
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    [Header("天气控制")]
    public bool isRaining = false;

    [Header("环境物理参数")]
    public float drySlipFactor = 0.5f;  // 晴天抓地力（正常）
    public float wetSlipFactor = 0.85f; // 雨天抓地力（易打滑）

    private bool lastRainState = false;

    void Start()
    {
        ApplyWeatherPhysics();
    }

    void Update()
    {
        // 如果在 Inspector 中动态勾选了下雨，实时更新物理状态
        if (isRaining != lastRainState)
        {
            ApplyWeatherPhysics();
            lastRainState = isRaining;
            
            Debug.Log(isRaining ? "🌧️ 天气转为雨天，路面变得湿滑！" : "☀️ 晴天，路面抓地力恢复。");
        }
    }

    void ApplyWeatherPhysics()
    {
        float currentSlip = isRaining ? wetSlipFactor : drySlipFactor;

        // 查找场景中所有的车辆控制器，全局修改摩擦力
        SimpleCarController[] allCars = FindObjectsOfType<SimpleCarController>();
        foreach (var car in allCars)
        {
            car.slipFactor = currentSlip;
        }
    }
}