using System;

namespace Voltage.Audio
{
	/// <summary>
	/// The software-mixing backend's clip representation: fully-decoded interleaved PCM float samples in
	/// [-1, 1], which the <see cref="SoftwareMixer"/> reads directly (resampling for pitch/sample-rate).
	/// Create one via <see cref="AudioClip.FromPcm"/>; decode a file with <see cref="AudioDecoders.DecodeToPcm"/>.
	/// </summary>
	internal sealed class PcmAudioClip : AudioClip
	{
		/// <summary>Interleaved samples in [-1, 1]; length == <see cref="FrameCount"/> * <see cref="Channels"/>.</summary>
		public readonly float[] Samples;

		/// <summary>1 (mono) or 2 (stereo).</summary>
		public readonly int Channels;

		public readonly int SampleRate;

		/// <summary>Number of sample frames (per-channel length).</summary>
		public readonly int FrameCount;

		public PcmAudioClip(float[] samples, int channels, int sampleRate)
		{
			Samples = samples ?? Array.Empty<float>();
			Channels = channels < 1 ? 1 : channels;
			SampleRate = sampleRate < 1 ? 44100 : sampleRate;
			FrameCount = Samples.Length / Channels;
		}
	}
}
