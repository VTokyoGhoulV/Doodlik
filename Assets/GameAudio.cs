using System;
using System.Collections;
using UnityEngine;

/// <summary>Original, lightweight music and sound effects shared by every scene.</summary>
[DisallowMultipleComponent]
public sealed class GameAudio : MonoBehaviour
{
    public enum Cue
    {
        Jump, Bush, Fall, Hurt, Coin, Shield, ShieldBreak, Boost,
        Slow, Purchase, Click, Zone, Record, Start
    }

    public const string MusicVolumeKey = "DoodlikMusicVolume";
    public const string EffectsVolumeKey = "DoodlikEffectsVolume";
    public const int MaximumEffectVoices = 8;
    private const int SampleRate = 22050;
    private const int CueCount = (int)Cue.Start + 1;
    private const float DefaultMusicVolume = 0.32f;
    private const float DefaultEffectsVolume = 0.75f;

    private static GameAudio instance;
    private static bool quitting;
    private static bool preferencesLoaded;
    private static float musicVolume;
    private static float effectsVolume;
    private static bool preferencesDirty;
    private static float saveAfter;

    private readonly AudioClip[] clips = new AudioClip[CueCount];
    private readonly double[] lastCueTimes = new double[CueCount];
    private readonly AudioSource[] voices = new AudioSource[MaximumEffectVoices];
    private readonly int[] voiceImportance = new int[MaximumEffectVoices];
    private readonly double[] voiceStarted = new double[MaximumEffectVoices];
    private readonly float[] voiceGain = new float[MaximumEffectVoices];
    private AudioSource music;
    private AudioClip musicClip;
    private bool initialized;
    private bool suspended;
    private float musicDuck = 1f;

    public static float MusicVolume
    {
        get { LoadPreferences(); return musicVolume; }
    }

    public static float EffectsVolume
    {
        get { LoadPreferences(); return effectsVolume; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        quitting = false;
        preferencesLoaded = false;
        preferencesDirty = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateAudio()
    {
        if (quitting || instance != null)
            return;

        instance = FindAnyObjectByType<GameAudio>();
        if (instance != null)
        {
            instance.Initialize();
            return;
        }

        new GameObject("Doodlik Audio").AddComponent<GameAudio>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        LoadPreferences();

        music = CreateSource("Music", 0);
        music.loop = true;
        music.volume = 0f;
        for (int i = 0; i < voices.Length; i++)
            voices[i] = CreateSource("Effect " + (i + 1), 64);

        for (int i = 0; i < CueCount; i++)
        {
            clips[i] = CreateEffect((Cue)i);
            lastCueTimes[i] = double.NegativeInfinity;
        }

        // Synthesis is one-time. Music is prepared a bar per frame while the title
        // screen opens, rather than blocking a phone with a long startup operation.
        StartCoroutine(PrepareMusic());
    }

    private AudioSource CreateSource(string sourceName, int priority)
    {
        GameObject child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.priority = priority;
        return source;
    }

    public static void Play(Cue cue)
    {
        int index = (int)cue;
        if (!Application.isPlaying || quitting || index < 0 || index >= CueCount)
            return;
        CreateAudio();
        if (instance != null)
            instance.PlayEffect(cue);
    }

    private void PlayEffect(Cue cue)
    {
        if (!initialized || suspended || EffectsVolume <= 0f)
            return;

        int index = (int)cue;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - lastCueTimes[index] < MinimumInterval(cue))
            return;

        int importance = Importance(cue);
        int selected = -1;
        for (int i = 0; i < voices.Length; i++)
        {
            if (!voices[i].isPlaying)
            {
                selected = i;
                break;
            }
            if (selected < 0 || voiceImportance[i] < voiceImportance[selected] ||
                (voiceImportance[i] == voiceImportance[selected] && voiceStarted[i] < voiceStarted[selected]))
                selected = i;
        }

        if (selected < 0 || (voices[selected].isPlaying && voiceImportance[selected] > importance))
            return;

        lastCueTimes[index] = now;
        AudioSource source = voices[selected];
        source.Stop();
        source.clip = clips[index];
        source.ignoreListenerPause = IsInterfaceCue(cue);
        // Do not consume UnityEngine.Random: sound variation must never change
        // platform generation or the difficulty of a run.
        source.pitch = cue == Cue.Jump ? 0.98f + (float)(now % 0.1) * 0.4f : 1f;
        voiceGain[selected] = Gain(cue);
        source.volume = effectsVolume * voiceGain[selected];
        source.priority = 128 - importance * 24;
        voiceImportance[selected] = importance;
        voiceStarted[selected] = now;
        source.Play();
    }

    public static void SetMusicVolume(float volume)
    {
        LoadPreferences();
        musicVolume = ValidVolume(volume, DefaultMusicVolume);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        MarkPreferencesDirty();
        if (instance != null && instance.music != null)
            instance.music.volume = musicVolume * instance.musicDuck;
    }

    public static void SetEffectsVolume(float volume)
    {
        LoadPreferences();
        effectsVolume = ValidVolume(volume, DefaultEffectsVolume);
        PlayerPrefs.SetFloat(EffectsVolumeKey, effectsVolume);
        MarkPreferencesDirty();
        if (instance == null)
            return;
        for (int i = 0; i < instance.voices.Length; i++)
            if (instance.voices[i] != null)
                instance.voices[i].volume = effectsVolume * instance.voiceGain[i];
    }

    private static void LoadPreferences()
    {
        if (preferencesLoaded)
            return;
        musicVolume = ValidVolume(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume), DefaultMusicVolume);
        effectsVolume = ValidVolume(PlayerPrefs.GetFloat(EffectsVolumeKey, DefaultEffectsVolume), DefaultEffectsVolume);
        preferencesLoaded = true;
    }

    private static float ValidVolume(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
    }

    private static void MarkPreferencesDirty()
    {
        preferencesDirty = true;
        saveAfter = Time.realtimeSinceStartup + 0.5f;
    }

    private static void SavePreferences()
    {
        if (!preferencesDirty)
            return;
        PlayerPrefs.Save();
        preferencesDirty = false;
    }

    private void Update()
    {
        // UI and defeat cues still play at timeScale == 0; only the music ducks.
        float duckTarget = Time.timeScale <= 0f ? 0.55f : 1f;
        musicDuck = Mathf.MoveTowards(musicDuck, duckTarget, Time.unscaledDeltaTime * 1.7f);
        if (music != null && musicClip != null)
            music.volume = MusicVolume * musicDuck;
        if (preferencesDirty && Time.realtimeSinceStartup >= saveAfter)
            SavePreferences();
    }

    private void OnApplicationPause(bool paused)
    {
        suspended = paused;
        if (paused)
        {
            SavePreferences();
            if (music != null)
                music.Pause();
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] != null)
                    voices[i].Stop();
        }
        else if (music != null && musicClip != null)
        {
            music.UnPause();
            if (!music.isPlaying)
                music.Play();
        }
    }

    private void OnApplicationQuit()
    {
        quitting = true;
        SavePreferences();
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;
        SavePreferences();
        instance = null;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null)
                Destroy(clips[i]);
        if (musicClip != null)
            Destroy(musicClip);
    }

    private static bool IsInterfaceCue(Cue cue)
    {
        return cue == Cue.Click || cue == Cue.Purchase || cue == Cue.Start ||
               cue == Cue.Record || cue == Cue.Fall || cue == Cue.Hurt;
    }

    private static int Importance(Cue cue)
    {
        if (cue == Cue.Fall || cue == Cue.Hurt || cue == Cue.ShieldBreak)
            return 4;
        if (cue == Cue.Record || cue == Cue.Purchase || cue == Cue.Zone || cue == Cue.Start)
            return 3;
        return cue == Cue.Jump || cue == Cue.Coin ? 1 : 2;
    }

    private static float MinimumInterval(Cue cue)
    {
        switch (cue)
        {
            case Cue.Jump: return 0.13f;
            case Cue.Coin: return 0.055f;
            case Cue.Click: return 0.055f;
            case Cue.Record: return 1.3f;
            case Cue.Zone: return 1f;
            case Cue.Fall:
            case Cue.Hurt: return 0.5f;
            default: return 0.15f;
        }
    }

    private static float Gain(Cue cue)
    {
        switch (cue)
        {
            case Cue.Jump: return 0.40f;
            case Cue.Click: return 0.42f;
            case Cue.Coin: return 0.50f;
            case Cue.Bush: return 0.60f;
            case Cue.Zone: return 0.72f;
            default: return 0.80f;
        }
    }

    private static AudioClip CreateEffect(Cue cue)
    {
        float duration;
        switch (cue)
        {
            case Cue.Jump: duration = 0.18f; break;
            case Cue.Bush: duration = 0.40f; break;
            case Cue.Fall: duration = 0.65f; break;
            case Cue.Hurt: duration = 0.28f; break;
            case Cue.Coin: duration = 0.26f; break;
            case Cue.Shield: duration = 0.60f; break;
            case Cue.ShieldBreak: duration = 0.36f; break;
            case Cue.Boost: duration = 0.55f; break;
            case Cue.Slow: duration = 0.65f; break;
            case Cue.Purchase: duration = 0.78f; break;
            case Cue.Click: duration = 0.07f; break;
            case Cue.Zone: duration = 1.05f; break;
            case Cue.Record: duration = 1.42f; break;
            default: duration = 0.64f; break;
        }

        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        uint noiseState = (uint)((int)cue + 1) * 7919u;
        float smoothedNoise = 0f;
        double phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float progress = t / duration;
            float frequency = 440f;
            float decay = 5f;
            float noiseAmount = 0f;
            float toneAmount = 0.7f;
            switch (cue)
            {
                case Cue.Jump:
                    frequency = Mathf.Lerp(410f, 740f, 1f - Mathf.Pow(1f - progress, 3f));
                    decay = 5.5f;
                    break;
                case Cue.Bush:
                    frequency = 180f + 40f * Mathf.Sin(t * 37f);
                    noiseAmount = 0.55f;
                    toneAmount = 0.30f;
                    decay = 3.5f;
                    break;
                case Cue.Fall:
                    frequency = Mathf.Lerp(680f, 140f, progress);
                    decay = 2.5f;
                    break;
                case Cue.Hurt:
                    frequency = Mathf.Lerp(225f, 85f, progress);
                    noiseAmount = 0.70f * Mathf.Exp(-progress * 12f);
                    toneAmount = 0.58f;
                    break;
                case Cue.ShieldBreak:
                    frequency = Mathf.Lerp(1200f, 290f, progress);
                    noiseAmount = 0.35f;
                    toneAmount = 0.52f;
                    break;
                case Cue.Boost:
                    frequency = Mathf.Lerp(280f, 1120f, progress);
                    noiseAmount = 0.15f;
                    decay = 2.5f;
                    break;
                case Cue.Click:
                    frequency = 460f;
                    noiseAmount = 0.12f;
                    decay = 9f;
                    break;
                default:
                    // The remaining cues use explicitly voiced little arpeggios below.
                    toneAmount = 0f;
                    break;
            }

            phase += Math.PI * 2.0 * frequency / SampleRate;
            noiseState = noiseState * 1664525u + 1013904223u;
            float noise = (noiseState >> 8) / 8388607.5f - 1f;
            smoothedNoise += (noise - smoothedNoise) * 0.22f;
            float attack = Mathf.Min(1f, t / 0.004f);
            float release = Mathf.Min(1f, (duration - t) / 0.018f);
            float envelope = attack * release * Mathf.Exp(-progress * decay);
            float tone = (float)(Math.Sin(phase) + 0.18 * Math.Sin(phase * 2.0));
            samples[i] = (tone * toneAmount + smoothedNoise * noiseAmount) * envelope * 0.64f;
        }

        switch (cue)
        {
            case Cue.Coin:
                AddNote(samples, 0f, 0.14f, 83, 0.48f, 0);
                AddNote(samples, 0.075f, 0.18f, 88, 0.48f, 0);
                break;
            case Cue.Shield:
                AddArpeggio(samples, new[] { 72, 76, 79 }, 0.11f, 0.32f, 0.36f);
                break;
            case Cue.Slow:
                AddArpeggio(samples, new[] { 79, 76, 72, 67 }, 0.13f, 0.23f, 0.38f);
                break;
            case Cue.Purchase:
                AddArpeggio(samples, new[] { 72, 76, 79, 84 }, 0.115f, 0.4f, 0.45f);
                break;
            case Cue.Zone:
                AddArpeggio(samples, new[] { 67, 72, 76, 79, 84 }, 0.15f, 0.40f, 0.40f);
                break;
            case Cue.Record:
                AddArpeggio(samples, new[] { 72, 76, 79, 84, 79, 88, 84 }, 0.14f, 0.50f, 0.40f);
                AddNote(samples, 0.84f, 0.55f, 72, 0.22f, 1);
                break;
            case Cue.Start:
                AddArpeggio(samples, new[] { 67, 72, 76, 79 }, 0.1f, 0.32f, 0.38f);
                break;
        }

        Normalize(samples, 0.80f);
        AudioClip clip = AudioClip.Create("Doodlik " + cue, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void AddArpeggio(float[] data, int[] notes, float interval, float duration, float gain)
    {
        for (int i = 0; i < notes.Length; i++)
            AddNote(data, i * interval, duration, notes[i], gain, 0);
    }

    private IEnumerator PrepareMusic()
    {
        const float beat = 0.60f;
        const float barLength = beat * 4f;
        const int bars = 8;
        float[] data = new float[Mathf.RoundToInt(bars * barLength * SampleRate)];
        int[][] melody =
        {
            new[] { 76, 79, 81, -1, 79, 76, 74, -1 },
            new[] { 72, 76, 79, -1, 76, 72, 69, -1 },
            new[] { 77, 81, 79, -1, 77, 76, 72, -1 },
            new[] { 74, 79, 76, 74, 71, -1, 74, -1 },
            new[] { 76, -1, 79, 84, 83, 79, 76, -1 },
            new[] { 79, 76, 71, -1, 76, 79, 83, -1 },
            new[] { 81, -1, 77, 74, 76, 77, 74, -1 },
            new[] { 79, 77, 74, 71, 74, -1, 67, -1 }
        };
        int[][] chords =
        {
            new[] { 48, 60, 64, 67 }, new[] { 45, 57, 60, 64 },
            new[] { 41, 57, 60, 65 }, new[] { 43, 59, 62, 67 },
            new[] { 48, 60, 64, 67 }, new[] { 40, 55, 59, 64 },
            new[] { 38, 57, 62, 65 }, new[] { 43, 59, 62, 67 }
        };

        for (int bar = 0; bar < bars; bar++)
        {
            float start = bar * barLength;
            for (int step = 0; step < melody[bar].Length; step++)
            {
                int note = melody[bar][step];
                if (note >= 0)
                    AddNote(data, start + step * beat * 0.5f, 0.68f, note,
                        step % 2 == 0 ? 0.18f : 0.14f, 0, true);
                AddPercussion(data, start + step * beat * 0.5f, false, 0.022f, true);
            }

            for (int half = 0; half < 2; half++)
            {
                float position = start + half * beat * 2f;
                AddNote(data, position, 0.80f, chords[bar][0] + (half == 1 ? 7 : 0), 0.18f, 2, true);
                AddPercussion(data, position, true, 0.065f, true);
                for (int chord = 1; chord < 4; chord++)
                    AddNote(data, position + beat + (chord - 1) * 0.015f,
                        0.55f, chords[bar][chord], 0.047f, 1, true);
            }
            yield return null;
        }

        Normalize(data, 0.68f);
        musicClip = AudioClip.Create("Doodlik - Little Paws Above the Meadow", data.Length, 1, SampleRate, false);
        musicClip.SetData(data, 0);
        music.clip = musicClip;
        if (!suspended)
            music.Play();
    }

    // Instrument 0: rounded music-box pluck, 1: soft wooden chord, 2: warm bass.
    // Wrapping note tails makes the music loop continuous at the sample boundary.
    private static void AddNote(float[] target, float start, float duration, int midi,
        float gain, int instrument, bool wrap = false)
    {
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.CeilToInt(duration * SampleRate);
        double step = Math.PI * 2.0 * 440.0 * Math.Pow(2.0, (midi - 69) / 12.0) / SampleRate;
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            int position = first + i;
            if (position >= target.Length)
            {
                if (!wrap)
                    break;
                position %= target.Length;
            }

            float t = i / (float)SampleRate;
            float normalized = t / duration;
            float envelope = Mathf.Min(1f, t / 0.006f) *
                Mathf.Min(1f, (duration - t) / 0.03f) * Mathf.Exp(-normalized * 5f);
            double sample = Math.Sin(phase);
            if (instrument == 0)
                sample += 0.24 * Math.Sin(phase * 2.0) * Math.Exp(-normalized * 6f) +
                          0.07 * Math.Sin(phase * 3.0) * Math.Exp(-normalized * 8f);
            else if (instrument == 1)
                sample += 0.30 * Math.Sin(phase * 2.0) + 0.09 * Math.Sin(phase * 3.0);
            else
                sample += 0.12 * Math.Sin(phase * 2.0);
            target[position] += (float)sample * envelope * gain;
            phase += step;
        }
    }

    private static void AddPercussion(float[] target, float start, bool kick, float gain, bool wrap)
    {
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.RoundToInt((kick ? 0.13f : 0.045f) * SampleRate);
        uint noiseState = (uint)(first + 17);
        float previousNoise = 0;
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            int position = first + i;
            if (position >= target.Length)
            {
                if (!wrap)
                    break;
                position %= target.Length;
            }
            float fraction = i / (float)count;
            float envelope = Mathf.Min(1f, i / (SampleRate * 0.002f)) *
                Mathf.Exp(-fraction * 9f) * (1f - fraction);
            float sample;
            if (kick)
            {
                phase += Math.PI * 2.0 * Mathf.Lerp(110f, 58f, fraction) / SampleRate;
                sample = (float)Math.Sin(phase);
            }
            else
            {
                noiseState = noiseState * 1664525u + 1013904223u;
                float noise = (noiseState >> 8) / 8388607.5f - 1f;
                sample = (noise - previousNoise) * 0.5f;
                previousNoise = noise;
            }
            target[position] += sample * envelope * gain;
        }
    }

    private static void Normalize(float[] samples, float maximum)
    {
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        // Preserve intentional quiet layers; this is a safety limiter, not a
        // loudness boost that would make tiny menu clicks as loud as a fanfare.
        float scale = peak > maximum ? maximum / peak : 1f;
        if (scale >= 1f)
            return;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= scale;
    }
}
