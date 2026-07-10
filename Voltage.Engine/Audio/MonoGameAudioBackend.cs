using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// <see cref="IAudioHandle"/> backed by a MonoGame <see cref="SoundEffectInstance"/>.
	/// </summary>
	internal sealed class MonoGameAudioHandle : IAudioHandle
	{
		private readonly SoundEffectInstance _instance;

		public MonoGameAudioHandle(SoundEffectInstance instance)
		{
			_instance = instance;
		}

		public float Volume
		{
			get => _instance.Volume;
			set => _instance.Volume = MathHelper.Clamp(value, 0f, 1f);
		}

		public float Pitch
		{
			get => _instance.Pitch;
			set => _instance.Pitch = MathHelper.Clamp(value, -1f, 1f);
		}

		public float Pan
		{
			get => _instance.Pan;
			set => _instance.Pan = MathHelper.Clamp(value, -1f, 1f);
		}

		public bool IsLooped
		{
			// MonoGame throws if IsLooped is changed after the instance has started playing,
			// so only set it while the instance is stopped (CreateHandle sets it before Play).
			get => _instance.IsLooped;
			set { if (_instance.State == SoundState.Stopped) _instance.IsLooped = value; }
		}

		public AudioPlayState State => _instance.State switch
		{
			SoundState.Playing => AudioPlayState.Playing,
			SoundState.Paused => AudioPlayState.Paused,
			_ => AudioPlayState.Stopped,
		};

		public bool IsDisposed => _instance.IsDisposed;

		public void Play() => _instance.Play();
		public void Pause() => _instance.Pause();
		public void Resume() => _instance.Resume();
		public void Stop() => _instance.Stop();

		public void Dispose()
		{
			if (!_instance.IsDisposed)
				_instance.Dispose();
		}
	}

	/// <summary>
	/// Default, always-available audio backend built on MonoGame's <see cref="SoundEffect"/> API.
	/// Cross-platform "for free": MonoGame maps this to OpenAL on desktop and to each console's native
	/// audio backend. Has no mixing/bus/DSP concepts of its own — those live in <see cref="AudioMixer"/>
	/// and <see cref="AudioManager"/> above it.
	/// </summary>
	public sealed class MonoGameAudioBackend : IAudioBackend
	{
		public void Init() { }

		public void Shutdown() { }

		public IAudioHandle CreateHandle(SoundEffect clip, bool looped)
		{
			var instance = clip.CreateInstance();
			instance.IsLooped = looped;
			return new MonoGameAudioHandle(instance);
		}

		public bool PlayOneShot(SoundEffect clip, float volume, float pitch, float pan)
		{
			return clip.Play(
				MathHelper.Clamp(volume, 0f, 1f),
				MathHelper.Clamp(pitch, -1f, 1f),
				MathHelper.Clamp(pan, -1f, 1f));
		}
	}
}
