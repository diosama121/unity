using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public enum CameraMode { Follow, TopDown, Free }
    public CameraMode currentMode = CameraMode.Follow;

    [Header("跟车设置")]
    public Transform target;
    public Vector3 followOffset = new Vector3(0, 5, -10);
    public float followSmooth = 5f;

    [Header("俯视设置")]
    public float topDownHeight = 40f;
    public float topDownSmooth = 5f;

    [Header("自由视角设置")]
    public float freeMoveSpeed = 20f;
    public float freeRotateSpeed = 3f;

    private float freeYaw = 0f;
    private float freePitch = 30f;

    void Start()
    {
        
    }

    void Update()
    { // 每帧检查，直到找到目标
    if (target == null)
    {
        var car = FindObjectOfType<SimpleCarController>();
        if (car != null) target = car.transform;
        return;
    }

    if (Input.GetKeyDown(KeyCode.C))
        SwitchMode();

    if (currentMode == CameraMode.Free)
        HandleFreeCamera();
    }

    void LateUpdate()
    {
        if (target == null) return;

        switch (currentMode)
        {
            case CameraMode.Follow:
                UpdateFollowCamera();
                break;
            case CameraMode.TopDown:
                UpdateTopDownCamera();
                break;
        }
    }

    void SwitchMode()
    {
        currentMode = (CameraMode)(((int)currentMode + 1) % 3);
        Debug.Log($"[Camera] 切换到: {currentMode}");

        if (currentMode == CameraMode.Free)
        {
            freeYaw = transform.eulerAngles.y;
            freePitch = transform.eulerAngles.x;
        }
    }

    void UpdateFollowCamera()
    {
        Vector3 targetPos = target.position + target.TransformDirection(followOffset);
        transform.position = Vector3.Lerp(transform.position, targetPos, followSmooth * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }

    void UpdateTopDownCamera()
    {
        Vector3 targetPos = new Vector3(target.position.x, topDownHeight, target.position.z);
        transform.position = Vector3.Lerp(transform.position, targetPos, topDownSmooth * Time.deltaTime);
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(90f, 0f, 0f), topDownSmooth * Time.deltaTime);
    }

    void HandleFreeCamera()
    {
        // 右键按住旋转
        if (Input.GetMouseButton(1))
        {
            freeYaw += Input.GetAxis("Mouse X") * freeRotateSpeed;
            freePitch -= Input.GetAxis("Mouse Y") * freeRotateSpeed;
            freePitch = Mathf.Clamp(freePitch, -80f, 80f);
            transform.rotation = Quaternion.Euler(freePitch, freeYaw, 0f);
        }

        // WASD移动
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float y = Input.GetKey(KeyCode.E) ? 1f : Input.GetKey(KeyCode.Q) ? -1f : 0f;

        Vector3 move = transform.right * h + transform.forward * v + Vector3.up * y;
        transform.position += move * freeMoveSpeed * Time.deltaTime;
    }
}