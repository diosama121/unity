using System.Collections.Generic;
using UnityEngine;

public static class TrajectoryBuilder
{
    public static CatmullRomSpline BuildSafeSpline(List<int> pathNodeIds)
    {
        List<Vector3> safePoints = new List<Vector3>();
        
        for (int i = 0; i < pathNodeIds.Count; i++)
        {
            Vector3 pos = WorldModel.Instance.GetNode(pathNodeIds[i]).WorldPos;
            safePoints.Add(new Vector3(pos.x, 0, pos.z));
            
            if (IsCriticalCorner(i, pathNodeIds))
            {
                InsertCornerAssistPoints(safePoints, i);
            }
        }
        
        return new CatmullRomSpline(safePoints, useCentripetal: true);
    }

    private static bool IsCriticalCorner(int index, List<int> nodeIds)
    {
        if (index < 1 || index >= nodeIds.Count - 1) return false;
        
        Vector3 prevDir = (WorldModel.Instance.GetNode(nodeIds[index-1]).WorldPos - 
                          WorldModel.Instance.GetNode(nodeIds[index]).WorldPos).normalized;
        Vector3 nextDir = (WorldModel.Instance.GetNode(nodeIds[index+1]).WorldPos - 
                          WorldModel.Instance.GetNode(nodeIds[index]).WorldPos).normalized;
        
        float angle = Vector3.Angle(prevDir, nextDir);
        return angle > 60f;
    }

    private static void InsertCornerAssistPoints(List<Vector3> points, int cornerIndex)
    {
        if (cornerIndex <= 0 || cornerIndex >= points.Count - 1) return;
        
        Vector3 corner = points[cornerIndex];
        Vector3 prevDir = (points[cornerIndex-1] - corner).normalized;
        Vector3 nextDir = (points[cornerIndex+1] - corner).normalized;
        
        points.Insert(cornerIndex, corner + prevDir * 2f);
        points.Insert(cornerIndex + 2, corner + nextDir * 2f);
    }
}