using System;
using System.Runtime.InteropServices;

namespace Voltage.Audio
{
	/// <summary>
	/// Minimal SDL2 audio-callback output for the software mixer. SDL runs its own real-time audio thread and
	/// invokes our callback whenever it needs samples — decoupled from the game frame rate, so a frame hitch or
	/// GC pause can't starve it. Uses the <c>"SDL2.dll"</c> P/Invoke name that <see cref="SdlNative"/> remaps
	/// to the correct native library per platform.
	/// </summary>
	internal sealed class SdlAudioDevice : IDisposable
	{
		private const string Lib = "SDL2.dll";
		private const uint SDL_INIT_AUDIO = 0x00000010u;
		private const ushort AUDIO_S16LSB = 0x8010;
		private const int SDL_AUDIO_ALLOW_FREQUENCY_CHANGE = 0x00000001;
		private const int SDL_AUDIO_ALLOW_SAMPLES_CHANGE = 0x00000008;

		[StructLayout(LayoutKind.Sequential)]
		private struct SDL_AudioSpec
		{
			public int freq;
			public ushort format;
			public byte channels;
			public byte silence;
			public ushort samples;
			public ushort padding;
			public uint size;
			public IntPtr callback;
			public IntPtr userdata;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SdlAudioCallback(IntPtr userdata, IntPtr stream, int len);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		private static extern int SDL_InitSubSystem(uint flags);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		private static extern uint SDL_OpenAudioDevice(IntPtr device, int iscapture,
			ref SDL_AudioSpec desired, out SDL_AudioSpec obtained, int allowed_changes);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		private static extern void SDL_PauseAudioDevice(uint dev, int pause_on);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		private static extern void SDL_CloseAudioDevice(uint dev);

		private uint _dev;
		private SdlAudioCallback _callback; // held so the delegate isn't GC'd while native code holds the pointer
		private Action<IntPtr, int> _fill;

		public int SampleRate { get; private set; }
		public int Channels { get; private set; }
		public int BufferFrames { get; private set; }
		public bool IsOpen => _dev != 0;

		/// <summary>
		/// Opens the default output device. <paramref name="fill"/> is invoked on SDL's audio thread with the
		/// native stream pointer and byte length, and must fill exactly that many bytes. Returns false if audio
		/// can't be initialized. Call <see cref="Start"/> to begin.
		/// </summary>
		public bool Open(int freq, int channels, int bufferFrames, Action<IntPtr, int> fill)
		{
			_fill = fill;
			_callback = OnAudio;

			try
			{
				// SDL is already initialized by MonoGame for video; this just adds the audio subsystem.
				if (SDL_InitSubSystem(SDL_INIT_AUDIO) != 0)
					return false;

				var desired = new SDL_AudioSpec
				{
					freq = freq,
					format = AUDIO_S16LSB,
					channels = (byte)channels,
					samples = (ushort)bufferFrames,
					callback = Marshal.GetFunctionPointerForDelegate(_callback),
					userdata = IntPtr.Zero,
				};

				_dev = SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, out var obtained,
					SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL_AUDIO_ALLOW_SAMPLES_CHANGE);
				if (_dev == 0)
					return false;

				SampleRate = obtained.freq;
				Channels = obtained.channels;
				BufferFrames = obtained.samples;
				return true;
			}
			catch (DllNotFoundException) { return false; }
			catch (EntryPointNotFoundException) { return false; }
			catch (BadImageFormatException) { return false; }
		}

		/// <summary>Begins playback (the device opens paused).</summary>
		public void Start()
		{
			if (_dev != 0)
				SDL_PauseAudioDevice(_dev, 0);
		}

		private void OnAudio(IntPtr userdata, IntPtr stream, int len)
		{
			// A managed exception must never propagate across the native boundary.
			try { _fill?.Invoke(stream, len); }
			catch { }
		}

		public void Dispose()
		{
			if (_dev != 0)
			{
				try
				{
					SDL_PauseAudioDevice(_dev, 1);
					SDL_CloseAudioDevice(_dev);
				}
				catch { }
				_dev = 0;
			}
			_callback = null;
			_fill = null;
		}
	}
}
