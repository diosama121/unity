using UnityEngine;

public partial class SimpleAutoDrive : MonoBehaviour
{
    void HandleFollowingState()
    {
        if (currentSpline == null || currentSpline.TotalLength < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            RequestNewRandomPath();
            if (currentSpline == null)
            {
                currentState = DriveState.Idle;
                return;
            }
        }

        if (obstacleDetected && avoidCooldown <= 0f)
        {
            AppendThought("Obstacle detected < " + safeDistance.ToString("F1") + "m -> Switch to Avoiding");
            currentState = DriveState.Avoiding;
            return;
        }

        if (currentIntersectionState == IntersectionState.RedLight || currentIntersectionState == IntersectionState.YellowLight)
        {
            AppendThought("Traffic light " + currentIntersectionState + " -> Stopping");
            int stopNodeId = (nearestIntersectionNodeId >= 0) ? nearestIntersectionNodeId : currentDestinationNodeId;
            if (stopNodeId >= 0 && WorldModel.Instance != null)
            {
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(stopNodeId, transform.position);
                if (relevantStopLine != null)
                {
                    stopTargetPosition = relevantStopLine.Position;
                    hasStopTarget = true;
                }
            }
            currentState = DriveState.Stopping;
            return;
        }

        if (currentT >= 1.0f)
        {
            RequestNewRandomPath();
            return;
        }

        FollowPath();
    }

    void HandleAvoidingState()
    {
        if (currentSpline == null || currentSpline.TotalLength < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            currentState = DriveState.Idle;
            return;
        }

        Vector3 splinePos = currentSpline.GetPoint(Mathf.Clamp01(currentT));
        Vector3 toCar = transform.position - splinePos;
        toCar.y = 0;
        float lateralDist = toCar.magnitude;
        float maxLateral = 6f;

        if (lateralDist > maxLateral && isReversing)
        {
            isReversing = false;
            reverseCount = 0;
            reverseTimer = 0f;
            carController.SetAutoControl(0f, 0f);
            currentT = 1f;
            currentState = DriveState.Following;
            return;
        }

        if (isReversing)
        {
            reverseTimer += Time.deltaTime;
            Vector3 reversePoint = splinePos + (currentSpline.GetPoint(Mathf.Max(0f, currentT - 0.02f)) - splinePos).normalized * 1f;
            Vector3 localReverse = transform.InverseTransformPoint(reversePoint);
            float revSteering = Mathf.Clamp(localReverse.x * 1.5f, -1f, 1f);
            carController.SetAutoControl(-0.3f, revSteering);

            if (reverseTimer >= 1f + reverseCount * 0.3f)
            {
                reverseCount++;
                isReversing = false;
                reverseTimer = 0f;
                avoidCooldown = 1.5f;
                startupDelay = 0.8f;
                carController.SetAutoControl(0f, 0f);
                currentT = Mathf.Max(0, currentT - 0.03f);
                if (reverseCount >= 3)
                {
                    reverseCount = 0;
                    currentT = 1f;
                }
                RerouteToDestination();
                currentState = DriveState.Following;
            }
            return;
        }
        if (obstacleDetected) { isReversing = true; reverseTimer = 0f; return; }

        isReversing = false;
        reverseTimer = 0f;
        avoidCooldown = 1f;
        currentState = DriveState.Following;
    }

    void HandleStoppingState()
    {
        if (currentIntersectionState == IntersectionState.GreenLight || currentIntersectionState == IntersectionState.Uncontrolled)
        {
            hasStopTarget = false;
            stuckTimer = 0f; stuckCheckTimer = 0f; lastPosition = transform.position; startupDelay = 2f;
            carController.SetAutoControl(0f, 0f);
            carController.SetAutoBrake(0f);
            currentState = DriveState.Following;
            return;
        }

        if (!hasStopTarget)
        {
            carController.SetAutoControl(0f, 0f);
            return;
        }

        float distToStop = Vector3.Distance(transform.position, stopTargetPosition);
        float speed = Mathf.Abs(carController.GetSpeed());

        float brakingDecel = distToStop > 0.01f ? (speed * speed) / (2f * distToStop) : brakeMaxDecel;
        brakingDecel = Mathf.Clamp(brakingDecel, 0.5f, brakeMaxDecel);

        if (distToStop < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            carController.SetAutoBrake(brakeMaxDecel);
            AppendThought("Hard brake! Dist=" + distToStop.ToString("F2") + "m Speed=" + speed.ToString("F1") + "m/s");
            TriggerTORIfNeeded(distToStop, speed);
            return;
        }

        Vector3 diffToStop = stopTargetPosition - transform.position;
        Vector3 dirToStop = CarControlUtility.SafeNormalize(diffToStop, transform.forward);
        Vector3 localDir = transform.InverseTransformDirection(dirToStop);
        float steering = Mathf.Clamp(localDir.x * 2f, -1f, 1f);

        carController.SetAutoControl(0f, steering);
        carController.SetAutoBrake(brakingDecel);
    }

    void HandleWaitingState() => carController.SetAutoControl(0f, 0f);

    void HandleRemoteControlledState()
    {
        if (WorldModel.Instance != null)
        {
            laneSearchTimer += Time.deltaTime;
            if (laneSearchTimer > 0.2f)
            {
                currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
                laneSearchTimer = 0f;
            }
        }
        if (currentDestinationNodeId >= 0 && WorldModel.Instance != null)
        {
            currentIntersectionState = WorldModel.Instance.GetIntersectionState(currentDestinationNodeId);
        }
    }

    void FollowPath()
    {
        if (currentSpline == null || currentSpline.TotalLength < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            return;
        }

        float actualSpeed = Mathf.Abs(carController.GetSpeed());
        if (actualSpeed > 0.1f && currentSpline.TotalLength > 0)
        {
            currentT += (actualSpeed * Time.deltaTime) / currentSpline.TotalLength;
            currentT = Mathf.Clamp01(currentT);
        }

        if (WorldModel.Instance != null)
        {
            laneSearchTimer += Time.deltaTime;
            if (laneSearchTimer > 0.2f)
            {
                currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
                laneSearchTimer = 0f;
            }
        }

        Vector3 posOnSpline = currentSpline.GetPoint(currentT);

        float activeLookAhead = lookAheadT;
        if (dynamicLookAhead && currentSpline.TotalLength > 0.1f)
        {
            float speedFraction = Mathf.Clamp01(actualSpeed / targetSpeed);
            float activeLookDist = Mathf.Lerp(lookAheadMin, lookAheadMax, speedFraction);
            activeLookAhead = activeLookDist / currentSpline.TotalLength;
        }
        float targetLookAheadT = Mathf.Min(currentT + activeLookAhead, 1f);

        Vector3 lateralTarget = posOnSpline;
        Vector3 lookAheadPos;

        if (WorldModel.Instance != null && currentLaneId >= 0 && WorldModel.Instance.GlobalLanes.TryGetValue(currentLaneId, out Lane lane))
        {
            float bestLaneT = 0f;
            float minDistSqr = float.MaxValue;
            int searchSteps = 20;
            for (int i = 0; i <= searchSteps; i++)
            {
                float t = i / (float)searchSteps;
                Vector3 pt = lane.CenterSpline.GetPoint(t);
                float sqrD = (pt.x - posOnSpline.x) * (pt.x - posOnSpline.x) + (pt.z - posOnSpline.z) * (pt.z - posOnSpline.z);
                if (sqrD < minDistSqr) { minDistSqr = sqrD; bestLaneT = t; }
            }

            Vector3 lanePoint = lane.CenterSpline.GetPoint(bestLaneT);
            lateralTarget.x = lanePoint.x;
            lateralTarget.z = lanePoint.z;

            float laneLookT = Mathf.Clamp01(bestLaneT + (activeLookAhead * 2f));
            lookAheadPos = lane.CenterSpline.GetPoint(laneLookT);

            currentT = bestLaneT;
        }
        else
        {
            float nextT = (currentT < 0.999f) ? Mathf.Min(currentT + 0.001f, 1f) : Mathf.Max(currentT - 0.001f, 0f);
            Vector3 tangentRaw = currentSpline.GetPoint(nextT) - posOnSpline;
            Vector3 tangent = CarControlUtility.SafeNormalize(tangentRaw, transform.forward);
            Vector3 rightVector = Vector3.Cross(Vector3.up, tangent).normalized;
            float offset = isYielding ? yieldRightOffset : rightLaneOffset;
            lateralTarget = posOnSpline + rightVector * offset;
            lookAheadPos = currentSpline.GetPoint(targetLookAheadT) + rightVector * offset;
        }

        float cte = CalculateCTE(posOnSpline);
        float roadWidth = GetRoadWidth();
        float maxAllowedDeviation = (roadWidth / 2f) - (vehicleWidth / 2f) - cteSafetyMargin;
        maxAllowedDeviation = Mathf.Max(maxAllowedDeviation, 0.5f);

        float steering;
        float braking = 0f;
        bool cteActive = false;

        if (Mathf.Abs(cte) > maxAllowedDeviation)
        {
            steering = -Mathf.Sign(cte) * 1f;
            braking = 0.8f;
            cteActive = true;
        }
        else
        {
            lookAheadPos.y = transform.position.y;
            Vector3 localTarget = transform.InverseTransformPoint(lookAheadPos);
            float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
            steering = Mathf.Clamp(angle / 45f, -1f, 1f);
        }

        float speedFactor = 1f;
        if (!cteActive && Mathf.Abs(steering) > 0.55f && Mathf.Abs(actualSpeed) > 10f) speedFactor = 0.5f;

        if (currentIntersectionState == IntersectionState.RedLight) speedFactor = 0f;
        if (isYielding) speedFactor = 0f;

        float throttle = CarControlUtility.SafeDivide(targetSpeed * speedFactor, carController.maxSpeed);
        carController.SetAutoControl(throttle, steering);
        carController.SetAutoBrake(cteActive ? brakeMaxDecel * braking : 0f);
    }

    float CalculateCTE(Vector3 splinePoint)
    {
        Vector3 toCar = transform.position - splinePoint;
        toCar.y = 0;

        float nextT = Mathf.Clamp01(currentT + 0.001f);
        float prevT = Mathf.Clamp01(currentT - 0.001f);
        Vector3 tangent = (currentSpline.GetPoint(nextT) - currentSpline.GetPoint(prevT)).normalized;
        if (tangent.magnitude < 0.001f) tangent = transform.forward;

        Vector3 leftNormal = Vector3.Cross(Vector3.up, tangent).normalized;

        float cteSigned = Vector3.Dot(toCar, leftNormal);
        return cteSigned;
    }

    float GetRoadWidth()
    {
        ProceduralRoadBuilder rb = FindObjectOfType<ProceduralRoadBuilder>();
        return rb != null ? rb.roadWidth : 6f;
    }
}