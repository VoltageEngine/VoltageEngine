using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Voltage.Audio;
using Voltage.Serialization;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class AudioVoiceTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private readonly FakeBackend _backend = new();
    private AudioManager _manager;
    private object _previousCore;

    [SetUp]
    public void SetUp()
    {
        _previousCore = typeof(Core).GetField("_instance", Fields)!.GetValue(null);
        var core = (Core)RuntimeHelpers.GetUninitializedObject(typeof(Core));
        GC.SuppressFinalize(core);
        _manager = new AudioManager(_backend);
        typeof(Core).GetField("_audioManager", Fields)!.SetValue(core, _manager);
        typeof(Core).GetField("_instance", Fields)!.SetValue(null, core);
    }

    [TearDown]
    public void TearDown()
    {
        try { _manager.Shutdown(); }
        finally { typeof(Core).GetField("_instance", Fields)!.SetValue(null, _previousCore); }
    }

    private (AudioSourceComponent Source, AudioVoice Voice, FakeHandle Handle) Play(bool loop)
    {
        var settings = AudioPlaySettings.Default;
        settings.Loop = loop;
        var voice = _manager.PlayControlled(AudioClip.FromPcm(new float[32], 1, 48000), settings);
        var source = new AudioSourceComponent { PlayOnStart = false, Volume = 0.4f };
        typeof(AudioSourceComponent).GetField("_voice", Fields)!.SetValue(source, voice);
        return (source, voice, _backend.LastHandle);
    }

    private static AudioVoice OwnedVoice(AudioSourceComponent source) =>
        (AudioVoice)typeof(AudioSourceComponent).GetField("_voice", Fields)!.GetValue(source);

    [TestCase(false), TestCase(true)]
    public void Paused_sources_keep_their_voice_and_resume_without_recreating_it(bool loop)
    {
        var (source, voice, handle) = Play(loop);
        for (int i = 0; i < 20; i++)
        {
            voice.Pause();
            source.Update();
            _manager.Update();
            Assert.That(voice.IsPlaying, Is.False);
            Assert.That(voice.IsAlive, Is.True);
            Assert.That(OwnedVoice(source), Is.SameAs(voice));
            Assert.That(handle.Volume, Is.EqualTo(0.4f));
            voice.Resume();
            Assert.That(voice.IsPlaying, Is.True);
            Assert.That(_backend.LastHandle, Is.SameAs(handle));
        }
    }

    [TestCase(false), TestCase(true)]
    public void Stopping_a_paused_source_releases_and_disposes_its_voice(bool loop)
    {
        var (source, voice, handle) = Play(loop);
        voice.Pause();
        source.Update();
        source.Stop();
        _manager.Update();
        Assert.That(OwnedVoice(source), Is.Null);
        Assert.That(handle.IsDisposed, Is.True);
        Assert.That(handle.DisposeCount, Is.EqualTo(1));
        Assert.That(voice.IsAlive, Is.False);
    }

    [Test]
    public void Global_stop_releases_a_paused_source_on_its_next_update()
    {
        var (source, voice, handle) = Play(true);
        voice.Pause();
        source.Update();
        _manager.StopAll();
        source.Update();
        Assert.That(OwnedVoice(source), Is.Null);
        Assert.That(handle.DisposeCount, Is.EqualTo(1));
        Assert.That(voice.IsAlive, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void Finished_or_disposed_handles_are_still_released(bool disposed)
    {
        var (source, voice, handle) = Play(false);
        if (disposed) handle.Dispose();
        else handle.Stop();
        source.Update();
        _manager.Update();
        Assert.That(voice.IsAlive, Is.False);
        Assert.That(OwnedVoice(source), Is.Null);
        Assert.That(handle.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Virtual_loops_remain_alive_without_a_hardware_handle()
    {
        var voice = new AudioVoice();
        typeof(AudioVoice).GetField("IsVirtual", Fields)!.SetValue(voice, true);
        Assert.That(voice.IsAlive, Is.True);
        Assert.That(voice.IsPlaying, Is.False);
    }

    private sealed class FakeBackend : IAudioBackend
    {
        public FakeHandle LastHandle;
        public void Init() { }
        public void Shutdown() { }
        public AudioClip LoadClip(AssetReference reference) => null;
        public IAudioHandle CreateHandle(AudioClip clip, bool looped) => LastHandle = new FakeHandle { IsLooped = looped };
        public bool PlayOneShot(AudioClip clip, float volume, float pitch, float pan) => false;
    }

    private sealed class FakeHandle : IAudioHandle
    {
        public float Volume { get; set; }
        public float Pitch { get; set; }
        public float Pan { get; set; }
        public bool IsLooped { get; set; }
        public float LowPassCutoffHz { get; set; }
        public float ReverbSend { get; set; }
        private AudioPlayState _state;
        public AudioPlayState State => !IsDisposed ? _state : throw new ObjectDisposedException(nameof(FakeHandle));
        public bool IsDisposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Play() => _state = AudioPlayState.Playing;
        public void Pause() => _state = AudioPlayState.Paused;
        public void Resume() => _state = AudioPlayState.Playing;
        public void Stop() => _state = AudioPlayState.Stopped;
        public void Dispose() { IsDisposed = true; DisposeCount++; }
    }
}
