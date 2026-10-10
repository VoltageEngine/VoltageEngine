using System.ComponentModel;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;

namespace Voltage.Pipeline.Fonts;

/// <summary>Embeds processed page images so a published font has no missing texture dependencies.</summary>
[ContentProcessor(DisplayName = "BMFont - Voltage")]
public sealed class BitmapFontProcessor : ContentProcessor<BitmapFontContent, BitmapFontContent>
{
	/// <summary>Premultiply the page textures at build time; the engine premultiplies on the CPU when a loader asks for it.</summary>
	[DefaultValue(false)]
	public bool PremultiplyAlpha { get; set; }

	public override BitmapFontContent Process(BitmapFontContent input, ContentProcessorContext context)
	{
		var parameters = new OpaqueDataDictionary
		{
			{ "PremultiplyAlpha", PremultiplyAlpha },
			{ "GenerateMipmaps", false },
			{ "ColorKeyEnabled", false },
			{ "TextureFormat", TextureProcessorOutputFormat.Color }
		};

		foreach (var page in input.Pages)
			page.Texture = context.BuildAndLoadAsset<TextureContent, TextureContent>(new ExternalReference<TextureContent>(page.File), "VoltageTextureProcessor", parameters, "TextureImporter");

		return input;
	}
}
