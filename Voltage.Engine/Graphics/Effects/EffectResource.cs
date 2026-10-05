using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Voltage.Utils.Extensions;

namespace Voltage
{
	public static class EffectResource
	{
		const string BundledPrefix = "Voltage.Graphics.Effects.Compiled.";
		public static string BuiltinOverrideDirectory { get; set; }
		public static string[] BundledEffectNames => Array.FindAll(typeof(EffectResource).Assembly.GetManifestResourceNames(),
			name => name.StartsWith(BundledPrefix, StringComparison.Ordinal));
		public static string BundledRevision { get; } = GetBundledRevision();

		static string GetBundledRevision()
		{
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			var names = BundledEffectNames;
			Array.Sort(names, StringComparer.Ordinal);
			foreach (var name in names)
			{
				using var stream = typeof(EffectResource).Assembly.GetManifestResourceStream(name);
				using var buffer = new MemoryStream();
				stream.CopyTo(buffer);
				hash.AppendData(buffer.ToArray());
			}
			return Convert.ToHexString(hash.GetHashAndReset()).Substring(0, 16);
		}
		// sprite effects
		internal static byte[] SpriteBlinkEffectBytes => GetFileResourceBytes("Content/Voltage/Effects/SpriteBlinkEffect.mgfxo");

		internal static byte[] SpriteLinesEffectBytes => GetFileResourceBytes("Content/Voltage/Effects/SpriteLines.mgfxo");

		internal static byte[] SpriteAlphaTestBytes => GetFileResourceBytes("Content/Voltage/Effects/SpriteAlphaTest.mgfxo");

		internal static byte[] CrosshatchBytes => GetFileResourceBytes("Content/Voltage/Effects/Crosshatch.mgfxo");

		internal static byte[] InvertBytes => GetFileResourceBytes("Content/Voltage/Effects/Invert.mgfxo");

		internal static byte[] NoiseBytes => GetFileResourceBytes("Content/Voltage/Effects/Noise.mgfxo");

		internal static byte[] TwistBytes => GetFileResourceBytes("Content/Voltage/Effects/Twist.mgfxo");

		internal static byte[] DotsBytes => GetFileResourceBytes("Content/Voltage/Effects/Dots.mgfxo");

		internal static byte[] DissolveBytes => GetFileResourceBytes("Content/Voltage/Effects/Dissolve.mgfxo");

		// post processor effects
		internal static byte[] BloomCombineBytes => GetFileResourceBytes("Content/Voltage/Effects/BloomCombine.mgfxo");

		internal static byte[] BloomExtractBytes => GetFileResourceBytes("Content/Voltage/Effects/BloomExtract.mgfxo");

		internal static byte[] GaussianBlurBytes => GetFileResourceBytes("Content/Voltage/Effects/GaussianBlur.mgfxo");

		internal static byte[] VignetteBytes => GetFileResourceBytes("Content/Voltage/Effects/Vignette.mgfxo");

		internal static byte[] LetterboxBytes => GetFileResourceBytes("Content/Voltage/Effects/Letterbox.mgfxo");

		internal static byte[] HeatDistortionBytes => GetFileResourceBytes("Content/Voltage/Effects/HeatDistortion.mgfxo");

		internal static byte[] SpriteLightMultiplyBytes => GetFileResourceBytes("Content/Voltage/Effects/SpriteLightMultiply.mgfxo");

		internal static byte[] PixelGlitchBytes => GetFileResourceBytes("Content/Voltage/Effects/PixelGlitch.mgfxo");

		internal static byte[] StencilLightBytes => GetFileResourceBytes("Content/Voltage/Effects/StencilLight.mgfxo");

		// deferred lighting
		internal static byte[] DeferredSpriteBytes => GetFileResourceBytes("Content/Voltage/Effects/DeferredSprite.mgfxo");

		internal static byte[] DeferredLightBytes => GetFileResourceBytes("Content/Voltage/Effects/DeferredLighting.mgfxo");

		// forward lighting
		internal static byte[] ForwardLightingBytes => GetFileResourceBytes("Content/Voltage/Effects/ForwardLighting.mgfxo");

		internal static byte[] PolygonLightBytes => GetFileResourceBytes("Content/Voltage/Effects/PolygonLight.mgfxo");

		// scene transitions
		internal static byte[] SquaresTransitionBytes => GetFileResourceBytes("Content/Voltage/Effects/transitions/Squares.mgfxo");

		// sprite or post processor effects
		internal static byte[] SpriteEffectBytes => GetMonoGameEmbeddedResourceBytes("Microsoft.Xna.Framework.Graphics.Effect.Resources.SpriteEffect.ogl.mgfxo");

		internal static byte[] MultiTextureOverlayBytes => GetFileResourceBytes("Content/Voltage/Effects/MultiTextureOverlay.mgfxo");

		internal static byte[] ScanlinesBytes => GetFileResourceBytes("Content/Voltage/Effects/Scanlines.mgfxo");

		internal static byte[] ReflectionBytes => GetFileResourceBytes("Content/Voltage/Effects/Reflection.mgfxo");

		internal static byte[] GrayscaleBytes => GetFileResourceBytes("Content/Voltage/Effects/Grayscale.mgfxo");

		internal static byte[] SepiaBytes => GetFileResourceBytes("Content/Voltage/Effects/Sepia.mgfxo");

		internal static byte[] PaletteCyclerBytes => GetFileResourceBytes("Content/Voltage/Effects/PaletteCycler.mgfxo");


		/// <summary>
		/// gets the raw byte[] from an EmbeddedResource
		/// </summary>
		/// <returns>The embedded resource bytes.</returns>
		/// <param name="name">Name.</param>
		static byte[] GetEmbeddedResourceBytes(string name)
		{
			var assembly = typeof(EffectResource).Assembly;
			using (var stream = assembly.GetManifestResourceStream(name))
			{
				using (var ms = new MemoryStream())
				{
					stream.CopyTo(ms);
					return ms.ToArray();
				}
			}
		}


		internal static byte[] GetMonoGameEmbeddedResourceBytes(string name)
		{
			var assembly = typeof(MathHelper).Assembly;
#if FNA
			name = name.Replace( ".ogl.mgfxo", ".fxb" );
#else
			// MG 3.8 decided to change the location of Effecs...sigh.
			if (!assembly.GetManifestResourceNames().Contains(name))
				name = name.Replace(".Framework", ".Framework.Platform");
#endif

			using (var stream = assembly.GetManifestResourceStream(name))
			{
				using (var ms = new MemoryStream())
				{
					stream.CopyTo(ms);
					return ms.ToArray();
				}
			}
		}


		/// <summary>
		/// fetches the raw byte data of a file from the Content folder. Used to keep the Effect subclass code simple and clean due to the Effect
		/// constructor requiring the byte[].
		/// </summary>
		/// <returns>The file resource bytes.</returns>
		/// <param name="path">Path.</param>
		public static byte[] GetFileResourceBytes(string path)
		{
#if FNA
			path = path.Replace( ".mgfxo", ".fxb" );
#endif

			try
			{
				var normalized = path.Replace('\\', '/');
				const string prefix = "Content/Voltage/Effects/";
				if (normalized.StartsWith(prefix, StringComparison.Ordinal))
				{
					var relative = normalized.Substring(prefix.Length);
					if (!string.IsNullOrEmpty(BuiltinOverrideDirectory))
					{
						var replacement = Path.Combine(BuiltinOverrideDirectory, relative);
						if (File.Exists(replacement))
						{
							try { return ReadEffect(File.OpenRead(replacement), path); }
							catch (InvalidDataException) { }
						}
					}
					var external = Path.Combine(AppContext.BaseDirectory, normalized);
					if (File.Exists(external))
					{
						try { return ReadEffect(File.OpenRead(external), path); }
						catch (InvalidDataException) { }
					}
					using var bundled = typeof(EffectResource).Assembly.GetManifestResourceStream(BundledPrefix + relative.Replace('/', '.'));
					if (bundled != null) return ReadEffect(bundled, path);
				}
				return ReadEffect(Path.IsPathRooted(path) ? File.OpenRead(path) : TitleContainer.OpenStream(path), path);
			}
			catch (Exception e)
			{
				var txt = $"Unable to load effect '{path}': {e.Message}. Custom effects must be compiled for DesktopGL and included in Content.";
				Debug.Error(txt);
				throw new Exception(txt, e);
			}

		}

		static byte[] ReadEffect(Stream stream, string path)
		{
			using (stream)
			using (var buffer = new MemoryStream())
			{
				stream.CopyTo(buffer);
				var bytes = buffer.ToArray();
#if !FNA
				if (bytes.Length < 10 || bytes[0] != 'M' || bytes[1] != 'G' || bytes[2] != 'F' || bytes[3] != 'X' || bytes[4] != 11 || bytes[5] != 0)
					throw new InvalidDataException($"'{path}' is not a valid DesktopGL effect; recompile with /Profile:OpenGL.");
#endif
				return bytes;
			}
		}
	}
}
