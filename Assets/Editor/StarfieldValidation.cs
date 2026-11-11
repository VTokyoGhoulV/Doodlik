using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Tests the generated meshes and actual viewport transforms. Does not change
// scenes, world-zone state, preferences, the live camera, or the Unity RNG.
public static class StarfieldValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("Doodlik/Validate Starfield (Play Mode)")]
    public static void Validate()
    {
        Require(Application.isPlaying, "Start Play Mode before validating the atmosphere lifecycle.");
        Type type = typeof(ScrollingBackground).Assembly.GetType("ZoneAtmosphere", true);
        MethodInfo build = type.GetMethod("Build", Static);
        MethodInfo refresh = type.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public);
        var temporary = new List<Mesh>();
        GameObject sandbox = new GameObject("Starfield validation sandbox") { hideFlags = HideFlags.DontSave };
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        int starChecks = 0;
        try
        {
            GameObject cameraObject = new GameObject("Validation camera") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(sandbox.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            Component atmosphere = sandbox.AddComponent(type);
            Require((Material)type.GetField("material", Instance).GetValue(atmosphere) != null,
                "Atmosphere shader/material must exist for viewport validation.");

            foreach (float aspect in new[] { 9f / 16f, 16f / 9f, 20f / 9f })
            {
                camera.aspect = aspect;
                refresh.Invoke(atmosphere, new object[] { camera, WorldZones.Zone.Night });
                var strips = (Transform[])type.GetField("strips", Instance).GetValue(atmosphere);
                var meshes = (Mesh[])type.GetField("meshes", Instance).GetValue(atmosphere);
                for (int stripIndex = 2; stripIndex < 4; stripIndex++)
                {
                    Mesh mesh = meshes[stripIndex];
                    Require(strips[stripIndex].GetComponent<MeshFilter>().sharedMesh == mesh,
                        "Aspect rebuild must replace the rendered mesh, not only the cached one.");
                    starChecks += CheckStars(mesh, strips[stripIndex]);
                    CheckScattering(Centers(mesh));
                }
                Require(!EqualVertices(meshes[2], meshes[3]), "Adjacent repeated strips need different constellations.");

                foreach (int seed in new[] { 1, 731 + 2 * 997, 731 + 3 * 997, 2026, 91873 })
                {
                    Mesh first = Build(build, temporary, seed, aspect);
                    Mesh same = Build(build, temporary, seed, aspect);
                    Mesh different = Build(build, temporary, seed + 101, aspect);
                    Require(EqualVertices(first, same) && EqualIndices(first, same) && EqualColors(first, same),
                        "Same seed/aspect must reproduce stable geometry and opacity.");
                    Require(!EqualVertices(first, different), "Different seeds must change the starfield.");
                    starChecks += CheckStars(first, strips[2]);
                    CheckScattering(Centers(first));
                }
            }
            Require(UnityEngine.Random.state.Equals(randomState),
                "Atmosphere creation/refresh must not advance the platform generator's Unity RNG.");
            Debug.Log($"STARFIELD_PASS: {starChecks} generated stars; concave center fans, area/winding, equal arms after 3 actual viewport transforms, stable/different seeds, irregular rows, isolated RNG.");
        }
        finally
        {
            foreach (Mesh mesh in temporary) if (mesh != null) Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(sandbox);
            UnityEngine.Random.state = randomState;
        }
    }

    private static Mesh Build(MethodInfo build, List<Mesh> temporary, int seed, float aspect)
    {
        Mesh result = (Mesh)build.Invoke(null, new object[] { true, seed, aspect });
        temporary.Add(result);
        return result;
    }

    private static Dictionary<int, List<int>> Fans(Mesh mesh)
    {
        var fans = new Dictionary<int, List<int>>();
        int[] triangles = mesh.triangles;
        Require(triangles.Length % 3 == 0, "Mesh triangle index count must be valid.");
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int center = triangles[i];
            if (!fans.TryGetValue(center, out List<int> edges)) fans.Add(center, edges = new List<int>());
            edges.Add(triangles[i + 1]);
            edges.Add(triangles[i + 2]);
        }
        return fans;
    }

    private static int CheckStars(Mesh mesh, Transform strip)
    {
        Vector3[] vertices = mesh.vertices;
        var fans = Fans(mesh);
        Require(fans.Count == 27, "The field should contain 27 individual stars, not joined ribbons.");
        foreach (var fan in fans)
        {
            Require(fan.Value.Count == 16, "A concave four-point star needs eight triangles around its center.");
            var outlineIndices = new HashSet<int>(fan.Value);
            Require(outlineIndices.Count == 8 && !outlineIndices.Contains(fan.Key),
                "Each star must have eight boundary vertices plus a separate center.");
            Vector3 center = vertices[fan.Key];
            Vector3 average = Vector3.zero;
            var outline = new List<Vector2>();
            foreach (int index in outlineIndices)
            {
                Require(index >= 0 && index < vertices.Length, "All boundary indices must refer to real vertices.");
                average += vertices[index];
                outline.Add(strip.TransformVector(vertices[index] - center));
            }
            Require((average / 8f - center).sqrMagnitude < 0.000000001f,
                "Fan origin must be the actual center, not a tip that fills across notches.");
            outline.Sort((a, b) => Mathf.Atan2(a.y, a.x).CompareTo(Mathf.Atan2(b.y, b.x)));
            float radius = 0f;
            foreach (Vector2 point in outline) radius = Mathf.Max(radius, point.magnitude);
            Require(radius > 0f, "Stars cannot collapse to zero-size geometry.");
            int outer = 0, inner = 0, concaveCorners = 0;
            float twicePolygonArea = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 point = outline[i];
                Vector2 next = outline[(i + 1) % 8];
                Vector2 opposite = outline[(i + 4) % 8];
                Require((point + opposite).magnitude < radius * 0.002f,
                    "Opposing star arms must stay symmetric after viewport scaling.");
                if (Mathf.Abs(point.magnitude / radius - 1f) < 0.003f) outer++;
                else if (Mathf.Abs(point.magnitude / radius - 0.28f) < 0.003f) inner++;
                else Require(false, "Nonuniform viewport scaling must not stretch one arm or notch.");
                if (Cross(point - outline[(i + 7) % 8], next - point) < 0f) concaveCorners++;
                twicePolygonArea += Cross(point, next);
            }
            Require(outer == 4 && inner == 4 && concaveCorners == 4,
                "Four equal outer arms and four recessed notches should remain recognizable.");
            float twiceTriangleArea = 0f;
            for (int i = 0; i < fan.Value.Count; i += 2)
            {
                Vector2 a = strip.TransformVector(vertices[fan.Value[i]] - center);
                Vector2 b = strip.TransformVector(vertices[fan.Value[i + 1]] - center);
                float area = Cross(a, b);
                Require(area > radius * radius * 0.001f, "Triangles must be nondegenerate and consistently wound.");
                Require(Mathf.Abs(Vector2.SignedAngle(a, b) - 45f) < 0.1f,
                    "Each fan triangle must join neighboring outline corners, not cross another arm.");
                twiceTriangleArea += area;
            }
            Require(Mathf.Abs(twiceTriangleArea - twicePolygonArea) < twicePolygonArea * 0.001f,
                "Triangle area must equal the concave outline, with no overlap across its notches.");
        }
        return fans.Count;
    }

    private static List<Vector2> Centers(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        var centers = new List<Vector2>();
        foreach (int index in Fans(mesh).Keys) centers.Add(vertices[index]);
        return centers;
    }

    private static void CheckScattering(List<Vector2> centers)
    {
        var yLevels = new HashSet<int>();
        var xLevels = new HashSet<int>();
        var sorted = new List<float>();
        foreach (Vector2 center in centers)
        {
            yLevels.Add(Mathf.RoundToInt(center.y * 10000f));
            xLevels.Add(Mathf.RoundToInt(center.x * 10000f));
            sorted.Add(center.y);
            Require(Mathf.Abs(center.x) <= 0.451f && Mathf.Abs(center.y) <= 0.451f,
                "Star centers must remain within the intended strip inset.");
        }
        Require(yLevels.Count >= 24 && xLevels.Count >= 24, "Stars must not share fixed horizontal or vertical rows.");
        sorted.Sort();
        float meanGap = (sorted[sorted.Count - 1] - sorted[0]) / (sorted.Count - 1);
        float variance = 0f;
        for (int i = 1; i < sorted.Count; i++)
        {
            float deviation = sorted[i] - sorted[i - 1] - meanGap;
            variance += deviation * deviation;
        }
        float relativeSpread = Mathf.Sqrt(variance / (sorted.Count - 1)) / meanGap;
        Require(relativeSpread > 0.35f, "Height spacing must be irregular rather than an evenly spaced diagonal sequence.");
    }

    private static bool EqualVertices(Mesh a, Mesh b)
    {
        Vector3[] x = a.vertices, y = b.vertices;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }
    private static bool EqualIndices(Mesh a, Mesh b)
    {
        int[] x = a.triangles, y = b.triangles;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }
    private static bool EqualColors(Mesh a, Mesh b)
    {
        Color[] x = a.colors, y = b.colors;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Starfield validation: " + message);
    }
}
