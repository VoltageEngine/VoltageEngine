using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Voltage.Serialization;

namespace Voltage.Audio
{
	// IAudioHandle backed by a MonoGame SoundEffectInstance.
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
			// MonoGame throws if IsLooped changes after playback starts, so only set it while stopped.
			get => _instance.IsLooped;
			set { if (_instance.State == SoundState.Stopped) _instance.IsLooped = value; }
		}

		// Stored but ignored — SoundEffectInstance has no low-pass/reverb.
		public float LowPassCutoffHz { get; set; }
		public float ReverbSend { get; set; }

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
	/// Default audio backend built on MonoGame's <see cref="SoundEffect"/> API (OpenAL on desktop, native
	/// audio on consoles). Mixing/bus/DSP lives above it in <see cref="AudioMixer"/> and <see cref="AudioManager"/>.
	/// </summary>
	public sealed class MonoGameAudioBackend : IAudioBackend
	{
		public void Init() { }

		public void Shutdown() { }

		public AudioClip LoadClip(AssetReference reference)
		{
			// Loads through the scene's content pipeline; null scene / unresolved reference degrades to null.
			if (!reference.IsValid || Core.Scene == null)
				return null;
			return AudioClip.FromSoundEffect(Core.Scene.LoadAsset<SoundEffect>(reference));
		}

		public IAudioHandle CreateHandle(AudioClip clip, bool looped)
		{
			if (clip is not SoundEffectAudioClip seClip || seClip.Sound == null)
				return null;

			var instance = seClip.Sound.CreateInstance();
			instance.IsLooped = looped;
			return new MonoGameAudioHandle(instance);
		}

		public bool PlayOneShot(AudioClip clip, float volume, float pitch, float pan)
		{
			if (clip is not SoundEffectAudioClip seClip || seClip.Sound == null)
				return false;

			return seClip.Sound.Play(
				MathHelper.Clamp(volume, 0f, 1f),
				MathHelper.Clamp(pitch, -1f, 1f),
				MathHelper.Clamp(pan, -1f, 1f));
		}
	}
}
