using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Voltage.Textures;
using Voltage.Utils;
using Voltage.Utils.Collections;


namespace Voltage.Particles
{
	public class ParticleEmitter : RenderableComponent, IUpdatable
	{
		public override RectangleF Bounds => _bounds;

		public bool IsPaused => _isPaused;
		public bool IsPlaying => _active && !_isPaused;
		public bool IsStopped => !_active && !_isPaused;
		public bool IsEmitting => _emitting;
		public float ElapsedTime => _elapsedTime;

		private Vector2 _spriteRenderOrigin;

		/// <summary>
		/// convenience method for setting ParticleEmitterConfig.simulateInWorldSpace. If true, particles will simulate in world space. ie when the
		/// parent Transform moves it will have no effect on any already active Particles.
		/// </summary>
		public bool SimulateInWorldSpace
		{
			set => _emitterConfig.SimulateInWorldSpace = value;
		}

		/// <summary>
		/// If true, the particle Sprite will render from its own origin rather than default Sprite.Center
		/// </summary>
		public bool ShouldRenderUsingSpriteOrigin
		{
			set
			{
				if (_emitterConfig.Sprite != null)
				{
					if (value)
						_spriteRenderOrigin = _emitterConfig.Sprite.Origin;
					else
						_spriteRenderOrigin = _emitterConfig.Sprite.Center;
				}
			}
		}

		private bool _shouldRenderUsingSpriteOrigin;

		/// <summary>
		/// config object with various properties to deal with particle collisions
		/// </summary>
		public ParticleCollisionConfig CollisionConfig;

		/// <summary>
		/// keeps track of how many particles should be emitted
		/// </summary>
		float _emitCounter;

		/// <summary>
		/// tracks the elapsed time of the emitter
		/// </summary>
		float _elapsedTime;

		bool _active = false;
		bool _isPaused;

		/// <summary>
		/// if the emitter is emitting this will be true. Note that emitting can be false while particles are still alive. emitting gets set
		/// to false and then any live particles are allowed to finish their lifecycle.
		/// </summary>
		bool _emitting;

		List<Particle> _particles;
		bool _playOnAwake;
		[Serialize] ParticleEmitterConfig _emitterConfig;

		// Animation fields
		public float AnimationSpeed = 1f;
		private int _animCycleTarget;
		private int _animCycleCount;

		#region Events

		/// <summary>
		/// event that's going to be called when particles count becomes 0 after stopping emission.
		/// emission can stop after either we stop it manually or when we run for entire duration specified in ParticleEmitterConfig.
		/// </summary>
		public event Action<ParticleEmitter> OnAllParticlesExpired;

		/// <summary>
		/// event that's going to be called when emission is stopped due to reaching duration specified in ParticleEmitterConfig
		/// </summary>
		public event Action<ParticleEmitter> OnEmissionDurationReached;

		/// <summary>
		/// event fired when the configured number of sprite animation cycles have completed.
		/// emission is stopped immediately after this fires; any live particles are allowed to expire naturally.
		/// </summary>
		public event Action<ParticleEmitter> OnAnimationCyclesCompleted;

		#endregion

		public ParticleEmitter() : this(new ParticleEmitterConfig())
		{
			ShouldRenderUsingSpriteOrigin = false;
		}

		public ParticleEmitter(ParticleEmitterConfig emitterConfig, bool playOnAwake = true)
		{
			_emitterConfig = emitterConfig;
			_playOnAwake = playOnAwake;
			_particles = new List<Particle>((int) _emitterConfig.MaxParticles);
			Pool<Particle>.WarmCache((int) _emitterConfig.MaxParticles);

			// set some sensible defaults
			CollisionConfig.Elasticity = 0.5f;
			CollisionConfig.Friction = 0.5f;
			CollisionConfig.CollidesWithLayers = Physics.AllLayers;
			CollisionConfig.Gravity = _emitterConfig.Gravity;
			CollisionConfig.LifetimeLoss = 0f;
			CollisionConfig.MinKillSpeedSquared = float.MinValue;
			CollisionConfig.RadiusScale = 0.8f;

			// performs _spriteRenderOrigin = _emitterConfig.Sprite.Center;
			ShouldRenderUsingSpriteOrigin = false;
			
			Init();
		}


		/// <summary>
		/// creates the Batcher and loads the texture if it is available
		/// </summary>
		void Init()
		{
			// prep our custom BlendState and set the Material with it
			var blendState = new BlendState();
			blendState.ColorSourceBlend = blendState.AlphaSourceBlend = _emitterConfig.BlendFuncSource;
			blendState.ColorDestinationBlend = blendState.AlphaDestinationBlend = _emitterConfig.BlendFuncDestination;

			SetMaterial(new Material(blendState));
		}


		#region Component/RenderableComponent

		public override void OnStart()
		{
			if (_playOnAwake)
				Play();
		}

		/// <summary>
		/// configures the emitter to stop emission after <paramref name="cycles"/> full sprite animation cycles have completed.
		/// fires <see cref="OnAnimationCyclesCompleted"/> when the target is reached.
		/// pass 0 to disable.
		/// </summary>
		public ParticleEmitter StopEmitAfterAnimationCycles(int cycles)
		{
			_animCycleTarget = cycles;
			_animCycleCount = 0;
			return this;
		}

		public virtual void Update()
		{
			if (_isPaused)
				return;

			// prep data for the particle.update method
			var rootPosition = Entity.Transform.Position + _localOffset;

			// if the emitter is active and the emission rate is greater than zero then emit particles
			if (_active && _emitterConfig.EmissionRate > 0)
			{
				if (_emitting)
				{
					var rate = 1.0f / _emitterConfig.EmissionRate;

					if (_particles.Count < _emitterConfig.MaxParticles)
						_emitCounter += Time.DeltaTime;

					while (_particles.Count < _emitterConfig.MaxParticles && _emitCounter > rate)
					{
						AddParticle(rootPosition);
						_emitCounter -= rate;
					}

					_elapsedTime += Time.DeltaTime;

					if (_emitterConfig.Duration != -1 && _emitterConfig.Duration < _elapsedTime)
					{
						// when we hit our duration we dont emit any more particles
						_emitting = false;

						if (OnEmissionDurationReached != null)
							OnEmissionDurationReached(this);
					}
				}
			}

			// checked outside the emission-rate guard so EmitAt particles also trigger cleanup
			if (_active && !_emitting && _particles.Count == 0)
			{
				Stop();

				if (OnAllParticlesExpired != null)
					OnAllParticlesExpired(this);
			}

			var min = new Vector2(float.MaxValue, float.MaxValue);
			var max = new Vector2(float.MinValue, float.MinValue);
			var maxParticleSize = float.MinValue;

			for (var i = _particles.Count - 1; i >= 0; i--)
			{
				var currentParticle = _particles[i];

				if (currentParticle.Update(_emitterConfig, ref CollisionConfig, rootPosition))
				{
					// accumulate completed cycles for StopEmitAfterAnimationCycles
					_animCycleCount += currentParticle.AnimCycleCount;
					Pool<Particle>.Free(currentParticle);
					_particles.RemoveAt(i);

					if (_animCycleTarget > 0 && _animCycleCount >= _animCycleTarget)
					{
						OnAnimationCyclesCompleted?.Invoke(this);
						PauseEmission();
						_animCycleCount = 0;
					}
				}
				else
				{
					var pos = _emitterConfig.SimulateInWorldSpace ? currentParticle.SpawnPosition : rootPosition;
					pos += currentParticle.Position;
					Vector2.Min(ref min, ref pos, out min);
					Vector2.Max(ref max, ref pos, out max);
					maxParticleSize = Math.Max(maxParticleSize, currentParticle.ParticleSize);
				}
			}

			_bounds.Location = min;
			_bounds.Width = max.X - min.X;
			_bounds.Height = max.Y - min.Y;

			var boundsSprite = GetCurrentSprite(null);
			if (boundsSprite == null)
			{
				_bounds.Inflate(1 * maxParticleSize, 1 * maxParticleSize);
			}
			else
			{
				maxParticleSize /= boundsSprite.SourceRect.Width;
				_bounds.Inflate(boundsSprite.SourceRect.Width * maxParticleSize,
					boundsSprite.SourceRect.Height * maxParticleSize);
			}
		}


		public override void Render(Batcher batcher, Camera camera)
		{
			// we still render when we are paused
			if (!_active && !_isPaused)
				return;

			var rootPosition = Entity.Transform.Position + _localOffset;

			// loop through all the particles updating their location and color
			for (var i = 0; i < _particles.Count; i++)
			{
				var currentParticle = _particles[i];
				var pos = _emitterConfig.SimulateInWorldSpace ? currentParticle.SpawnPosition : rootPosition;

				var currentSprite = GetCurrentSprite(currentParticle);
				if (currentSprite == null)
				{
					batcher.Draw(Graphics.Instance.PixelTexture, pos + currentParticle.Position, currentParticle.Color,
						currentParticle.Rotation, Vector2.One, currentParticle.ParticleSize * 0.5f, SpriteEffects.None,
						LayerDepth);
				}
				else
				{
					var origin = _emitterConfig.SpriteAnimation != null
						? (_shouldRenderUsingSpriteOrigin ? currentSprite.Origin : currentSprite.Center)
						: _spriteRenderOrigin;
					batcher.Draw(currentSprite, pos + currentParticle.Position,
						currentParticle.Color, currentParticle.Rotation, origin,
						currentParticle.ParticleSize / currentSprite.SourceRect.Width, SpriteEffects.None,
						LayerDepth);
				}
			}
		}

		#endregion


		/// <summary>
		/// removes all particles from the particle emitter
		/// </summary>
		public void Clear()
		{
			for (var i = 0; i < _particles.Count; i++)
				Pool<Particle>.Free(_particles[i]);
			_particles.Clear();
		}

		/// <summary>
		/// plays the particle emitter
		/// </summary>
		public void Play()
		{
			// if we are just unpausing, we only toggle flags and we dont mess with any other parameters
			if (_isPaused)
			{
				_active = true;
				_isPaused = false;
				return;
			}

			_active = true;
			_emitting = true;
			_elapsedTime = 0;
			_emitCounter = 0;
			_animCycleCount = 0;
		}

		/// <summary>
		/// stops the particle emitter
		/// </summary>
		public void Stop()
		{
			_active = false;
			_isPaused = false;
			_elapsedTime = 0;
			_emitCounter = 0;
			_animCycleCount = 0;
			Clear();
		}

		/// <summary>
		/// pauses the particle emitter
		/// </summary>
		public void Pause()
		{
			_isPaused = true;
			_active = false;
		}

		/// <summary>
		/// resumes emission of particles.
		/// this is possible only if stop() wasn't called and emission wasn't stopped due to duration
		/// </summary>
		public void ResumeEmission()
		{
			if (IsStopped || (_emitterConfig.Duration != -1 && _emitterConfig.Duration < _elapsedTime))
				return;

			_emitting = true;
		}

		/// <summary>
		/// pauses emission of particles while allowing existing particles to expire
		/// </summary>
		public void PauseEmission()
		{
			_emitting = false;
		}

		/// <summary>
		/// manually emit some particles
		/// </summary>
		/// <param name="count">Count.</param>
		public void Emit(int count)
		{
			var rootPosition = Entity.Transform.Position + _localOffset;

			Init();
			_active = true;
			for (var i = 0; i < count; i++)
				AddParticle(rootPosition);
		}

		/// <summary>
		/// spawns a single particle at the given world position without resetting emitter state.
		/// safe to call multiple times in quick succession — each particle is fully independent.
		/// </summary>
		public void EmitAt(Vector2 worldPosition)
		{
			_active = true;
			AddParticle(worldPosition);
		}

		/// <summary>
		/// adds a Particle to the emitter
		/// </summary>
		void AddParticle(Vector2 position)
		{
			// sync lifespan with current AnimationSpeed so the particle lives for exactly one cycle
			var anim = _emitterConfig.SpriteAnimation;
			if (anim != null && anim.Sprites.Length > 0)
			{
				var totalDuration = 0f;
				foreach (var rate in anim.FrameRates)
					totalDuration += 1f / (rate * Math.Max(0.001f, AnimationSpeed));
				_emitterConfig.ParticleLifespan = totalDuration;
			}

			var particle = Pool<Particle>.Obtain();
			particle.Initialize(_emitterConfig, position, AnimationSpeed);
			_particles.Add(particle);
		}

		/// <summary>
		/// returns the sprite for the given particle's current animation frame, or the static Sprite if no animation is set.
		/// pass null to get the first frame (used for bounds calculation).
		/// </summary>
		private Sprite GetCurrentSprite(Particle p)
		{
			var anim = _emitterConfig.SpriteAnimation;
			if (anim != null && anim.Sprites.Length > 0)
				return anim.Sprites[p != null ? p.AnimFrame : 0];
			return _emitterConfig.Sprite;
		}
	}
}