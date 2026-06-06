using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Textures;
using Voltage.Sprites;


namespace Voltage.Particles
{
	[Serializable]
	public class ParticleEmitterConfig : IDisposable
	{
		public Sprite Sprite;

		/// <summary>
		/// Optional. When set, particles will cycle through the animation frames instead of displaying a static Sprite.
		/// The animation always loops for the lifetime of the emitter. Takes priority over Sprite when set.
		/// </summary>
		public SpriteAnimation SpriteAnimation;

		/// <summary>
		/// If true, particles will simulate in world space. ie when the parent Transform moves it will have no effect on any already active Particles.
		/// </summary>
		public bool SimulateInWorldSpace = true;

		public Blend BlendFuncSource;
		public Blend BlendFuncDestination;

		/// <summary>
		/// sourcePosition is read in but internally it is not used. The ParticleEmitter.localPosition is what the emitter will use for positioning
		/// </summary>
		public Vector2 SourcePosition;

		public Vector2 SourcePositionVariance;
		public float Speed, SpeedVariance;
		public float ParticleLifespan, ParticleLifespanVariance;
		public float Angle, AngleVariance;
		public Vector2 Gravity;
		public float RadialAcceleration, RadialAccelVariance;
		public float TangentialAcceleration, TangentialAccelVariance;
		public Color StartColor = Color.White, StartColorVariance = Color.Transparent;
		public Color FinishColor = Color.White, FinishColorVariance = Color.Transparent;
		public uint MaxParticles;
		public float StartParticleSize, StartParticleSizeVariance;
		public float FinishParticleSize, FinishParticleSizeVariance;
		public float Duration;
		public ParticleEmitterType EmitterType;

		public float RotationStart, RotationStartVariance;
		public float RotationEnd, RotationEndVariance;
		public float EmissionRate;


		// Particle ivars only used when a maxRadius value is provided. These values are used for
		// the special purpose of creating the spinning portal emitter
		// Max radius at which particles are drawn when rotating
		public float MaxRadius;

		// Variance of the maxRadius
		public float MaxRadiusVariance;

		// Radius from source below which a particle dies
		public float MinRadius;

		// Variance of the minRadius
		public float MinRadiusVariance;

		// Number of degrees to rotate a particle around the source pos per second
		public float RotatePerSecond;

		// Variance in degrees for rotatePerSecond
		public float RotatePerSecondVariance;

		public ParticleEmitterConfig()
		{
			BlendFuncSource = Blend.SourceAlpha;
			BlendFuncDestination = Blend.InverseSourceAlpha;
			MaxParticles = 100;
			EmissionRate = 10f;
			ParticleLifespan = 1f;
			StartParticleSize = 32f;
			FinishParticleSize = 32f;
			Speed = 50f;
		}

		/// <summary>
		/// Creates a config preconfigured for a single animated particle effect (e.g. a hit flash).
		/// Lifespan and particle size are automatically derived from the animation so nothing
		/// needs to be calculated manually at the call site.
		/// </summary>
		public ParticleEmitterConfig(SpriteAnimation animation)
		{
			SpriteAnimation = animation;
			BlendFuncSource = Blend.SourceAlpha;
			BlendFuncDestination = Blend.InverseSourceAlpha;
			MaxParticles = 1;
			EmissionRate = 100f;
			Speed = 0f;
			SimulateInWorldSpace = true;

			// particle must live exactly one full animation cycle
			var totalDuration = 0f;
			foreach (var rate in animation.FrameRates)
				totalDuration += 1f / rate;
			ParticleLifespan = totalDuration;

			if (animation.Sprites.Length > 0)
			{
				StartParticleSize = animation.Sprites[0].SourceRect.Width;
				FinishParticleSize = animation.Sprites[0].SourceRect.Width;
			}
		}


		void IDisposable.Dispose()
		{
			if (Sprite != null)
			{
				Sprite.Texture2D.Dispose();
				Sprite.Texture2D = null;
				Sprite = null;
			}
		}
	}
}