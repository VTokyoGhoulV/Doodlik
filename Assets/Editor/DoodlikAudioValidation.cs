using System;
using UnityEditor;
using UnityEngine;

/// <summary>Non-mutating checks after the generated soundtrack has finished preparing.</summary>
public static class DoodlikAudioValidation
{
    [MenuItem("Doodlik/Validation/Check Generated Audio (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Audio validation requires Play Mode. Wait for the title music first.");

        GameAudio[] roots = UnityEngine.Object.FindObjectsByType<GameAudio>();
        Require(roots.Length == 1, "There must be exactly one persistent GameAudio.");
        AudioSource[] sources = roots[0].GetComponentsInChildren<AudioSource>();
        Require(sources.Length == GameAudio.MaximumEffectVoices + 1, "Audio voice count is bounded.");
        foreach (AudioSource source in sources)
            Require(source.spatialBlend == 0f && !source.playOnAwake, "Audio must be explicitly started and two-dimensional.");

        int listeners = 0;
        foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>())
            if (listener.enabled && listener.gameObject.activeInHierarchy)
                listeners++;
        Require(listeners == 1, "The active scene needs exactly one enabled AudioListener.");

        int effectCount = 0;
        int musicCount = 0;
        long bytes = 0;
        foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (!clip.name.StartsWith("Doodlik ", StringComparison.Ordinal))
                continue;
            bool isMusic = clip.name.StartsWith("Doodlik - ", StringComparison.Ordinal);
            if (isMusic) musicCount++;
            else effectCount++;
            Require(clip.channels == 1 && clip.frequency == 22050, "Generated audio must use compact mono PCM.");
            Require(clip.samples > 0 && clip.length > 0, "Generated clip cannot be empty.");
            float[] samples = new float[clip.samples];
            Require(clip.GetData(samples, 0), "Generated audio data must remain readable.");
            double energy = 0;
            float peak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                Require(!float.IsNaN(samples[i]) && !float.IsInfinity(samples[i]), "Audio samples must be finite.");
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                energy += samples[i] * samples[i];
            }
            Require(energy > 0.0001 && peak <= 0.801f, "Every clip needs audible content and unclipped samples.");
            if (isMusic)
                Require(Mathf.Abs(samples[0] - samples[samples.Length - 1]) < 0.04f, "Music loop must not have a hard waveform seam.");
            else
                Require(Mathf.Abs(samples[samples.Length - 1]) < 0.01f, "Effects must end without an abrupt waveform cut.");
            bytes += samples.LongLength * sizeof(float);
        }

        Require(effectCount == Enum.GetValues(typeof(GameAudio.Cue)).Length, "All sound cues must be prepared once.");
        Require(musicCount == 1, "Exactly one prepared music loop is expected after eight frames.");
        Require(bytes < 4 * 1024 * 1024, "Generated audio PCM must stay under 4 MiB.");
        Debug.Log("Doodlik audio validation PASS: " + effectCount + " effects, one seamless loop, " +
                  sources.Length + " pooled sources, " + (bytes / 1024) + " KiB PCM.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
