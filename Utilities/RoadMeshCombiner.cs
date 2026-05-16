using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道路网格静态合批工具类 (V4.1 终极版)
/// 负责接管所有散碎路面，按材质分组聚合，突破 65535 顶点限制，并统一铺设物理碰撞体。
/// </summary>
public static class RoadMeshCombiner
{
    public static void CombineRoadMeshes(Transform rootTransform)
    {
        if (rootTransform == null)
        {
            Debug.LogError("[RoadMeshCombiner] 根节点不能为空！");
            return;
        }

        Dictionary<Material, List<CombineInstance>> materialToCombineInstances = new Dictionary<Material, List<CombineInstance>>();
        List<GameObject> originalObjects = new List<GameObject>();

        // 自动跳过未激活节点
        MeshRenderer[] meshRenderers = rootTransform.GetComponentsInChildren<MeshRenderer>(false);

        foreach (MeshRenderer renderer in meshRenderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Mesh sourceMesh = filter.sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            Transform transform = renderer.transform;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;

                // 【核心防御】：防止材质槽位数量大于实际子网格数量导致的越界崩溃！
                if (i >= sourceMesh.subMeshCount) break; 

                if (!materialToCombineInstances.ContainsKey(mat))
                {
                    materialToCombineInstances.Add(mat, new List<CombineInstance>());
                }

                CombineInstance ci = new CombineInstance
                {
                    mesh = sourceMesh,
                    subMeshIndex = i, 
                    transform = transform.localToWorldMatrix 
                };

                materialToCombineInstances[mat].Add(ci);
            }

            originalObjects.Add(renderer.gameObject);
        }

        if (materialToCombineInstances.Count == 0)
        {
            Debug.LogWarning("[RoadMeshCombiner] 未在根节点下找到任何有效的路面网格。");
            return;
        }

        // 开始合并并生成超级网格
        foreach (var kvp in materialToCombineInstances)
        {
            Material mat = kvp.Key;
            List<CombineInstance> instances = kvp.Value;

            GameObject combinedGO = new GameObject($"Combined_Road_{mat.name}");
            combinedGO.transform.SetParent(rootTransform.parent);
            combinedGO.transform.position = Vector3.zero;
            combinedGO.transform.rotation = Quaternion.identity;
            combinedGO.transform.localScale = Vector3.one;
            combinedGO.layer = rootTransform.gameObject.layer; // 继承图层

            MeshFilter combinedFilter = combinedGO.AddComponent<MeshFilter>();
            MeshRenderer combinedRenderer = combinedGO.AddComponent<MeshRenderer>();
            combinedRenderer.sharedMaterial = mat; 

            Mesh combinedMesh = new Mesh { name = $"CombinedMesh_{mat.name}" };
            // 【核心】：突破顶点数限制
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combinedMesh.CombineMeshes(instances.ToArray(), true, true);

            combinedFilter.sharedMesh = combinedMesh;

            // 【核心】：统一烘焙物理碰撞体，车辆从此绝不会掉入虚空
            MeshCollider collider = combinedGO.AddComponent<MeshCollider>();
            collider.sharedMesh = combinedMesh;
            collider.convex = false; 
        }

        // 清理战场：安全的销毁机制
        foreach (GameObject obj in originalObjects)
        {
            if (Application.isPlaying)
                GameObject.Destroy(obj);
            else
                GameObject.DestroyImmediate(obj); // 兼容编辑器模式下点击生成
        }

        Debug.Log($"[RoadMeshCombiner] ✅ 合批完成！散碎网格数量: {originalObjects.Count} -> 合并为超级网格: {materialToCombineInstances.Count} 个，碰撞体已铺设。");
    }
}