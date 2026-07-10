using System;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Decodes compressed audio to a MonoGame <see cref="SoundEffect"/> with pure-managed decoders
	/// (NVorbis for <c>.ogg</c>, NLayer for <c>.mp3</c>), so they load on the same path as <c>.wav</c>
	/// and flow through the mixer. No native binaries or content-pipeline step — AOT/trim-safe and
	/// portable everywhere MonoGame targets.
	/// </summary>
	public static class AudioDecoders
	{
		private const int ChunkSamples = 16384;

		/// <summary>Decodes an Ogg Vorbis file to a <see cref="SoundEffect"/>.</summary>
		public static SoundEffect DecodeOgg(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new NVorbis.VorbisReader(stream, closeOnDispose: false);

			int channels = reader.Channels;
			int sampleRate = reader.SampleRate;

			// TotalSamples is per-channel; interleaved length is TotalSamples * channels.
			long interleavedLong = reader.TotalSamples * channels;
			int total = interleavedLong > 0 && interleavedLong <= int.MaxValue
				? (int)interleavedLong
				: 0;

			float[] samples;
			int count;
			if (total > 0)
			{
				samples = new float[total];
				int read = 0;
				int n;
				while (read < total && (n = reader.ReadSamples(samples, read, total - read)) > 0)
					read += n;
				count = read;
			}
			else
			{
				(samples, count) = ReadAll(reader.ReadSamples);
			}

			return FloatSamplesToSoundEffect(samples, count, sampleRate, channels, path);
		}

		/// <summary>Decodes an MP3 file to a <see cref="SoundEffect"/>.</summary>
		public static SoundEffect DecodeMp3(string path)
		{
			using var stream = File.OpenRead(path);
			using var mpeg = new NLayer.MpegFile(stream);

			int channels = mpeg.Channels;
			int sampleRate = mpeg.SampleRate;

			var (samples, count) = ReadAll(mpeg.ReadSamples);
			return FloatSamplesToSoundEffect(samples, count, sampleRate, channels, path);
		}

		// Reads all interleaved float samples via a decoder's ReadSamples(buffer, offset, count) delegate.
		private static (float[] buffer, int count) ReadAll(Func<float[], int, int, int> read)
		{
			var buffer = new float[ChunkSamples * 4];
			var chunk = new float[ChunkSamples];
			int total = 0;
			int n;
			while ((n = read(chunk, 0, chunk.Length)) > 0)
			{
				if (total + n > buffer.Length)
				{
					int newSize = buffer.Length * 2;
					while (newSize < total + n)
						newSize *= 2;
					Array.Resize(ref buffer, newSize);
				}
				Array.Copy(chunk, 0, buffer, total, n);
				total += n;
			}
			return (buffer, total);
		}

		// Converts interleaved float [-1,1] samples to 16-bit PCM and builds a SoundEffect.
		private static SoundEffect FloatSamplesToSoundEffect(float[] samples, int count, int sampleRate, int channels, string path)
		{
			if (channels != 1 && channels != 2)
				throw new NotSupportedException(
					$"Audio file '{Path.GetFileName(path)}' has {channels} channels; " +
					"only mono and stereo are supported by SoundEffect.");

			var pcm = new byte[count * 2];
			int bi = 0;
			for (int i = 0; i < count; i++)
			{
				float f = samples[i];
				if (f > 1f) f = 1f;
				else if (f < -1f) f = -1f;

				short s = (short)(f * short.MaxValue);
				pcm[bi++] = (byte)(s & 0xFF);
				pcm[bi++] = (byte)((s >> 8) & 0xFF);
			}

			var audioChannels = channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono;
			return new SoundEffect(pcm, sampleRate, audioChannels);
		}
	}
}
