using System;
using Voltage.Persistence;
using Voltage.Textures;

namespace Voltage.Sprites;

public class SpriteAnimation
{
	public readonly Sprite[] Sprites;
	public readonly float[] FrameRates;
	/// <summary>The normal frames carry a Glow layer's self-illumination in their alpha.</summary>
	public bool HasGlow;

	public SpriteAnimation(Sprite[] sprites, float frameRate)
	{
		Sprites = sprites;
		FrameRates = new float[sprites.Length];
		for (var i = 0; i < FrameRates.Length; ++i) FrameRates[i] = frameRate;
	}

	public SpriteAnimation(Sprite[] sprites, float[] frameRates)
	{
		Sprites = sprites;
		FrameRates = frameRates;
	}
}