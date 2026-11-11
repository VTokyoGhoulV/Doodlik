#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Real-physics witness checks plus the identical frame-sliced runtime planner.
public static class ConnectedHazardValidation
{
    public static void RunFinal()
    {
        Run();
        HazardValidation.DiagnoseSliced();
    }

    public static void Run()
    {
        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        try
        {
            for (int format = 0; format < (GameplayPreferences.IsDesktopBuild ? 1 : 2); format++)
            for (int control = 0; control < 2; control++)
            {
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
                Spawner spawner = UnityEngine.Object.FindAnyObjectByType<Spawner>();
                float width = format == 0 ? 17.77778f : 4.5f;
                var planner = new FlyingHazardRoutes(player, spawner.platformPrefab, width);
                float model = GameplayPreferences.ModelScale;
                float size = model * (format == 1 ? 0.85f : 1f);
                float distance = format == 1 ? 0.9f : 1.4f;
                Vector2[] route = { new Vector2(-distance, 80f), new Vector2(0f, 81.8f),
                    new Vector2(distance, 83.6f), new Vector2(distance * 2f, 86f) };
                Stopwatch cacheWatch = Stopwatch.StartNew();
                var ordinary = planner.Build(route[1], route[2]);
                var initial = planner.Build(route[0], route[1]);
                Debug.Log($"HAZARD_CACHE_COST format={format} inertia={control} twoRoutesMs={cacheWatch.Elapsed.TotalMilliseconds:F3}");
                foreach (bool flying in new[] { false, true })
                {
                    float patrol = flying ? (control == 1 ? 0.20f : format == 1 ? 0.38f : 0.55f) * size : 0f;
                    Vector2 half = flying ? new Vector2(0.26f * size + patrol, 0.36f * size)
                        : new Vector2(0.51f, 0.235f) * size;
                    bool found = false;
                    double longest = 0f;
                    int attempts = 0;
                    var empty = new List<Rect>();
                    for (float y = 2.2f; y < 4.1f && !found; y += 0.2f)
                    for (float x = -0.5f; x < distance + 0.8f && !found; x += 0.2f)
                    {
                        Vector2 center = route[1] + new Vector2(x, y);
                        Rect area = new Rect(center - half, half * 2f);
                        bool relevant = flying ? planner.CanThreatenARealJump(ordinary, center, patrol, size, 1f)
                            : planner.CanThreatenStatic(ordinary, new Rect(center - new Vector2(0.49f, 0.22f) * size,
                                new Vector2(0.98f, 0.44f) * size));
                        if (!relevant) continue;
                        Stopwatch watch = Stopwatch.StartNew();
                        found = planner.CanTraverseChain(route, area, empty, out int simulated, initial);
                        watch.Stop();
                        attempts++;
                        longest = Math.Max(longest, watch.Elapsed.TotalMilliseconds);
                        if (found)
                        {
                            var sliced = new FlyingHazardRoutes.ChainResult();
                            IEnumerator work = planner.TraverseChain(route, area, empty, sliced, initial);
                            int yields = 0;
                            while (work.MoveNext()) yields++;
                            if (!sliced.Passed || sliced.SimulatedPaths != simulated || yields == 0)
                                throw new Exception("Frame-sliced proof differs from the synchronous proof.");
                            Debug.Log($"CONNECTED_HAZARD_EXAMPLE format={format} inertia={control} flying={flying} " +
                                $"center={center} simulated={simulated} elapsedMs={watch.Elapsed.TotalMilliseconds:F2} " +
                                $"attempts={attempts} longestMs={longest:F2} slices={sliced.Slices} " +
                                $"longestSliceMs={sliced.LongestSliceMilliseconds:F3}");
                            if (format == 0 && control == 0 && flying)
                                LifecycleChecks(player, spawner.platformPrefab, route, area, center,
                                    initial, size, patrol, width);
                            empty.Add(new Rect(-width, 79f, width * 2f, 10f));
                            if (planner.CanTraverseChain(route, area, empty, out _, initial))
                                throw new Exception("Connected proof ignored a blocking existing hazard.");
                            // The physical situation is identical after any
                            // number of wraps; inherited virtual X must not let
                            // a three-hop witness escape the hazard world copies.
                            Vector2[] translated = (Vector2[])route.Clone();
                            for (int node = 0; node < translated.Length; node++)
                                translated[node].x += width * 3f;
                            if (planner.CanTraverseChain(translated, area, empty, out _))
                                throw new Exception("Connected proof ignored an existing hazard after repeated wraps.");
                            Debug.Log($"CONNECTED_MULTIWRAP_PASS format={format} inertia={control} flying={flying}");
                        }
                    }
                    if (!found) throw new Exception($"No connected bypass: format={format}, inertia={control}, flying={flying}; attempts={attempts}, longestMs={longest:F2}");
                }
            }
            Debug.Log("CONNECTED_HAZARDS_PASS: nine inputs, actual inherited arrivals, three hops, meaningful hits, existing hazards.");
        }
        finally
        {
            if (hadOrientation) PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, orientation);
            else PlayerPrefs.DeleteKey(GameplayPreferences.OrientationKey);
            if (hadInertia) PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, inertia);
            else PlayerPrefs.DeleteKey(GameplayPreferences.InertiaKey);
            PlayerPrefs.Save();
        }
    }

    private static void LifecycleChecks(Player player, GameObject prefab, Vector2[] route, Rect area,
        Vector2 center, FlyingHazardRoutes.Paths initial, float size, float patrol, float width)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        Camera camera = new GameObject("Connected planner validation camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.aspect = width / 10f;
        camera.transform.position = new Vector3(0f, 60f, -10f);
        var generator = new HazardSpawner(camera, player, prefab, new TrapSettings());
        Type pendingType = typeof(HazardSpawner).GetNestedType("PendingPlacement", BindingFlags.NonPublic);
        object candidate = Activator.CreateInstance(pendingType);
        pendingType.GetField("area").SetValue(candidate, area);
        pendingType.GetField("center").SetValue(candidate, center);
        pendingType.GetField("route").SetValue(candidate, route);
        pendingType.GetField("initialPaths").SetValue(candidate, initial);
        pendingType.GetField("existing").SetValue(candidate, Array.Empty<Rect>());
        Array candidates = Array.CreateInstance(pendingType, 1);
        candidates.SetValue(candidate, 0);
        FieldInfo jobField = typeof(HazardSpawner).GetField("activeJob", flags);
        MethodInfo resolve = typeof(HazardSpawner).GetMethod("ResolvePending", flags);
        IList generated = (IList)typeof(HazardSpawner).GetField("hazards", flags).GetValue(generator);
        FieldInfo deadField = typeof(Player).GetField("isDead", flags);
        bool dead = (bool)deadField.GetValue(player);
        try
        {
            // Each blocked case starts fresh: home, transition, death, changed
            // layout token, newly visible obstacle, newly reserved coin path.
            for (int scenario = 0; scenario < 7; scenario++)
            {
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                deadField.SetValue(player, false);
                camera.transform.position = new Vector3(0f, 60f, -10f);
                CoinRouteReservations.ClearForCamera(camera);
                object token = new object();
                jobField.SetValue(generator, token);
                IEnumerator work = (IEnumerator)resolve.Invoke(generator,
                    new object[] { token, candidates, route[1], true, false, size,
                        new Vector2(patrol, 0.1f * size), 1f, 0f,
                        DoodlikHazard.FlightPattern.Horizontal });
                work.MoveNext();
                Time.timeScale = 0f;
                for (int frame = 0; frame < 3; frame++)
                    if (!work.MoveNext() || generated.Count != 0)
                        throw new Exception("Paused planner advanced or committed a hazard.");
                Time.timeScale = 1f;
                switch (scenario)
                {
                    case 0: RunFlow.RunActive = false; break;
                    case 1: RunFlow.IsTransitioning = true; break;
                    case 2: deadField.SetValue(player, true); break;
                    case 3: jobField.SetValue(generator, null); break;
                    case 4: camera.transform.position = new Vector3(0f, area.center.y, -10f); break;
                    case 5: CoinRouteReservations.Reserve(camera,
                        new[] { center + Vector2.left, center + Vector2.right }, Vector2.zero, Vector2.one * 0.1f); break;
                }
                int frames = 0;
                while (work.MoveNext()) if (++frames > 10000) throw new Exception("Planner did not finish.");
                if (generated.Count != (scenario == 6 ? 1 : 0))
                    throw new Exception("Planner lifecycle/visibility/coin reservation check failed: " + scenario);
            }
            Debug.Log("CONNECTED_LIFECYCLE_PASS pause, home, transition, death, layout, camera, coins, offscreen commit.");
        }
        finally
        {
            foreach (DoodlikHazard hazard in generated)
                if (hazard != null) UnityEngine.Object.DestroyImmediate(hazard.transform.root.gameObject);
            CoinRouteReservations.ClearForCamera(camera);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            deadField.SetValue(player, dead);
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }
}
#endif
