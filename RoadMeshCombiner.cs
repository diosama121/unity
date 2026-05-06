using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道路网格静态合批工具类
/// 用于在程序化生成路网后，将散碎的路面 Mesh 按材质合并，以大幅降低 Draw Call。
/// </summary>
public static class RoadMeshCombiner
{
    /// <summary>
    /// 合并指定根节点下所有的路面网格
    /// </summary>
    /// <param name="rootTransform">路网生成的父节点</param>
   /* public static void CombineRoadMeshes(Transform rootTransform)
    {
        if (rootTransform == null)
        {
            Debug.LogError("[RoadMeshCombiner] 根节点不能为空！");
            return;
        }

        // 需求2：按材质分组的字典（支持一个物体多个材质的情况）
        Dictionary<Material, List<CombineInstance>> materialToCombineInstances = new Dictionary<Material, List<CombineInstance>>();
        // 记录参与合并的原始对象，用于后续清理
        List<GameObject> originalObjects = new List<GameObject>();

        // ==========================================
        // 需求1：收集网格 (自动跳过未激活节点)
        // ==========================================
        // 注意：这里的 false 参数确保了 GetComponentsInChildren 不会去获取未激活的节点
        MeshRenderer[] meshRenderers = rootTransform.GetComponentsInChildren<MeshRenderer>(false);

        foreach (MeshRenderer renderer in meshRenderers)
        {
            // 健壮性检查：获取对应的 MeshFilter
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                continue; // 跳过没有 MeshFilter 或 Mesh 为空的对象
            }

            Mesh sourceMesh = filter.sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            Transform transform = renderer.transform;

            // 遍历该 Renderer 上的所有材质（处理多材质/子网格情况）
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                
                // 健壮性检查：跳过缺失的材质槽位
                if (mat == null) continue;

                // 如果字典中还没有这个材质，初始化列表
                if (!materialToCombineInstances.ContainsKey(mat))
                {
                    materialToCombineInstances.Add(mat, new List<CombineInstance>());
                }

                // 构建 CombineInstance
                CombineInstance ci = new CombineInstance
                {
                    mesh = sourceMesh,
                    // 关键：指定子网格索引，确保多材质模型的顶点能正确按材质归类
                    subMeshIndex = i, 
                    // 关键：使用局部转世界矩阵。这样合并后的顶点直接处于世界空间，
                    // 我们不需要让新网格保持原来散碎对象的父子层级关系。
                    transform = transform.localToWorldMatrix 
                };

                materialToCombineInstances[mat].Add(ci);
            }

            originalObjects.Add(renderer.gameObject);
        }

        // 如果没有找到任何需要合并的网格，直接退出
        if (materialToCombineInstances.Count == 0)
        {
            Debug.LogWarning("[RoadMeshCombiner] 未在根节点下找到任何有效的路面网格。");
            return;
        }

        // ==========================================
        // 需求3、4、5：突破顶点限制、执行合并、重组对象树
        // ==========================================
        foreach (var kvp in materialToCombineInstances)
        {
            Material mat = kvp.Key;
            List<CombineInstance> instances = kvp.Value;

            // 创建新的合并容器对象
            GameObject combinedGO = new GameObject($"Combined_Road_{mat.name}");
            
            // 专家技巧：将合并后的对象放在根节点的同级（或场景根节点），
            // 因为我们使用了 localToWorldMatrix 烘焙顶点，所以新对象的 Transform 必须是零点且无旋转。
            // 这样能绝对避免父节点缩放/旋转导致的顶点偏移问题。
            combinedGO.transform.SetParent(rootTransform.parent);
            combinedGO.transform.position = Vector3.zero;
            combinedGO.transform.rotation = Quaternion.identity;
            combinedGO.transform.localScale = Vector3.one;

            // 添加 MeshFilter 和 MeshRenderer
            MeshFilter combinedFilter = combinedGO.AddComponent<MeshFilter>();
            MeshRenderer combinedRenderer = combinedGO.AddComponent<MeshRenderer>();
            combinedRenderer.sharedMaterial = mat; // 赋予对应的单一材质

            // 创建全新的合并网格
            Mesh combinedMesh = new Mesh
            {
                name = $"CombinedMesh_{mat.name}"
            };

            // 需求3：【核心】在合并前必须设置为 UInt32，否则合并时如果顶点超过 65535 会直接报错抛出异常
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // 需求4：执行合并
            // 参数 true 表示将所有子网格合并为一个单一的子网格（因为我们已经按材质在外部分组了）
            combinedMesh.CombineMeshes(instances.ToArray(), true, true);

            // 赋值给 MeshFilter
            combinedFilter.sharedMesh = combinedMesh;

            // 需求5：自动添加 MeshCollider
            // 注意：对于超大型合并网格，MeshCollider 在唤醒时会进行凸包分解或树状结构构建，可能会有瞬间的卡顿。
            // 如果路面只是给车辆行驶用，这是标准做法。
            MeshCollider collider = combinedGO.AddComponent<MeshCollider>();
            collider.sharedMesh = combinedMesh;
            
            // 由于道路一般不需要作为凸包碰撞体（且凸包会消耗极大内存和算力），确保它是非凸的
            collider.convex = false; 
        }

        // ==========================================
        // 需求6：清理战场
        // ==========================================
        foreach (GameObject obj in originalObjects)
        {
            // 使用 Destroy 而不是 DestroyImmediate，确保在编辑器或运行时都能安全执行，不阻塞当前帧
            GameObject.Destroy(obj);
        }

        Debug.Log($"[RoadMeshCombiner] 合批完成！原始网格数量: {originalObjects.Count}，合并后网格数量: {materialToCombineInstances.Count}");
    }*/
}
