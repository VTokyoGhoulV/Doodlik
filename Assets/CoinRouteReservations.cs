using System.Collections.Generic;
using UnityEngine;

// Late hazard planning must not invalidate a coin's already published return
// path. Reservations outlive collection until the whole jump is below camera.
public static class CoinRouteReservations
{
    private sealed class Entry
    {
        public Camera Camera;
        public Vector2[] Path;
        public Vector2 HalfSize;
        public float Top;
    }
    private static readonly List<Entry> entries = new List<Entry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => entries.Clear();

    public static void Reserve(Camera camera, IList<Vector2> localPath, Vector2 origin, Vector2 bodyHalfSize)
    {
        if (camera == null || localPath == null || localPath.Count < 2) return;
        Entry entry = new Entry { Camera = camera, Path = new Vector2[localPath.Count],
            HalfSize = bodyHalfSize + Vector2.one * 0.12f, Top = float.NegativeInfinity };
        for (int i = 0; i < localPath.Count; i++)
        {
            entry.Path[i] = origin + localPath[i];
            entry.Top = Mathf.Max(entry.Top, entry.Path[i].y + entry.HalfSize.y);
        }
        entries.Add(entry);
    }

    public static bool Overlaps(Rect patrolBounds, float screenWidth)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            if (entry.Camera == null || entry.Camera.transform.position.y - entry.Camera.orthographicSize > entry.Top + 1f)
            { entries.RemoveAt(i); continue; }
            for (int p = 1; p < entry.Path.Length; p++)
            for (int copy = -1; copy <= 1; copy++)
            {
                Rect shifted = patrolBounds;
                shifted.x += screenWidth * copy;
                if (TrapGeometry.SweptBox(entry.Path[p - 1], entry.Path[p], entry.HalfSize, shifted)) return true;
            }
        }
        return false;
    }

    public static void ClearForCamera(Camera camera)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Camera == null || entries[i].Camera == camera) entries.RemoveAt(i);
    }
}
