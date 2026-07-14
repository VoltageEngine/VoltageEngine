using System;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Decodes compressed audio with pure-managed decoders (NVorbis for <c>.ogg</c>, NLayer for <c>.mp3</c>,
	/// a built-in RIFF/WAVE reader for <c>.wav</c>). <see cref="DecodeOgg"/>/<see cref="DecodeMp3"/> build a
	/// MonoGame <see cref="SoundEffect"/>; <see cref="DecodeToPcm"/> returns interleaved float PCM. No native
	/// binaries or content-pipeline step — AOT/trim-safe.
	/// </summary>
	public static class AudioDecoders
	{
		private const int ChunkSamples = 16384;

		#region SoundEffect output (MonoGame backend)

		/// <summary>Decodes an Ogg Vorbis file to a <see cref="SoundEffect"/>.</summary>
		public static SoundEffect DecodeOgg(string path)
		{
			var (samples, count, sampleRate, channels) = ReadOggFloats(path);
			return FloatSamplesToSoundEffect(samples, count, sampleRate, channels, path);
		}

		/// <summary>Decodes an MP3 file to a <see cref="SoundEffect"/>.</summary>
		public static SoundEffect DecodeMp3(string path)
		{
			var (samples, count, sampleRate, channels) = ReadMp3Floats(path);
			return FloatSamplesToSoundEffect(samples, count, sampleRate, channels, path);
		}

		#endregion

		#region PCM output (software mixing backend)

		/// <summary>
		/// Decodes <c>.wav</c> / <c>.ogg</c> / <c>.mp3</c> to interleaved float PCM in a
		/// <see cref="PcmAudioClip"/>. Mono/stereo only. Throws <see cref="NotSupportedException"/> for other
		/// extensions or unsupported WAVE sub-formats.
		/// </summary>
		public static AudioClip DecodeToPcm(string path)
		{
			var ext = Path.GetExtension(path).ToLowerInvariant();
			var (samples, count, sampleRate, channels) = ext switch
			{
				".ogg" => ReadOggFloats(path),
				".mp3" => ReadMp3Floats(path),
				".wav" => ReadWavFloats(path),
				_ => throw new NotSupportedException(
					$"Unsupported audio format '{ext}' for '{Path.GetFileName(path)}'. Supported: .wav, .ogg, .mp3."),
			};

			if (channels != 1 && channels != 2)
				throw new NotSupportedException(
					$"Audio file '{Path.GetFileName(path)}' has {channels} channels; only mono and stereo are supported.");

			if (count != samples.Length)
				Array.Resize(ref samples, count);

			return new PcmAudioClip(samples, channels, sampleRate);
		}

		#endregion

		#region Decode cores (interleaved float [-1,1])

		private static (float[] samples, int count, int sampleRate, int channels) ReadOggFloats(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new NVorbis.VorbisReader(stream, closeOnDispose: false);

			int channels = reader.Channels;
			int sampleRate = reader.SampleRate;

			// TotalSamples is per-channel; interleaved length is TotalSamples * channels.
			long interleavedLong = reader.TotalSamples * channels;
			int total = interleavedLong > 0 && interleavedLong <= int.MaxValue ? (int)interleavedLong : 0;

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

			return (samples, count, sampleRate, channels);
		}

		private static (float[] samples, int count, int sampleRate, int channels) ReadMp3Floats(string path)
		{
			using var stream = File.OpenRead(path);
			using var mpeg = new NLayer.MpegFile(stream);

			int channels = mpeg.Channels;
			int sampleRate = mpeg.SampleRate;

			var (samples, count) = ReadAll(mpeg.ReadSamples);
			return (samples, count, sampleRate, channels);
		}

		// Minimal RIFF/WAVE reader: 8/16-bit integer PCM and 32-bit IEEE float, mono/stereo. Chunks are
		// word-aligned; unknown chunks are skipped.
		private static (float[] samples, int count, int sampleRate, int channels) ReadWavFloats(string path)
		{
			using var fs = File.OpenRead(path);
			using var br = new BinaryReader(fs);

			if (Tag(br) != "RIFF")
				throw new NotSupportedException($"'{Path.GetFileName(path)}' is not a RIFF file.");
			br.ReadInt32(); // overall size
			if (Tag(br) != "WAVE")
				throw new NotSupportedException($"'{Path.GetFileName(path)}' is not a WAVE file.");

			int audioFormat = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
			byte[] data = null;

			while (fs.Position + 8 <= fs.Length)
			{
				string id = Tag(br);
				int size = br.ReadInt32();
				if (size < 0) break; // malformed
				long next = fs.Position + size + (size & 1); // chunk bodies are word-aligned

				if (id == "fmt ")
				{
					audioFormat = br.ReadInt16();
					channels = br.ReadInt16();
					sampleRate = br.ReadInt32();
					br.ReadInt32(); // byte rate
					br.ReadInt16(); // block align
					bitsPerSample = br.ReadInt16();
				}
				else if (id == "data")
				{
					data = br.ReadBytes(size);
				}

				// Header read always advances ≥8 bytes, so this loop terminates.
				fs.Position = Math.Min(next, fs.Length);
			}

			if (data == null || channels < 1)
				throw new NotSupportedException($"'{Path.GetFileName(path)}' has no readable WAVE data chunk.");

			float[] samples;
			if (audioFormat == 1 && bitsPerSample == 16)
			{
				int n = data.Length / 2;
				samples = new float[n];
				for (int i = 0, b = 0; i < n; i++, b += 2)
					samples[i] = (short)(data[b] | (data[b + 1] << 8)) / 32768f;
			}
			else if (audioFormat == 3 && bitsPerSample == 32)
			{
				int n = data.Length / 4;
				samples = new float[n];
				Buffer.BlockCopy(data, 0, samples, 0, n * 4); // little-endian float, matches x86/ARM
			}
			else if (audioFormat == 1 && bitsPerSample == 8)
			{
				int n = data.Length;
				samples = new float[n];
				for (int i = 0; i < n; i++)
					samples[i] = (data[i] - 128) / 128f; // 8-bit PCM is unsigned
			}
			else
			{
				throw new NotSupportedException(
					$"'{Path.GetFileName(path)}' WAVE sub-format unsupported (format={audioFormat}, bits={bitsPerSample}). " +
					"Supported: 8/16-bit PCM, 32-bit float.");
			}

			return (samples, samples.Length, sampleRate, channels);
		}

		private static string Tag(BinaryReader br)
		{
			var b = br.ReadBytes(4);
			return b.Length < 4 ? string.Empty : System.Text.Encoding.ASCII.GetString(b);
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

		#endregion

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
