using System;
using Nez.Persistence;
using Nez.Textures;

namespace Nez.Sprites;

public class SpriteAnimation
{
	public readonly Sprite[] Sprites;
	public readonly float[] FrameRates;

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

	// public SpriteAnimation Clone()
	// {
	// 	// Deep copy Sprites array (clone each Sprite)
	// 	var spritesClone = new Sprite[Sprites.Length];
	// 	for (int i = 0; i < Sprites.Length; i++)
	// 		spritesClone[i] = Sprites[i]?.Clone();
	//
	// 	// Deep copy FrameRates array
	// 	var frameRatesClone = new float[FrameRates.Length];
	// 	Array.Copy(FrameRates, frameRatesClone, FrameRates.Length);
	//
	// 	return new SpriteAnimation(spritesClone, frameRatesClone);
	// }
}