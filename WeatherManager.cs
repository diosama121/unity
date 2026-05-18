using UnityEngine;
using System.Collections.Generic;

public enum WeatherType { Sunny, RainSnow }

public class WeatherManager : MonoBehaviour
{
    [Header("Weather Settings")]
    public WeatherType currentWeather = WeatherType.Sunny;
    public ParticleSystem rainParticleSystem;

    [Header("Vehicle Effects")]
    public float rainSlipFactor = 0.85f;
    public float rainMaxSpeedMultiplier = 0.7f;

    public static WeatherManager Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        SetWeather(WeatherType.Sunny);
    }

    public void SetWeather(WeatherType type)
    {
        currentWeather = type;

        if (rainParticleSystem != null)
        {
            if (type == WeatherType.RainSnow)
            {
                rainParticleSystem.Play();
            }
            else
            {
                rainParticleSystem.Stop();
            }
        }

        TrafficManager tm = FindObjectOfType<TrafficManager>();
        if (tm != null)
        {
            foreach (var npc in tm.ActiveNPCs)
            {
                if (npc == null) continue;
                ApplyWeatherToVehicle(npc.gameObject, type);
            }
        }

        SimpleCarController player = FindObjectOfType<SimpleCarController>();
        if (player != null && !player.isNPC)
        {
            ApplyWeatherToVehicle(player.gameObject, type);
        }

        Debug.Log("[WeatherManager] Weather: " + type);
    }

    void ApplyWeatherToVehicle(GameObject vehicleObj, WeatherType type)
    {
        if (vehicleObj == null) return;

        SimpleCarController ctrl = vehicleObj.GetComponent<SimpleCarController>();
        if (ctrl == null) ctrl = vehicleObj.GetComponentInChildren<SimpleCarController>();
        if (ctrl != null)
        {
            ctrl.slipFactor = (type == WeatherType.RainSnow) ? rainSlipFactor : 0.5f;
        }

        SimpleAutoDrive ad = vehicleObj.GetComponent<SimpleAutoDrive>();
        if (ad == null) ad = vehicleObj.GetComponentInChildren<SimpleAutoDrive>();
        if (ad != null)
        {
            ad.targetSpeed = (type == WeatherType.RainSnow)
                ? ad.targetSpeed * rainMaxSpeedMultiplier
                : ad.targetSpeed;
        }
    }

    public WeatherType GetWeather() { return currentWeather; }
}