using UnityEngine;

public partial class SimpleCarController : MonoBehaviour
{
    void HandlePlayerInput()
    {
        if (isNPC) return;
        if (ros2Controlled && autoMode) return;

        bool wasdActive = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        if (wasdActive)
        {
            if (!wasdOverride)
            {
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
            wasdOverride = false;
            autoMode = autoModeBeforeOverride;
        }

        if (Input.GetKeyDown(KeyCode.R) && !isNPC)
        {
            ResetPosition();
        }

        if (Input.GetKeyDown(KeyCode.N) && !isNPC)
        {
            wasdOverride = false;
            autoMode = true;
            SetAutoBrake(0f);
            SimpleAutoDrive autoDrive = GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = FindObjectOfType<SimpleAutoDrive>();
            if (autoDrive != null) autoDrive.ResetNavigation();
        }

        if (Input.GetMouseButtonDown(1) && !isNPC)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                RoadNode targetNode = WorldModel.Instance.GetNearestNode(hit.point);
                if (targetNode != null)
                {
                    SimpleAutoDrive playerAI = GetComponent<SimpleAutoDrive>();
                    if (playerAI == null) playerAI = FindObjectOfType<SimpleAutoDrive>();
                    if (playerAI != null)
                    {
                        playerAI.SetDestination(targetNode.WorldPos);
                        Debug.Log($"Navigation: heading to Node {targetNode.Id}");
                    }
                }
            }
        }
    }
}