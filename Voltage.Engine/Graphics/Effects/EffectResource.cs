using System;
using System.IO;
using Microsoft.Xna.Framework;
using Voltage.Utils.Extensions;

namespace Voltage
{
	public static class EffectResource
	{
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

			byte[] bytes;
			try
			{
				using (var stream = TitleContainer.OpenStream(path))
				{
					if (stream.CanSeek)
					{
						bytes = new byte[stream.Length];
						stream.Read(bytes, 0, bytes.Length);
					}
					else
					{
						using (var ms = new MemoryStream())
						{
							stream.CopyTo(ms);
							bytes = ms.ToArray();
						}
					}
				}
			}
			catch (Exception e)
			{
				var txt = string.Format(
					"OpenStream failed to find file at path: {0}. Possible errors: \n " +
					"1) Did you 'Build -> Build Effects -> Build ALL' ?. \n" +
					"2) Did you add the Effect you selected to the Content folder and set its properties to copy to output directory?",
					path);
				Debug.Error(txt);
				throw new Exception(txt, e);
			}

			return bytes;
		}
	}
}