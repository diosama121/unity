using UnityEngine;

public static class CarControlUtility
{
    public static Vector3 SafeNormalize(Vector3 dir, Vector3 fallback)
    {
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : fallback;
    }

    public static float ComputeSpeedFactor(float speed, float maxSpeed)
    {
        float safeMaxSpeed = Mathf.Max(maxSpeed, 0.1f);
        return Mathf.Clamp(Mathf.Pow(1f - (Mathf.Abs(speed) / safeMaxSpeed), 2), 0.2f, 1f);
    }

    public static float ComputeNormalizedSteering(float targetSteering, float maxSteeringAngle)
    {
        float safeMaxSteering = Mathf.Max(maxSteeringAngle, 0.1f);
        return targetSteering / safeMaxSteering;
    }

    public static float SafeDivide(float numerator, float denominator, float safeDenominator = 0.1f)
    {
        return numerator / Mathf.Max(denominator, safeDenominator);
    }
}