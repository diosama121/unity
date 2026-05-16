using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class KDTree
{
    private struct Node
    {
        public int RoadNodeId;
        public Vector3 Position;
        public int Left, Right; 
    }

    private readonly Node[] _nodes;

    public KDTree(IEnumerable<RoadNode> roadNodes)
    {
        var list = roadNodes.Select(r => new Node
        {
            RoadNodeId = r.Id,
            Position = r.WorldPos,
            Left = -1, Right = -1
        }).ToArray();

        _nodes = new Node[list.Length];
        int tail = 0;
        Build(list, 0, list.Length - 1, 0, ref tail);
    }

    public int QueryNearest(Vector3 target)
    {
        if (_nodes.Length == 0) return -1;
        int bestId = _nodes[0].RoadNodeId;
        float bestDist = float.MaxValue;
        Search(0, target, 0, ref bestId, ref bestDist);
        return bestId;
    }

    private void Build(Node[] src, int lo, int hi, int axis, ref int tail)
    {
        if (lo > hi) return;
        int mid = (lo + hi) / 2;
        PartialSort(src, lo, hi, mid, axis);

        int idx = tail++;
        _nodes[idx] = src[mid];

        int nextAxis = (axis + 1) % 3;
        int leftIdx = tail;
        Build(src, lo, mid - 1, nextAxis, ref tail);
        _nodes[idx].Left = (lo <= mid - 1) ? leftIdx : -1;

        int rightIdx = tail;
        Build(src, mid + 1, hi, nextAxis, ref tail);
        _nodes[idx].Right = (mid + 1 <= hi) ? rightIdx : -1;
    }

    private void Search(int idx, Vector3 target, int axis, ref int bestId, ref float bestDist)
    {
        if (idx == -1 || idx >= _nodes.Length) return;

        ref readonly Node n = ref _nodes[idx];
        float d = Vector3.SqrMagnitude(n.Position - target); 
        if (d < bestDist)
        {
            bestDist = d;
            bestId = n.RoadNodeId;
        }

        float split = GetAxis(n.Position, axis);
        float tdiff = GetAxis(target, axis) - split;

        int near = tdiff <= 0 ? n.Left : n.Right;
        int far = tdiff <= 0 ? n.Right : n.Left;
        int nextAxis = (axis + 1) % 3;

        Search(near, target, nextAxis, ref bestId, ref bestDist);
        if (tdiff * tdiff < bestDist)
            Search(far, target, nextAxis, ref bestId, ref bestDist);
    }

    private static float GetAxis(Vector3 v, int axis) => axis switch { 0 => v.x, 1 => v.y, _ => v.z };

    private static void PartialSort(Node[] arr, int lo, int hi, int k, int axis)
    {
        while (lo < hi)
        {
            int pivot = Partition(arr, lo, hi, axis);
            if (pivot == k) return;
            else if (pivot < k) lo = pivot + 1;
            else hi = pivot - 1;
        }
    }

    private static int Partition(Node[] arr, int lo, int hi, int axis)
    {
        float pivot = GetAxis(arr[hi].Position, axis);
        int store = lo;
        for (int i = lo; i < hi; i++)
        {
            if (GetAxis(arr[i].Position, axis) < pivot)
                (arr[i], arr[store++]) = (arr[store], arr[i]);
        }
        (arr[store], arr[hi]) = (arr[hi], arr[store]);
        return store;
    }
}