#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;

namespace Empire.Game.Platform;

/// <summary>Reusable procedural sound cues exposed by <see cref="ProceduralAudioService"/>.</summary>
public enum AudioCue
{
    Click,
    Select,
    Move,
    Attack,
    Build,
    Age,
    Win,
    Lose
}

/// <summary>
/// Synthesizes the game's sound palette once, then reuses pre-created voices and looping
/// layers. Update is allocation-free and only adjusts already-playing layer volumes.
/// Construct this service after MonoGame has initialized its audio subsystem.
/// </summary>
public sealed class ProceduralAudioService : IDisposable
{
    private const int SampleRate = 44_100;
    private const int CueCount = 8;
    private const int VoicesPerCue = 3;
    private const float Tau = MathF.PI * 2f;

    private static readonly float[] AgeNotes = [261.626f, 329.628f, 391.995f, 523.251f, 659.255f, 783.991f];
    private static readonly float[] VictoryNotes = [261.626f, 329.628f, 391.995f, 523.251f, 659.255f, 783.991f, 1_046.502f];
    private static readonly float[] DefeatNotes = [329.628f, 293.665f, 261.626f, 220f, 195.998f, 164.814f];
    private static readonly float[] CalmNotes = [220f, 261.625f, 329.625f, 293.625f, 220f, 329.625f, 261.625f, 196f];

    private readonly CueVoicePool[] _cuePools = new CueVoicePool[CueCount];
    private readonly SoundEffect _ambientEffect;
    private readonly SoundEffect _calmMusicEffect;
    private readonly SoundEffect _battleMusicEffect;
    private readonly SoundEffectInstance _ambientLoop;
    private readonly SoundEffectInstance _calmMusicLoop;
    private readonly SoundEffectInstance _battleMusicLoop;

    private float _masterVolume = 0.8f;
    private float _effectsVolume = 0.9f;
    private float _ambientVolume = 0.65f;
    private float _musicVolume = 0.7f;
    private float _ambientLevel;
    private float _calmMusicLevel;
    private float _battleMusicLevel;
    private float _adaptiveClock;
    private bool _adaptiveAudioRunning;
    private bool _muted;
    private bool _disposed;

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Clamp01(value);
            ApplyAllVolumes();
        }
    }

    public float EffectsVolume
    {
        get => _effectsVolume;
        set
        {
            _effectsVolume = Clamp01(value);
            ApplyAllVolumes();
        }
    }

    public float AmbientVolume
    {
        get => _ambientVolume;
        set
        {
            _ambientVolume = Clamp01(value);
            ApplyLoopVolumes();
        }
    }

    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Clamp01(value);
            ApplyLoopVolumes();
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            if (_muted == value)
            {
                return;
            }

            _muted = value;
            ApplyAllVolumes();
        }
    }

    public bool IsAdaptiveAudioRunning => _adaptiveAudioRunning;

    public ProceduralAudioService()
    {
        for (var cueIndex = 0; cueIndex < CueCount; cueIndex++)
        {
            var cue = (AudioCue)cueIndex;
            _cuePools[cueIndex] = new CueVoicePool(CreateCue(cue), VoicesPerCue);
        }

        _ambientEffect = CreateLoop(LoopKind.Ambient, 8f);
        _calmMusicEffect = CreateLoop(LoopKind.CalmMusic, 8f);
        _battleMusicEffect = CreateLoop(LoopKind.BattleMusic, 8f);

        _ambientLoop = CreateLoopInstance(_ambientEffect);
        _calmMusicLoop = CreateLoopInstance(_calmMusicEffect);
        _battleMusicLoop = CreateLoopInstance(_battleMusicEffect);
    }

    /// <summary>
    /// Attempts to initialize audio without making a missing audio device fatal to startup.
    /// </summary>
    public static bool TryCreate(out ProceduralAudioService? service, out string? error)
    {
        try
        {
            service = new ProceduralAudioService();
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is NoAudioHardwareException or InvalidOperationException)
        {
            service = null;
            error = $"無法初始化音效裝置：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Plays a pre-synthesized cue from a small reusable voice pool. Volume, pitch and pan
    /// are clamped to MonoGame's safe ranges. Returns false when muted or disposed.
    /// </summary>
    public bool Play(AudioCue cue, float volume = 1f, float pitch = 0f, float pan = 0f)
    {
        if (_disposed || _muted || _masterVolume <= 0f || _effectsVolume <= 0f)
        {
            return false;
        }

        var cueIndex = (int)cue;
        if ((uint)cueIndex >= CueCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cue), cue, "未知的音效提示。 ");
        }

        _cuePools[cueIndex].Play(
            Clamp01(volume),
            Clamp(pitch, -1f, 1f),
            Clamp(pan, -1f, 1f),
            EffectiveMasterVolume * _effectsVolume);
        return true;
    }

    /// <summary>
    /// Starts phase-aligned ambience, calm music and battle music layers. Silent layers
    /// remain playing so intensity changes cross-fade without timing discontinuities.
    /// </summary>
    public void StartAdaptiveAudio()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_adaptiveAudioRunning)
        {
            return;
        }

        _adaptiveAudioRunning = true;
        _adaptiveClock = 0f;
        _ambientLevel = 0f;
        _calmMusicLevel = 0f;
        _battleMusicLevel = 0f;
        ApplyLoopVolumes();

        StartOrResume(_ambientLoop);
        StartOrResume(_calmMusicLoop);
        StartOrResume(_battleMusicLoop);
    }

    /// <summary>Stops the adaptive looping layers immediately.</summary>
    public void StopAdaptiveAudio()
    {
        if (_disposed || !_adaptiveAudioRunning)
        {
            return;
        }

        _adaptiveAudioRunning = false;
        _ambientLoop.Stop();
        _calmMusicLoop.Stop();
        _battleMusicLoop.Stop();
        _ambientLevel = 0f;
        _calmMusicLevel = 0f;
        _battleMusicLevel = 0f;
    }

    /// <summary>
    /// Updates adaptive audio. Activity and danger are normalized 0..1 values. This method
    /// performs no collection growth, PCM generation, delegate creation or voice creation.
    /// </summary>
    public void Update(float elapsedSeconds, float activityLevel, float dangerLevel)
    {
        if (_disposed || !_adaptiveAudioRunning)
        {
            return;
        }

        var delta = float.IsFinite(elapsedSeconds) ? Clamp(elapsedSeconds, 0f, 0.25f) : 0f;
        var activity = Clamp01(activityLevel);
        var danger = Clamp01(dangerLevel);

        _adaptiveClock += delta;
        if (_adaptiveClock >= 256f)
        {
            _adaptiveClock -= 256f;
        }

        var windBreath = 0.91f + MathF.Sin(_adaptiveClock * 0.31f) * 0.09f;
        var ambientTarget = (0.22f + activity * 0.08f) * (1f - danger * 0.2f) * windBreath;
        var calmTarget = (0.12f + activity * 0.23f) * (1f - danger);
        var battleTarget = danger * (0.18f + danger * 0.34f);

        // Exponential smoothing is frame-rate independent and prevents audible zippering.
        var response = 1f - MathF.Exp(-delta * 2.8f);
        _ambientLevel += (ambientTarget - _ambientLevel) * response;
        _calmMusicLevel += (calmTarget - _calmMusicLevel) * response;
        _battleMusicLevel += (battleTarget - _battleMusicLevel) * response;

        ApplyLoopVolumes();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAdaptiveAudio();
        _disposed = true;

        _ambientLoop.Dispose();
        _calmMusicLoop.Dispose();
        _battleMusicLoop.Dispose();
        _ambientEffect.Dispose();
        _calmMusicEffect.Dispose();
        _battleMusicEffect.Dispose();

        for (var cueIndex = 0; cueIndex < _cuePools.Length; cueIndex++)
        {
            _cuePools[cueIndex]?.Dispose();
        }
    }

    private float EffectiveMasterVolume => _muted ? 0f : _masterVolume;

    private void ApplyAllVolumes()
    {
        if (_disposed)
        {
            return;
        }

        var effectsScale = EffectiveMasterVolume * _effectsVolume;
        for (var cueIndex = 0; cueIndex < _cuePools.Length; cueIndex++)
        {
            _cuePools[cueIndex]?.RefreshVolumes(effectsScale);
        }

        ApplyLoopVolumes();
    }

    private void ApplyLoopVolumes()
    {
        if (_disposed)
        {
            return;
        }

        var master = EffectiveMasterVolume;
        _ambientLoop.Volume = Clamp01(master * _ambientVolume * _ambientLevel);
        _calmMusicLoop.Volume = Clamp01(master * _musicVolume * _calmMusicLevel);
        _battleMusicLoop.Volume = Clamp01(master * _musicVolume * _battleMusicLevel);
    }

    private static SoundEffectInstance CreateLoopInstance(SoundEffect effect)
    {
        var instance = effect.CreateInstance();
        instance.IsLooped = true;
        instance.Volume = 0f;
        return instance;
    }

    private static void StartOrResume(SoundEffectInstance instance)
    {
        if (instance.State == SoundState.Paused)
        {
            instance.Resume();
        }
        else if (instance.State == SoundState.Stopped)
        {
            instance.Play();
        }
    }

    private static SoundEffect CreateCue(AudioCue cue)
    {
        var duration = cue switch
        {
            AudioCue.Click => 0.08f,
            AudioCue.Select => 0.16f,
            AudioCue.Move => 0.22f,
            AudioCue.Attack => 0.24f,
            AudioCue.Build => 0.28f,
            AudioCue.Age => 1.15f,
            AudioCue.Win => 1.85f,
            AudioCue.Lose => 1.75f,
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null)
        };

        var sampleCount = (int)(duration * SampleRate);
        var pcm = GC.AllocateUninitializedArray<byte>(sampleCount * sizeof(short));
        var noiseState = 0xA341_316Cu ^ ((uint)cue + 1u) * 0x9E37_79B9u;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var time = sampleIndex / (float)SampleRate;
            var sample = CueSample(cue, time, duration, ref noiseState);
            WriteSample(pcm, sampleIndex, sample);
        }

        return new SoundEffect(pcm, SampleRate, AudioChannels.Mono);
    }

    private static float CueSample(AudioCue cue, float time, float duration, ref uint noiseState)
    {
        float sample;
        switch (cue)
        {
            case AudioCue.Click:
            {
                var envelope = MathF.Exp(-time * 38f);
                var frequency = 1_250f - time * 5_800f;
                sample = envelope * (Tone(frequency, time) * 0.68f + NextNoise(ref noiseState) * 0.12f);
                break;
            }
            case AudioCue.Select:
            {
                var envelope = MathF.Exp(-time * 12f);
                var secondTone = time > 0.055f ? Tone(1_140f, time - 0.055f) * MathF.Exp(-(time - 0.055f) * 16f) : 0f;
                sample = Tone(720f, time) * envelope * 0.48f + secondTone * 0.38f;
                break;
            }
            case AudioCue.Move:
            {
                var envelope = MathF.Exp(-time * 9f);
                var frequency = 320f - time * 650f;
                var hoof = Impact(time, 0.105f, 34f) * Tone(118f, time) * 0.48f;
                sample = Tone(frequency, time) * envelope * 0.33f + hoof + NextNoise(ref noiseState) * envelope * 0.07f;
                break;
            }
            case AudioCue.Attack:
            {
                var impact = MathF.Exp(-time * 22f);
                var ring = Tone(1_780f, time) * MathF.Exp(-time * 13f);
                sample = NextNoise(ref noiseState) * impact * 0.55f + Tone(92f, time) * impact * 0.52f + ring * 0.19f;
                break;
            }
            case AudioCue.Build:
            {
                var first = Impact(time, 0f, 27f);
                var second = Impact(time, 0.135f, 30f);
                var impacts = first + second * 0.76f;
                sample = impacts * (Tone(176f, time) * 0.47f + Tone(1_340f, time) * 0.16f + NextNoise(ref noiseState) * 0.14f);
                break;
            }
            case AudioCue.Age:
            {
                var step = Math.Min((int)(time / 0.17f), AgeNotes.Length - 1);
                var localTime = time - step * 0.17f;
                var envelope = MathF.Exp(-localTime * 5.8f) * FadeOut(time, duration, 0.18f);
                var note = AgeNotes[step];
                sample = envelope * (Tone(note, localTime) * 0.36f + Tone(note * 2f, localTime) * 0.18f);
                break;
            }
            case AudioCue.Win:
            {
                var step = Math.Min((int)(time / 0.24f), VictoryNotes.Length - 1);
                var localTime = time - step * 0.24f;
                var note = VictoryNotes[step];
                var fanfare = MathF.Exp(-localTime * 3.5f) * FadeOut(time, duration, 0.2f);
                var chordRise = MathF.Min(1f, time * 3f) * FadeOut(time, duration, 0.38f);
                sample = fanfare * (Tone(note, localTime) * 0.34f + Tone(note * 1.5f, localTime) * 0.12f)
                    + chordRise * (Tone(130.813f, time) + Tone(164.814f, time) + Tone(195.998f, time)) * 0.075f;
                break;
            }
            case AudioCue.Lose:
            {
                var step = Math.Min((int)(time / 0.28f), DefeatNotes.Length - 1);
                var localTime = time - step * 0.28f;
                var note = DefeatNotes[step];
                var envelope = MathF.Exp(-localTime * 3.1f) * FadeOut(time, duration, 0.32f);
                sample = envelope * (Tone(note, localTime) * 0.34f + Tone(note * 0.5f, localTime) * 0.22f)
                    + NextNoise(ref noiseState) * MathF.Exp(-time * 3f) * 0.025f;
                break;
            }
            default:
                sample = 0f;
                break;
        }

        return SoftLimit(sample * 0.82f);
    }

    private static SoundEffect CreateLoop(LoopKind kind, float duration)
    {
        var sampleCount = (int)(duration * SampleRate);
        var pcm = GC.AllocateUninitializedArray<byte>(sampleCount * sizeof(short));
        var noiseState = 0xD1B5_4A35u ^ ((uint)kind + 1u) * 0x94D0_49BBu;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var time = sampleIndex / (float)SampleRate;
            var sample = kind switch
            {
                LoopKind.Ambient => AmbientSample(time, ref noiseState),
                LoopKind.CalmMusic => CalmMusicSample(time),
                LoopKind.BattleMusic => BattleMusicSample(time, ref noiseState),
                _ => 0f
            };
            WriteSample(pcm, sampleIndex, SoftLimit(sample));
        }

        return new SoundEffect(pcm, SampleRate, AudioChannels.Mono);
    }

    private static float AmbientSample(float time, ref uint noiseState)
    {
        // Every oscillator completes an integer number of cycles in eight seconds.
        var wind = Tone(0.25f, time) * 0.12f + Tone(0.625f, time) * 0.07f;
        var airy = NextNoise(ref noiseState) * (0.018f + (Tone(0.125f, time) + 1f) * 0.009f);
        var leaves = Tone(17f, time + Tone(0.25f, time) * 0.003f) * 0.018f;
        var birdOne = Chirp(time, 1.35f, 1_180f, 1_740f, 0.19f) * 0.11f;
        var birdTwo = Chirp(time, 5.15f, 980f, 1_460f, 0.24f) * 0.085f;
        return wind + airy + leaves + birdOne + birdTwo;
    }

    private static float CalmMusicSample(float time)
    {
        var noteIndex = Math.Min((int)time, CalmNotes.Length - 1);
        var localTime = time - noteIndex;
        var note = CalmNotes[noteIndex];
        var pluck = MathF.Exp(-localTime * 3.8f);
        var drone = Tone(55f, time) * 0.045f + Tone(82.5f, time) * 0.026f;
        return drone + pluck * (Tone(note, localTime) * 0.11f + Tone(note * 2f, localTime) * 0.038f);
    }

    private static float BattleMusicSample(float time, ref uint noiseState)
    {
        var halfBeat = time - MathF.Floor(time * 2f) * 0.5f;
        var fullBeat = time - MathF.Floor(time);
        var drumEnvelope = MathF.Exp(-halfBeat * 19f);
        var accentEnvelope = MathF.Exp(-fullBeat * 13f);
        var drum = Tone(74f - halfBeat * 55f, halfBeat) * drumEnvelope * 0.25f;
        var strike = NextNoise(ref noiseState) * accentEnvelope * 0.09f;
        var drone = Tone(55f, time) * 0.055f + Tone(82.5f, time) * 0.035f;
        return drum + strike + drone;
    }

    private static float Chirp(float time, float start, float startFrequency, float endFrequency, float length)
    {
        var local = time - start;
        if (local < 0f || local >= length)
        {
            return 0f;
        }

        var progress = local / length;
        var frequency = startFrequency + (endFrequency - startFrequency) * progress;
        var envelope = MathF.Sin(progress * MathF.PI) * MathF.Exp(-progress * 1.4f);
        return Tone(frequency, local) * envelope;
    }

    private static float Impact(float time, float start, float decay)
    {
        var local = time - start;
        return local < 0f ? 0f : MathF.Exp(-local * decay);
    }

    private static float FadeOut(float time, float duration, float fadeLength)
    {
        var remaining = duration - time;
        return remaining >= fadeLength ? 1f : Clamp01(remaining / fadeLength);
    }

    private static float Tone(float frequency, float time) => MathF.Sin(Tau * frequency * time);

    private static float NextNoise(ref uint state)
    {
        state = state * 1_664_525u + 1_013_904_223u;
        return ((state >> 8) * (1f / 8_388_607.5f)) - 1f;
    }

    private static float SoftLimit(float value) => value / (1f + MathF.Abs(value));

    private static void WriteSample(byte[] buffer, int sampleIndex, float value)
    {
        var clamped = Clamp(value, -1f, 1f);
        var sample = (short)MathF.Round(clamped * short.MaxValue);
        var offset = sampleIndex * sizeof(short);
        buffer[offset] = (byte)sample;
        buffer[offset + 1] = (byte)(sample >> 8);
    }

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    private static float Clamp(float value, float minimum, float maximum) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    private enum LoopKind
    {
        Ambient,
        CalmMusic,
        BattleMusic
    }

    private sealed class CueVoicePool : IDisposable
    {
        private readonly SoundEffect _effect;
        private readonly SoundEffectInstance[] _voices;
        private readonly float[] _voiceVolumes;
        private int _nextVoice;
        private bool _disposed;

        public CueVoicePool(SoundEffect effect, int voiceCount)
        {
            _effect = effect;
            _voices = new SoundEffectInstance[voiceCount];
            _voiceVolumes = new float[voiceCount];
            for (var voiceIndex = 0; voiceIndex < voiceCount; voiceIndex++)
            {
                _voices[voiceIndex] = effect.CreateInstance();
            }
        }

        public void Play(float volume, float pitch, float pan, float outputScale)
        {
            var voiceIndex = FindAvailableVoice();
            var voice = _voices[voiceIndex];
            if (voice.State != SoundState.Stopped)
            {
                voice.Stop();
            }

            _voiceVolumes[voiceIndex] = volume;
            voice.Volume = Clamp01(volume * outputScale);
            voice.Pitch = pitch;
            voice.Pan = pan;
            voice.Play();
            _nextVoice = (voiceIndex + 1) % _voices.Length;
        }

        public void RefreshVolumes(float outputScale)
        {
            for (var voiceIndex = 0; voiceIndex < _voices.Length; voiceIndex++)
            {
                _voices[voiceIndex].Volume = Clamp01(_voiceVolumes[voiceIndex] * outputScale);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var voiceIndex = 0; voiceIndex < _voices.Length; voiceIndex++)
            {
                _voices[voiceIndex].Dispose();
            }

            _effect.Dispose();
        }

        private int FindAvailableVoice()
        {
            for (var offset = 0; offset < _voices.Length; offset++)
            {
                var voiceIndex = (_nextVoice + offset) % _voices.Length;
                if (_voices[voiceIndex].State == SoundState.Stopped)
                {
                    return voiceIndex;
                }
            }

            return _nextVoice;
        }
    }
}
