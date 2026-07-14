using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voltage.Audio
{
	/// <summary>
	/// Pure software mixer: sums <see cref="SoftwareAudioHandle"/> voices into an interleaved float buffer,
	/// resampling each for pitch/sample-rate and applying volume, pan, low-pass and reverb send. No device
	/// dependency — <see cref="SoftwareMixingAudioBackend"/> drives it and it is unit-testable via <see cref="MixInto"/>.
	/// </summary>
	public sealed class SoftwareMixer
	{
		private readonly int _outRate;
		private readonly int _outChannels;
		private readonly List<SoftwareAudioHandle> _handles = new();
		private readonly object _lock = new();
		private readonly Reverb _reverb;
		private float[] _reverbSend = System.Array.Empty<float>();
		private SoftwareAudioHandle[] _scratch = new SoftwareAudioHandle[16];
		private static readonly Predicate<SoftwareAudioHandle> IsDisposedHandle = h => h.IsDisposed;

		public SoftwareMixer(int outputSampleRate = 44100, int outputChannels = 2)
		{
			_outRate = outputSampleRate < 1 ? 44100 : outputSampleRate;
			_outChannels = outputChannels == 1 ? 1 : 2;
			_reverb = new Reverb(_outRate);
		}

		public int OutputSampleRate => _outRate;
		public int OutputChannels => _outChannels;
		public bool ReverbEnabled => _reverb.Enabled;

		/// <summary>Number of voices currently mixing (playing, non-disposed) — for profiling.</summary>
		public int ActiveVoices
		{
			get
			{
				lock (_lock)
				{
					int n = 0;
					for (int i = 0; i < _handles.Count; i++)
						if (!_handles[i].IsDisposed && _handles[i].State == AudioPlayState.Playing)
							n++;
					return n;
				}
			}
		}

		/// <summary>Sets the global reverb room size / damping / wet level and enables it.</summary>
		public void SetReverb(float roomSize, float damping, float wet, bool enabled = true)
		{
			_reverb.SetParams(roomSize, damping);
			_reverb.Wet = wet < 0f ? 0f : (wet > 1f ? 1f : wet);
			_reverb.Enabled = enabled;
		}

		/// <summary>Enables/disables the global reverb; disabling flushes its tail.</summary>
		public void SetReverbEnabled(bool enabled)
		{
			_reverb.Enabled = enabled;
			if (!enabled)
				_reverb.Clear();
		}

		/// <summary>Creates a voice for <paramref name="clip"/> (must be a PCM clip) and registers it for mixing.</summary>
		public IAudioHandle CreateHandle(AudioClip clip, bool looped)
		{
			if (clip is not PcmAudioClip pcm || pcm.FrameCount <= 0)
				return null;

			var handle = new SoftwareAudioHandle(pcm) { IsLooped = looped };
			lock (_lock)
				_handles.Add(handle);
			return handle;
		}

		/// <summary>
		/// Mixes all active voices into <paramref name="output"/> (interleaved, length ≥
		/// <paramref name="frameCount"/> * output channels), clamped to [-1, 1].
		/// </summary>
		public void MixInto(float[] output, int frameCount)
		{
			int outLen = frameCount * _outChannels;
			Array.Clear(output, 0, outLen);

			bool reverbOn = _reverb.Enabled && _reverb.Wet > 0f;
			if (reverbOn)
			{
				if (_reverbSend.Length < frameCount)
					_reverbSend = new float[frameCount];
				Array.Clear(_reverbSend, 0, frameCount);
			}
			float[] sendBuffer = reverbOn ? _reverbSend : null;

			// Copy the live handles into a reusable scratch buffer under a short lock, then mix without holding
			// it — no per-callback allocation on the audio thread.
			int count;
			lock (_lock)
			{
				_handles.RemoveAll(IsDisposedHandle);
				count = _handles.Count;
				if (_scratch.Length < count)
					_scratch = new SoftwareAudioHandle[Math.Max(count, _scratch.Length * 2)];
				_handles.CopyTo(_scratch, 0);
			}

			for (int i = 0; i < count; i++)
				_scratch[i].MixInto(output, sendBuffer, frameCount, _outRate, _outChannels);

			if (count < _scratch.Length)
				Array.Clear(_scratch, count, _scratch.Length - count);

			if (reverbOn)
			{
				float wet = _reverb.Wet;
				for (int f = 0; f < frameCount; f++)
				{
					float w = _reverb.Process(_reverbSend[f]) * wet;
					int o = f * _outChannels;
					output[o] += w;
					if (_outChannels > 1)
						output[o + 1] += w;
				}
			}

			ClampBuffer(output, outLen);
		}

		// SIMD-vectorized where hardware-accelerated. (The mix/resample loop resists vectorization due to the
		// per-sample low-pass dependency, but the clamp is trivial.)
		private static void ClampBuffer(float[] output, int len)
		{
			int i = 0;
			if (Vector.IsHardwareAccelerated && len >= Vector<float>.Count)
			{
				int width = Vector<float>.Count;
				var one = new Vector<float>(1f);
				var negOne = new Vector<float>(-1f);
				for (; i <= len - width; i += width)
				{
					var v = new Vector<float>(output, i);
					v = Vector.Min(Vector.Max(v, negOne), one);
					v.CopyTo(output, i);
				}
			}
			for (; i < len; i++)
			{
				float v = output[i];
				if (v > 1f) output[i] = 1f;
				else if (v < -1f) output[i] = -1f;
			}
		}
	}

	/// <summary>
	/// A software-mixed voice: a playback cursor into a <see cref="PcmAudioClip"/> with volume/pitch/pan set
	/// by the <see cref="AudioManager"/>. Implements <see cref="IAudioHandle"/> so the manager drives it
	/// exactly like the MonoGame handle.
	/// </summary>
	internal sealed class SoftwareAudioHandle : IAudioHandle
	{
		private readonly PcmAudioClip _clip;
		private double _pos; // playback cursor in source frames
		private AudioPlayState _state = AudioPlayState.Stopped;
		private bool _disposed;

		private float _volume = 1f;
		private float _pitch;
		private float _pan;

		// One-pole low-pass state (per output channel), persisted across buffers.
		private float _lpL, _lpR;

		public SoftwareAudioHandle(PcmAudioClip clip) => _clip = clip;

		public float Volume { get => _volume; set => _volume = Clamp(value, 0f, 1f); }
		public float Pitch { get => _pitch; set => _pitch = Clamp(value, -1f, 1f); }
		public float Pan { get => _pan; set => _pan = Clamp(value, -1f, 1f); }
		public bool IsLooped { get; set; }
		public float LowPassCutoffHz { get; set; }
		public float ReverbSend { get; set; }
		public AudioPlayState State => _state;
		public bool IsDisposed => _disposed;

		// Fire-and-forget one-shots (PlayOneShot) self-dispose when finished since no manager voice owns them;
		// controlled voices leave this false and are disposed by the manager.
		internal bool AutoReleaseOnStop;

		public void Play() { if (_state == AudioPlayState.Stopped) _pos = 0; _state = AudioPlayState.Playing; }
		public void Pause() { if (_state == AudioPlayState.Playing) _state = AudioPlayState.Paused; }
		public void Resume() { if (_state == AudioPlayState.Paused) _state = AudioPlayState.Playing; }
		public void Stop() { _state = AudioPlayState.Stopped; _pos = 0; }
		public void Dispose() { _disposed = true; _state = AudioPlayState.Stopped; }

		// Accumulates this voice into the interleaved output. Snapshots volume/pan/pitch once so they stay
		// consistent across the buffer.
		internal void MixInto(float[] output, float[] reverbSend, int frameCount, int outRate, int outChannels)
		{
			if (_state != AudioPlayState.Playing || _disposed)
				return;

			int frames = _clip.FrameCount;
			if (frames <= 0)
				return;

			float[] src = _clip.Samples;
			int ch = _clip.Channels;
			bool looped = IsLooped;

			double step = (_clip.SampleRate / (double)outRate) * Math.Pow(2.0, _pitch);
			float vol = _volume;
			// Linear pan balance (matches the MonoGame backend's Pan behavior).
			float leftGain = _pan <= 0f ? 1f : 1f - _pan;
			float rightGain = _pan >= 0f ? 1f : 1f + _pan;

			// One-pole low-pass (occlusion/muffle). 0 or ≥ Nyquist = bypass.
			float nyquist = outRate * 0.5f;
			bool lowPass = LowPassCutoffHz > 0f && LowPassCutoffHz < nyquist;
			float lpAlpha = lowPass ? 1f - (float)Math.Exp(-2.0 * Math.PI * LowPassCutoffHz / outRate) : 1f;

			float send = ReverbSend;
			bool doSend = reverbSend != null && send > 0f;

			for (int f = 0; f < frameCount; f++)
			{
				if (!looped && _pos >= frames - 1)
				{
					_state = AudioPlayState.Stopped;
					_pos = 0;
					if (AutoReleaseOnStop)
						_disposed = true;
					return;
				}

				int i0 = (int)_pos;
				double frac = _pos - i0;
				int i1 = i0 + 1;

				if (looped)
				{
					if (i0 >= frames) i0 -= frames;
					if (i1 >= frames) i1 -= frames;
				}
				else if (i1 >= frames)
				{
					i1 = frames - 1;
				}

				float l, r;
				if (ch == 1)
				{
					float m = src[i0] + (float)((src[i1] - src[i0]) * frac);
					l = r = m;
				}
				else
				{
					int a = i0 * 2, b = i1 * 2;
					l = src[a] + (float)((src[b] - src[a]) * frac);
					r = src[a + 1] + (float)((src[b + 1] - src[a + 1]) * frac);
				}

				if (lowPass)
				{
					_lpL += lpAlpha * (l - _lpL); l = _lpL;
					_lpR += lpAlpha * (r - _lpR); r = _lpR;
				}

				int o = f * outChannels;
				output[o] += l * vol * leftGain;
				if (outChannels > 1)
					output[o + 1] += r * vol * rightGain;

				if (doSend)
					reverbSend[f] += (l + r) * 0.5f * vol * send;

				_pos += step;
				if (looped && _pos >= frames)
					_pos -= frames;
			}
		}

		private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
	}
}
