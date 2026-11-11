#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FlyingHazardValidation
{
    // Dedicated clone/batch Editor only. Opens scenes but never saves them.
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode for flying route validation.");
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int format = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int control = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        int checkedModes = 0;
        try
        {
            for (int orientation = 0; orientation < (GameplayPreferences.IsDesktopBuild ? 1 : 2); orientation++)
            for (int inertia = 0; inertia < 2; inertia++)
            {
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, orientation);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, inertia);
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
                Spawner spawner = UnityEngine.Object.FindAnyObjectByType<Spawner>();
                float width = orientation == 0 ? 17.77778f : 4.5f;
                FlyingHazardRoutes planner = new FlyingHazardRoutes(player, spawner.platformPrefab, width);
                bool found = false;
                float scale = GameplayPreferences.ModelScale * (orientation == 1 ? 0.85f : 1f);
                float patrol = (orientation == 1 ? 0.38f : 0.55f) * scale;
                var existing = new List<Rect>();
                FlyingHazardRoutes.Paths wrapped = planner.Build(new Vector2(width * 0.5f - 0.2f, 80f),
                    new Vector2(-width * 0.5f + 0.3f, 82f));
                if (!planner.HasSafeAlternatives(wrapped, new Rect(-1f, 100f, 2f, 1f), existing))
                    throw new Exception("Wrapped jump witness failed with an unobstructed route.");
                foreach (float gap in new[] { 1.8f, 2.4f, 3f })
                {
                    if (found) break;
                    foreach (float distance in new[] { 0.7f, 1.4f, 2.1f, 3f })
                    {
                        if (found) break;
                        if (distance > width * 0.45f) continue;
                        Vector2 start = new Vector2(-width * 0.25f, 80f);
                        Vector2 end = start + new Vector2(distance, gap);
                        FlyingHazardRoutes.Paths paths = planner.Build(start, end);
                        if (!paths.Complete) continue;
                        for (float y = 0.8f; y < 5.5f && !found; y += 0.25f)
                        for (float x = -0.5f; x <= distance + 0.5f && !found; x += 0.2f)
                        {
                            Vector2 center = start + new Vector2(x, y);
                            Vector2 half = new Vector2(0.26f * scale + patrol, 0.36f * scale);
                            Rect area = new Rect(center - half, half * 2f);
                            if (!planner.HasSafeAlternatives(paths, area, existing) ||
                                !planner.CanThreatenARealJump(paths, center, patrol, scale, 1f)) continue;
                            found = true;
                            // Combined obstacle proof must reject a wall that covers
                            // every alternative, not approve hazards independently.
                            existing.Add(new Rect(start.x - width, 79f, width * 2f, 8f));
                            if (planner.HasSafeAlternatives(paths, area, existing))
                                throw new Exception("Combined hazards did not invalidate blocked alternatives.");
                            Debug.Log($"FLYING_ROUTE_EXAMPLE format={orientation} inertia={inertia} " +
                                $"from={start} to={end} seed={center}: relevant hit + all-start-state safe detour");
                        }
                    }
                }
                if (!found) throw new Exception($"No meaningful safe flying challenge found: format={orientation}, inertia={inertia}");
                checkedModes++;
            }
            Debug.Log("FLYING_ROUTES_PASS modes=" + checkedModes +
                " real-landing alternatives, active collision counterexamples, combined-hazard rejection");
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadFormat, format);
            Restore(GameplayPreferences.InertiaKey, hadControl, control);
            PlayerPrefs.Save();
        }
    }

    private static void Restore(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key);
    }
}
#endif
