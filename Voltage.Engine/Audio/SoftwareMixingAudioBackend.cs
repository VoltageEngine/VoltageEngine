using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// Software-mixer profiling counters (via <see cref="AudioManager.TryGetSoftwareAudioStats"/>). The key
	/// number is <see cref="LoadPercent"/>; sustained above ~50% risks <see cref="Underruns"/>.
	/// </summary>
	public struct AudioProfilerStats
	{
		/// <summary>Smoothed time to mix one callback buffer, in milliseconds.</summary>
		public float MixAvgMs;
		/// <summary>Recent peak mix time, in milliseconds (slowly decays).</summary>
		public float MixPeakMs;
		/// <summary>Real-time budget for one callback buffer, in milliseconds.</summary>
		public float BudgetMs;
		/// <summary>Mix time as a percentage of the budget.</summary>
		public float LoadPercent;
		/// <summary>Cumulative callback overruns (mix exceeded the budget) — each risks an audible glitch.</summary>
		public int Underruns;
		/// <summary>Software voices currently mixing (playing, non-disposed).</summary>
		public int ActiveVoices;
	}

	/// <summary>
	/// Software-mixing backend: decodes clips to PCM and mixes all voices in managed code, fed straight to
	/// <b>SDL's real-time audio thread</b> via <see cref="SdlAudioDevice"/>. SDL's callback is scheduled
	/// independently of the game loop, so no frame hitch or GC pause can starve it, letting a tiny buffer give
	/// tight latency. This is the DSP backend (per-voice low-pass + shared reverb); desktop-focused, falls
	/// back to <see cref="MonoGameAudioBackend"/> where unsupported.
	/// </summary>
	// TODO(mobile/console): SoftwareMixer/DSP is platform-agnostic — only this output driver is desktop/SDL-specific.
	// Port by adding an IAudioBackend that pumps SoftwareMixer.MixInto from a native real-time callback
	// (Android AAudio/Oboe, iOS CoreAudio/AudioUnit, console native, or an FMOD backend). Not a rewrite.
	public sealed class SoftwareMixingAudioBackend : IAudioBackend
	{
		private const int PreferredSampleRate = 44100;
		private const int Channels = 2;
		// ~5.8 ms per callback at 44.1 kHz; SDL double-buffers, so end-to-end latency is ~12 ms — tight enough
		// for combat SFX. Tunable if a slower machine reports callback overruns.
		private const int BufferFrames = 256;

		private SoftwareMixer _mixer;
		private SdlAudioDevice _device;
		private bool _deviceAttempted;
		private readonly Dictionary<Guid, AudioClip> _clipCache = new();

		private float[] _mixFloat;
		private short[] _mixShort;

		// Profiling (alloc-free): timestamp-based mix timing + overrun counter, updated on the audio thread.
		private static readonly double MsPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		private double _mixAvgMs;
		private double _mixPeakMs;
		private int _underruns;

		/// <summary>The pure mixer — exposed for reverb control, tests and diagnostics.</summary>
		public SoftwareMixer Mixer => _mixer;

		/// <summary>Whether this platform can run the software backend (desktop only).</summary>
		public static bool IsSupported =>
			OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

		public void Init()
		{
			// Don't open the SDL device yet: at construction time SDL audio isn't initialized. The device
			// opens lazily on the first sound played.
			_mixer = new SoftwareMixer(PreferredSampleRate, Channels);
		}

		// Opens the SDL audio device on first use (by which point SDL is up). Idempotent; called from the
		// game thread before any voice is created.
		private void EnsureDevice()
		{
			if (_deviceAttempted)
				return;
			_deviceAttempted = true;

			var device = new SdlAudioDevice();
			if (!device.Open(PreferredSampleRate, Channels, BufferFrames, FillAudio))
			{
				device.Dispose();
				Debug.Warn("[SoftwareAudio] SDL audio device could not be opened — no sound will play. " +
					"Set AudioManager.PreferSoftwareBackend=false to use the MonoGame backend instead.");
				return;
			}

			// Match the mixer to the device's actual format (SDL may substitute a rate / buffer size).
			if (device.SampleRate != _mixer.OutputSampleRate || device.Channels != _mixer.OutputChannels)
				_mixer = new SoftwareMixer(device.SampleRate, device.Channels);

			_mixFloat = new float[device.BufferFrames * device.Channels];
			_mixShort = new short[device.BufferFrames * device.Channels];
			_device = device;
			_device.Start();

			Debug.Log($"[SoftwareAudio] SDL audio: {device.SampleRate}Hz {device.Channels}ch, " +
				$"{device.BufferFrames}-frame buffer (~{device.BufferFrames * 1000.0 / device.SampleRate:0.0} ms).");
		}

		public void Shutdown()
		{
			_device?.Dispose();
			_device = null;
		}

		public AudioClip LoadClip(AssetReference reference)
		{
			if (!reference.IsValid)
				return null;

			if (reference.AssetGuid != Guid.Empty && _clipCache.TryGetValue(reference.AssetGuid, out var cached))
				return cached;

			var path = reference.ResolvePath();
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				Debug.Warn($"[SoftwareAudio] cannot resolve a raw source file for {reference} — the software backend decodes .wav/.ogg/.mp3 from disk.");
				return null;
			}

			try
			{
				AudioClip clip = AudioDecoders.DecodeToPcm(path);
				if (reference.AssetGuid != Guid.Empty)
					_clipCache[reference.AssetGuid] = clip;
				return clip;
			}
			catch (Exception ex)
			{
				Debug.Warn($"[SoftwareAudio] decode failed for '{path}': {ex.Message}");
				return null;
			}
		}

		public IAudioHandle CreateHandle(AudioClip clip, bool looped)
		{
			EnsureDevice();
			return _mixer.CreateHandle(clip, looped);
		}

		public bool PlayOneShot(AudioClip clip, float volume, float pitch, float pan)
		{
			EnsureDevice();
			if (_mixer.CreateHandle(clip, looped: false) is not SoftwareAudioHandle handle)
				return false;

			handle.AutoReleaseOnStop = true; // no AudioManager voice owns a fire-and-forget one-shot
			handle.Volume = volume;
			handle.Pitch = pitch;
			handle.Pan = pan;
			handle.Play();
			return true;
		}

		// Invoked on SDL's real-time audio thread: mix, convert to interleaved 16-bit PCM, write to the
		// native stream.
		private void FillAudio(IntPtr stream, int lenBytes)
		{
			var mixer = _mixer;
			int channels = _device?.Channels ?? Channels;
			int frames = lenBytes / (channels * 2);
			int n = frames * channels;
			if (n <= 0)
				return;

			if (_mixShort == null || _mixShort.Length < n)
			{
				_mixFloat = new float[n];
				_mixShort = new short[n];
			}

			if (mixer == null)
			{
				Array.Clear(_mixShort, 0, n);
				Marshal.Copy(_mixShort, 0, stream, n);
				return;
			}

			long start = System.Diagnostics.Stopwatch.GetTimestamp();
			mixer.MixInto(_mixFloat, frames);
			double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * MsPerTick;
			_mixAvgMs = _mixAvgMs <= 0.0 ? ms : _mixAvgMs * 0.95 + ms * 0.05; // EMA
			_mixPeakMs = ms > _mixPeakMs ? ms : _mixPeakMs * 0.9995;          // decaying peak
			double budget = frames / (double)(_device?.SampleRate ?? PreferredSampleRate) * 1000.0;
			if (ms > budget)
				_underruns++;

			for (int i = 0; i < n; i++)
				_mixShort[i] = (short)(_mixFloat[i] * 32767f); // mixer already clamped to [-1,1]

			Marshal.Copy(_mixShort, 0, stream, n);
		}

		private float BudgetMs => (float)((_device?.BufferFrames ?? BufferFrames) /
			(double)(_device?.SampleRate ?? PreferredSampleRate) * 1000.0);

		/// <summary>Current profiling snapshot (mix time, load %, peak, active voices, overruns).</summary>
		public AudioProfilerStats GetStats()
		{
			float budget = BudgetMs;
			float avg = (float)_mixAvgMs;
			return new AudioProfilerStats
			{
				MixAvgMs = avg,
				MixPeakMs = (float)_mixPeakMs,
				BudgetMs = budget,
				LoadPercent = budget > 0f ? avg / budget * 100f : 0f,
				Underruns = _underruns,
				ActiveVoices = _mixer?.ActiveVoices ?? 0,
			};
		}
	}
}
