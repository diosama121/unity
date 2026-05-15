using UnityEngine;
using System.Collections.Generic;

public static class TopologyUtility
{
    public struct GraphNode
    {
        public int Id;
        public Vector3 Position;
        public List<int> Neighbors;

        public GraphNode(int id, Vector3 position)
        {
            Id = id;
            Position = position;
            Neighbors = new List<int>();
        }
    }

    public struct GraphEdge
    {
        public int NodeA;
        public int NodeB;
        public float Weight;

        public GraphEdge(int a, int b)
        {
            NodeA = Mathf.Min(a, b);
            NodeB = Mathf.Max(a, b);
            Weight = 1f;
        }

        public GraphEdge(int a, int b, float weight)
        {
            NodeA = Mathf.Min(a, b);
            NodeB = Mathf.Max(a, b);
            Weight = weight;
        }
    }

    public static List<int> FindShortestPath(List<GraphNode> nodes, int startId, int targetId)
    {
        if (startId == targetId)
            return new List<int> { startId };

        Dictionary<int, float> gCost = new Dictionary<int, float>();
        Dictionary<int, float> hCost = new Dictionary<int, float>();
        Dictionary<int, int> parent = new Dictionary<int, int>();
        HashSet<int> closedSet = new HashSet<int>();
        List<int> openSet = new List<int> { startId };

        foreach (var node in nodes)
        {
            gCost[node.Id] = float.MaxValue;
            hCost[node.Id] = Vector3.Distance(node.Position, nodes.Find(n => n.Id == targetId).Position);
            parent[node.Id] = -1;
        }

        gCost[startId] = 0;

        while (openSet.Count > 0)
        {
            int currentId = -1;
            float lowestFCost = float.MaxValue;

            foreach (int id in openSet)
            {
                float fCost = gCost[id] + hCost[id];
                if (fCost < lowestFCost)
                {
                    lowestFCost = fCost;
                    currentId = id;
                }
            }

            if (currentId == targetId)
                break;

            openSet.Remove(currentId);
            closedSet.Add(currentId);

            GraphNode currentNode = nodes.Find(n => n.Id == currentId);
            foreach (int neighborId in currentNode.Neighbors)
            {
                if (closedSet.Contains(neighborId))
                    continue;

                float tentativeGCost = gCost[currentId] + Vector3.Distance(currentNode.Position, nodes.Find(n => n.Id == neighborId).Position);

                if (tentativeGCost < gCost[neighborId])
                {
                    gCost[neighborId] = tentativeGCost;
                    parent[neighborId] = currentId;

                    if (!openSet.Contains(neighborId))
                        openSet.Add(neighborId);
                }
            }
        }

        if (parent[targetId] == -1)
            return null;

        List<int> path = new List<int>();
        int pathId = targetId;

        while (pathId != -1)
        {
            path.Add(pathId);
            pathId = parent[pathId];
        }

        path.Reverse();
        return path;
    }

    public static List<(int, int)> DetectEdgeIntersections(List<GraphNode> nodes, List<(int, int)> edges)
    {
        List<(int, int)> intersections = new List<(int, int)>();

        for (int i = 0; i < edges.Count; i++)
        {
            for (int j = i + 1; j < edges.Count; j++)
            {
                var edge1 = edges[i];
                var edge2 = edges[j];

                if (edge1.Item1 == edge2.Item1 || edge1.Item1 == edge2.Item2 ||
                    edge1.Item2 == edge2.Item1 || edge1.Item2 == edge2.Item2)
                    continue;

                Vector3 p1 = nodes.Find(n => n.Id == edge1.Item1).Position;
                Vector3 p2 = nodes.Find(n => n.Id == edge1.Item2).Position;
                Vector3 p3 = nodes.Find(n => n.Id == edge2.Item1).Position;
                Vector3 p4 = nodes.Find(n => n.Id == edge2.Item2).Position;

                if (GeometryUtility.LineSegmentsIntersect(p1, p2, p3, p4))
                {
                    intersections.Add((edge1.Item1, edge2.Item1));
                }
            }
        }

        return intersections;
    }

    public static List<GraphNode> MergeNearbyNodes(List<GraphNode> nodes, float minDistance)
    {
        List<GraphNode> result = new List<GraphNode>(nodes);
        bool merged = true;

        while (merged)
        {
            merged = false;

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    float dist = Vector3.Distance(result[i].Position, result[j].Position);

                    if (dist < minDistance)
                    {
                        GraphNode mergedNode = new GraphNode(result[i].Id, (result[i].Position + result[j].Position) * 0.5f);

                        foreach (int neighbor in result[i].Neighbors)
                        {
                            if (!mergedNode.Neighbors.Contains(neighbor) && neighbor != result[j].Id)
                                mergedNode.Neighbors.Add(neighbor);
                        }

                        foreach (int neighbor in result[j].Neighbors)
                        {
                            if (!mergedNode.Neighbors.Contains(neighbor) && neighbor != result[i].Id)
                                mergedNode.Neighbors.Add(neighbor);
                        }

                        foreach (int neighborId in mergedNode.Neighbors)
                        {
                            GraphNode neighbor = result.Find(n => n.Id == neighborId);
                            if (neighbor.Id == neighborId)
                            {
                                neighbor.Neighbors.Remove(result[i].Id);
                                neighbor.Neighbors.Remove(result[j].Id);
                                if (!neighbor.Neighbors.Contains(mergedNode.Id))
                                    neighbor.Neighbors.Add(mergedNode.Id);
                            }
                        }

                        result.RemoveAt(j);
                        result[i] = mergedNode;
                        merged = true;
                        break;
                    }
                }

                if (merged)
                    break;
            }
        }

        return result;
    }

    public static List<(int, int)> RemoveShortEdges(List<GraphNode> nodes, List<(int, int)> edges, float minLength)
    {
        List<(int, int)> result = new List<(int, int)>();

        foreach (var edge in edges)
        {
            Vector3 p1 = nodes.Find(n => n.Id == edge.Item1).Position;
            Vector3 p2 = nodes.Find(n => n.Id == edge.Item2).Position;
            float length = Vector3.Distance(p1, p2);

            if (length >= minLength)
                result.Add(edge);
        }

        return result;
    }

    public static void AddEdge(List<GraphNode> nodes, int a, int b)
    {
        if (a == b) return;

        GraphNode nodeA = nodes.Find(n => n.Id == a);
        GraphNode nodeB = nodes.Find(n => n.Id == b);

        if (nodeA.Neighbors.Contains(b)) return;
        if (nodeB.Neighbors.Contains(a)) return;

        nodeA.Neighbors.Add(b);
        nodeB.Neighbors.Add(a);
    }

    public static void RemoveEdge(List<GraphNode> nodes, int a, int b)
    {
        GraphNode nodeA = nodes.Find(n => n.Id == a);
        GraphNode nodeB = nodes.Find(n => n.Id == b);

        nodeA.Neighbors.Remove(b);
        nodeB.Neighbors.Remove(a);
    }

    public static int CountConnectedComponents(List<GraphNode> nodes)
    {
        HashSet<int> visited = new HashSet<int>();
        int components = 0;

        foreach (var node in nodes)
        {
            if (!visited.Contains(node.Id))
            {
                components++;
                Traverse(node.Id, nodes, visited);
            }
        }

        return components;
    }

    private static void Traverse(int startId, List<GraphNode> nodes, HashSet<int> visited)
    {
        Stack<int> stack = new Stack<int>();
        stack.Push(startId);

        while (stack.Count > 0)
        {
            int currentId = stack.Pop();

            if (visited.Contains(currentId))
                continue;

            visited.Add(currentId);

            GraphNode node = nodes.Find(n => n.Id == currentId);
            foreach (int neighborId in node.Neighbors)
            {
                if (!visited.Contains(neighborId))
                    stack.Push(neighborId);
            }
        }
    }

    public static bool IsNodeConnected(List<GraphNode> nodes, int nodeId, int targetId)
    {
        HashSet<int> visited = new HashSet<int>();
        Stack<int> stack = new Stack<int>();
        stack.Push(nodeId);

        while (stack.Count > 0)
        {
            int currentId = stack.Pop();

            if (currentId == targetId)
                return true;

            if (visited.Contains(currentId))
                continue;

            visited.Add(currentId);

            GraphNode node = nodes.Find(n => n.Id == currentId);
            foreach (int neighborId in node.Neighbors)
            {
                if (!visited.Contains(neighborId))
                    stack.Push(neighborId);
            }
        }

        return false;
    }

    public static List<GraphNode> GenerateGridGraph(int width, int height, float cellSize, float randomOffset, int seed)
    {
        System.Random rng = new System.Random(seed);
        List<GraphNode> nodes = new List<GraphNode>();

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float offsetX = (x == 0 || x == width - 1) ? 0 : (float)(rng.NextDouble() * 2 - 1) * randomOffset;
                float offsetZ = (z == 0 || z == height - 1) ? 0 : (float)(rng.NextDouble() * 2 - 1) * randomOffset;

                Vector3 pos = new Vector3(x * cellSize + offsetX, 0, z * cellSize + offsetZ);
                nodes.Add(new GraphNode(z * width + x, pos));
            }
        }

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int currentId = z * width + x;

                if (x < width - 1)
                {
                    int rightId = z * width + (x + 1);
                    AddEdge(nodes, currentId, rightId);
                }

                if (z < height - 1)
                {
                    int topId = (z + 1) * width + x;
                    AddEdge(nodes, currentId, topId);
                }
            }
        }

        return nodes;
    }

    public static void ShuffleEdges(List<GraphNode> nodes, float removeRate, int seed)
    {
        System.Random rng = new System.Random(seed);
        List<(int, int)> edgesToRemove = new List<(int, int)>();

        for (int i = 0; i < nodes.Count; i++)
        {
            foreach (int neighborId in nodes[i].Neighbors)
            {
                if (i < neighborId && rng.NextDouble() < removeRate)
                {
                    edgesToRemove.Add((i, neighborId));
                }
            }
        }

        foreach (var edge in edgesToRemove)
        {
            RemoveEdge(nodes, edge.Item1, edge.Item2);
        }
    }

    public static int FindNearestNode(List<GraphNode> nodes, Vector3 position)
    {
        if (nodes == null || nodes.Count == 0)
            return -1;

        int nearestId = nodes[0].Id;
        float minDist = float.MaxValue;

        foreach (var node in nodes)
        {
            float dist = Vector3.Distance(node.Position, position);
            if (dist < minDist)
            {
                minDist = dist;
                nearestId = node.Id;
            }
        }

        return nearestId;
    }

    public static List<int> GetNodesWithinRadius(List<GraphNode> nodes, Vector3 center, float radius)
    {
        List<int> result = new List<int>();

        foreach (var node in nodes)
        {
            if (Vector3.Distance(node.Position, center) <= radius)
                result.Add(node.Id);
        }

        return result;
    }

    public static GraphNodeType ClassifyNodeType(int neighborCount)
    {
        return neighborCount switch
        {
            0 => GraphNodeType.Isolated,
            1 => GraphNodeType.Endpoint,
            2 => GraphNodeType.Straight,
            3 => GraphNodeType.Merge,
            _ => GraphNodeType.Intersection
        };
    }
}

public enum GraphNodeType
{
    Isolated,
    Endpoint,
    Straight,
    Merge,
    Intersection
}