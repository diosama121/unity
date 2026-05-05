using System.Collections.Generic;
using UnityEngine;

namespace AutonomousSim.Navigation
{
  
    public class PathPlanner : MonoBehaviour
    {
        // A* 算法内部使用的临时节点结构
        private class PathNode
        {
            public int NodeId;
            public int ParentId;
            public float GCost; // 从起点到当前点的实际代价
            public float HCost; // 从当前点到终点的预估代价
            public float FCost => GCost + HCost;

            public PathNode(int id)
            {
                NodeId = id;
                ParentId = -1;
                GCost = float.MaxValue;
                HCost = 0;
            }
        }

        /// <summary>
        /// 全局路径规划接口 (V2.0 升级版)
        /// </summary>
        /// <param name="startPos">世界坐标起点</param>
        /// <param name="targetPos">世界坐标终点</param>
        /// <returns>平滑的 CatmullRomSpline 样条线对象</returns>
        public CatmullRomSpline PlanPath(Vector3 startPos, Vector3 targetPos)
        {
            // 1. 极速接入路网：调用 WorldModel 的 O(log N) 接口，告别暴力遍历
            int startNodeId = WorldModel.Instance.GetNearestNode(startPos).Id;
            int targetNodeId = WorldModel.Instance.GetNearestNode(targetPos).Id;

            if (startNodeId == -1 || targetNodeId == -1)
            {
                Debug.LogWarning("[PathPlanner] 无法在路网中找到有效的起点或终点！");
                return null;
            }

            // 2. 执行 A* 算法，获取路点 ID 序列
            List<int> pathIds = RunAStar(startNodeId, targetNodeId);
            if (pathIds == null || pathIds.Count == 0) return null;

            // 3. 将 ID 序列转换为世界坐标序列
            List<Vector3> pathPoints = new List<Vector3>();
            foreach (int id in pathIds)
            {
                // 向 WorldModel 请求节点的世界坐标
                Vector3 nodePos = WorldModel.Instance.GetNode(id).WorldPos; 
                pathPoints.Add(nodePos);
            }

            // 4. 生成并返回平滑的 Catmull-Rom 样条线
            return new CatmullRomSpline(pathPoints);
        }

        // 经典的 A* 启发式图搜索算法
        private List<int> RunAStar(int startId, int targetId)
        {
            List<PathNode> openSet = new List<PathNode>();
            HashSet<int> closedSet = new HashSet<int>();
            Dictionary<int, PathNode> allNodes = new Dictionary<int, PathNode>();

            PathNode startNode = new PathNode(startId);
            startNode.GCost = 0;
            startNode.HCost = GetDistance(startId, targetId);
            openSet.Add(startNode);
            allNodes[startId] = startNode;

            while (openSet.Count > 0)
            {
                // 找出 FCost 最小的节点
                PathNode currentNode = openSet[0];
                int currentIndex = 0;
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (openSet[i].FCost < currentNode.FCost)
                    {
                        currentNode = openSet[i];
                        currentIndex = i;
                    }
                }

                openSet.RemoveAt(currentIndex);
                closedSet.Add(currentNode.NodeId);

                // 到达终点，重构路径
                if (currentNode.NodeId == targetId)
                {
                    return ReconstructPath(currentNode, allNodes);
                }

                // 获取邻接节点 (向 WorldModel 请求拓扑结构)
                int[] neighbors = WorldModel.Instance.GetNode(currentNode.NodeId).NeighborIds.ToArray();
                
                foreach (int neighborId in neighbors)
                {
                    if (closedSet.Contains(neighborId)) continue;

                    float tentativeGCost = currentNode.GCost + GetDistance(currentNode.NodeId, neighborId);

                    PathNode neighborNode;
                    if (!allNodes.ContainsKey(neighborId))
                    {
                        neighborNode = new PathNode(neighborId);
                        allNodes[neighborId] = neighborNode;
                        openSet.Add(neighborNode);
                    }
                    else
                    {
                        neighborNode = allNodes[neighborId];
                    }

                    if (tentativeGCost < neighborNode.GCost)
                    {
                        neighborNode.ParentId = currentNode.NodeId;
                        neighborNode.GCost = tentativeGCost;
                        neighborNode.HCost = GetDistance(neighborId, targetId);
                    }
                }
            }

            return null; // 无路可走
        }

        // 路径重构
        private List<int> ReconstructPath(PathNode endNode, Dictionary<int, PathNode> allNodes)
        {
            List<int> path = new List<int>();
            PathNode current = endNode;
            while (current != null)
            {
                path.Add(current.NodeId);
                current = allNodes.ContainsKey(current.ParentId) ? allNodes[current.ParentId] : null;
            }
            path.Reverse();
            return path;
        }

        // 辅助函数：计算两点间的欧几里得距离 (作为 HCost 启发值和 GCost 增量)
        private float GetDistance(int idA, int idB)
        {
            Vector3 posA = WorldModel.Instance.GetNode(idA).WorldPos;
            Vector3 posB = WorldModel.Instance.GetNode(idB).WorldPos;
            return Vector3.Distance(posA, posB);
        }
    }
      public class CatmullRomSpline
    {
        public List<Vector3> ControlPoints { get; private set; }
        public float TotalLength { get; private set; }
        
        // 预烘焙的累积长度表，用于车辆根据行驶距离反推曲线上的位置
        private List<float> _cumulativeLengths;
        private const int SAMPLES_PER_SEGMENT = 10; // 每段控制点的采样密度

        public CatmullRomSpline(List<Vector3> controlPoints)
        {
            ControlPoints = controlPoints;
            BakeCurve();
        }

        // 预烘焙：计算曲线总长并建立“距离-t值”映射表
        private void BakeCurve()
        {
            if (ControlPoints == null || ControlPoints.Count < 2) return;

            _cumulativeLengths = new List<float> { 0f };
            TotalLength = 0f;
            Vector3 lastPoint = GetPoint(0f);
            
            // 遍历每一段进行高频采样，累加逼近真实弧长
            for (int i = 0; i < ControlPoints.Count - 1; i++)
            {
                for (int j = 1; j <= SAMPLES_PER_SEGMENT; j++)
                {
                    float tSegment = (float)j / SAMPLES_PER_SEGMENT;
                    // 将局部 t 映射到全局 t
                    float globalT = (i + tSegment) / (ControlPoints.Count - 1);
                    
                    Vector3 currentPoint = GetPoint(globalT);
                    float segmentLen = Vector3.Distance(lastPoint, currentPoint);
                    
                    TotalLength += segmentLen;
                    _cumulativeLengths.Add(TotalLength);
                    
                    lastPoint = currentPoint;
                }
            }
        }

        /// <summary>
        /// 获取样条线上 t (0~1) 处的世界坐标
        /// </summary>
        public Vector3 GetPoint(float t)
        {
            int numPoints = ControlPoints.Count;
            t = Mathf.Clamp01(t);
            
            // 映射 t 到具体的线段索引
            float scaledT = t * (numPoints - 1);
            int segmentIndex = Mathf.FloorToInt(scaledT);
            
            // 边界保护
            if (segmentIndex >= numPoints - 1) return ControlPoints[numPoints - 1];
            
            float localT = scaledT - segmentIndex;

            // 获取四个控制点 (P0, P1, P2, P3)，边界采用钳制策略
            int p0 = Mathf.Max(0, segmentIndex - 1);
            int p1 = segmentIndex;
            int p2 = segmentIndex + 1;
            int p3 = Mathf.Min(numPoints - 1, segmentIndex + 2);

            return CalculateCatmullRom(
                ControlPoints[p0], 
                ControlPoints[p1], 
                ControlPoints[p2], 
                ControlPoints[p3], 
                localT
            );
        }

        // 标准的 Catmull-Rom 插值数学公式
        private Vector3 CalculateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }
        
        /// <summary>
        /// 根据实际行驶距离 s 获取归一化 t 值 (用于车辆恒速控制)
        /// </summary>
        public float GetTFromLength(float length)
        {
            if (TotalLength <= 0) return 0;
            length = Mathf.Clamp(length, 0, TotalLength);
            
            // 在累积长度表中查找对应的位置
            for (int i = 0; i < _cumulativeLengths.Count - 1; i++)
            {
                if (_cumulativeLengths[i+1] >= length)
                {
                    float segmentLen = _cumulativeLengths[i+1] - _cumulativeLengths[i];
                    float localT = (length - _cumulativeLengths[i]) / segmentLen;
                    return (i + localT) / (_cumulativeLengths.Count - 1);
                }
            }
            return 1f;
        }
    }
}