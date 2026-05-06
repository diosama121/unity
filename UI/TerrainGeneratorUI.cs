using UnityEditor;
using UnityEngine;

public class WorldGenUI : MonoBehaviour
{
    [Header("窗口设置")]
    private Rect _windowRect = new Rect(Screen.width - 220, 10, 210, 420); // 稍微加高以容纳更多控件
    private const int WindowId = 8888;

    // 系统引用
    private WorldModel _worldModel;
    private ProceduralRoadBuilder _roadBuilder;

    // --- 绑定变量 (镜像 ProceduralRoadBuilder) ---
    private float _roadWidth;
    private float _meshResolution;
    private float _roadHeightOffset;
    private float _tangentLength;
    private float _uvScale;
    private bool _useCountrysideUniformMaterials;
    private bool _generateCity;
    private float _buildingHeight;
    private float _sidewalkWidth;
    private bool _showSplineGizmos;

    // 滚动条
    private Vector2 _scrollPosition;

    private void Start()
    {
        _worldModel = GetComponent<WorldModel>();
        _roadBuilder = FindObjectOfType<ProceduralRoadBuilder>();
        
        // 初始化时读取一次当前值作为默认值
        if (_roadBuilder != null)
        {
            SyncValuesFromBuilder();
        }
    }

    private void OnGUI()
    {
        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindowContent, "🛠️ a5 观测控制台");
        // 窗口锁边逻辑
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
    }

    private void DrawWindowContent(int windowId)
    {
        GUILayout.Space(5);
        
        // 开始滚动视图，防止参数太多显示不下
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, false);

        // 1. 核心几何参数
        GUILayout.Label("【几何核心】", EditorStyles.boldLabel);
        DrawFloatSlider("道路宽度", ref _roadWidth, 2f, 15f);
        DrawFloatSlider("网格精度", ref _meshResolution, 0.5f, 5f);
        DrawFloatSlider("离地高度", ref _roadHeightOffset, 0f, 1f);
        DrawFloatSlider("切线长度", ref _tangentLength, 0f, 1f);
        GUILayout.Space(8);

        // 2. 外观参数
        GUILayout.Label("【外观 UV】", EditorStyles.boldLabel);
        DrawFloatSlider("UV 缩放", ref _uvScale, 0.01f, 0.5f);
        GUILayout.Space(8);

        // 3. 模式开关
        GUILayout.Label("【生成模式】", EditorStyles.boldLabel);
        _useCountrysideUniformMaterials = GUILayout.Toggle(_useCountrysideUniformMaterials, " 乡村统一材质");
        _generateCity = GUILayout.Toggle(_generateCity, " 生成城镇建筑");
        
        // 如果开启城镇，显示城镇参数
        if (_generateCity)
        {
            EditorGUI.indentLevel++; // 模拟缩进，运行时用空格代替
            GUILayout.Space(2);
            DrawFloatSlider("  建筑高度", ref _buildingHeight, 5f, 50f);
            DrawFloatSlider("  人行道宽", ref _sidewalkWidth, 0.5f, 5f);
            EditorGUI.indentLevel--;
        }
        GUILayout.Space(8);

        // 4. 调试
        GUILayout.Label("【调试】", EditorStyles.boldLabel);
        _showSplineGizmos = GUILayout.Toggle(_showSplineGizmos, " 显示样条 Gizmos");
        GUILayout.Space(12);

        // 5. 创世按钮
        if (GUILayout.Button("🚀 执行创世", GUILayout.Height(35)))
        {
            if (_roadBuilder == null)
            {
                Debug.LogError("[a5] 未找到 a1 (ProceduralRoadBuilder)，拒绝点火！");
            }
            else
            {
                InjectParametersToBuilder();
                _worldModel.TriggerWorldGeneration();
            }
        }

        GUILayout.EndScrollView();
        GUI.DragWindow(); // 允许拖拽窗口标题栏
    }

    // 辅助函数：绘制带标签的滑块
    private void DrawFloatSlider(string label, ref float value, float min, float max)
    {
        GUILayout.Label($"{label}: {value:F2}");
        value = GUILayout.HorizontalSlider(value, min, max);
    }

    /// <summary>
    /// 【数据注入】将 UI 数据推入 a1 的内存
    /// </summary>
    private void InjectParametersToBuilder()
    {
        if (_roadBuilder == null) return;

        // 严格按照字段名赋值，零物理接触
        _roadBuilder.roadWidth = _roadWidth;
        _roadBuilder.meshResolution = _meshResolution;
        _roadBuilder.roadHeightOffset = _roadHeightOffset;
        _roadBuilder.tangentLength = _tangentLength;
        _roadBuilder.uvScale = _uvScale;
        _roadBuilder.useCountrysideUniformMaterials = _useCountrysideUniformMaterials;
        _roadBuilder.generateCity = _generateCity;
        _roadBuilder.buildingHeight = _buildingHeight;
        _roadBuilder.sidewalkWidth = _sidewalkWidth;
        _roadBuilder.showSplineGizmos = _showSplineGizmos;

        Debug.Log("[a5] ✅ 语义参数已同步至 a1 视觉层。");
    }

    /// <summary>
    /// 启动时从 a1 读取当前值
    /// </summary>
    private void SyncValuesFromBuilder()
    {
        _roadWidth = _roadBuilder.roadWidth;
        _meshResolution = _roadBuilder.meshResolution;
        _roadHeightOffset = _roadBuilder.roadHeightOffset;
        _tangentLength = _roadBuilder.tangentLength;
        _uvScale = _roadBuilder.uvScale;
        _useCountrysideUniformMaterials = _roadBuilder.useCountrysideUniformMaterials;
        _generateCity = _roadBuilder.generateCity;
        _buildingHeight = _roadBuilder.buildingHeight;
        _sidewalkWidth = _roadBuilder.sidewalkWidth;
        _showSplineGizmos = _roadBuilder.showSplineGizmos;
    }
}