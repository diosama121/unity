using UnityEngine;

/// <summary>
/// 虚拟行人（横穿马路测试用例）
/// </summary>
public class VirtualPedestrian : MonoBehaviour
{
    public float walkSpeed = 1.5f; // 正常人步行速度
    public float crossDistance = 8f; // 横穿距离
    
    private Vector3 startPos;
    private float currentOffset = 0f;
    private int walkDirection = 1;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        // 沿道路的横向（X轴方向）移动
        currentOffset += walkSpeed * walkDirection * Time.deltaTime;

        if (Mathf.Abs(currentOffset) > crossDistance / 2f)
        {
            walkDirection *= -1; // 走到马路对面后回头
        }

        transform.position = startPos + transform.right * currentOffset;
    }
}