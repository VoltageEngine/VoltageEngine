using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Voltage.Serialization;
using Voltage.Utils;
using Voltage.Utils.Collections;


namespace Voltage
{
	/// <summary>
	/// Note that this is not a full, multi-iteration physics system! This can be used for simple, arcade style physics.
	/// Based on http://elancev.name/oliver/2D%20polygon.htm#tut5
	/// </summary>
	public class ArcadeRigidbody : Component, IUpdatable
	{
		public class ArcadeRigidbodyComponentData : ComponentData
		{
			public float Mass { get; set; }
			public float Elasticity { get; set; }
			public float Friction { get; set; }
			public float Glue { get; set; }
			public bool ShouldUseGravity { get; set; }
			public Vector2 Velocity { get; set; }

			public ArcadeRigidbodyComponentData() { }

			public ArcadeRigidbodyComponentData(ArcadeRigidbody rigidbody)
			{
				Mass = rigidbody.Mass;
				Elasticity = rigidbody.Elasticity;
				Friction = rigidbody.Friction;
				Glue = rigidbody.Glue;
				ShouldUseGravity = rigidbody.ShouldUseGravity;
				Velocity = rigidbody.Velocity;
				Enabled = rigidbody.Enabled;
			}
		}

		/// <summary>
		/// mass of this rigidbody. A 0 mass will make this an immovable object.
		/// </summary>
		/// <value>The mass.</value>
		public float Mass
		{
			get => _mass;
			set => SetMass(value);
		}

		/// <summary>
		/// 0 - 1 range where 0 is no bounce and 1 is perfect reflection
		/// </summary>
		public float Elasticity
		{
			get => _elasticity;
			set => SetElasticity(value);
		}

		/// <summary>
		/// 0 - 1 range. 0 means no friction, 1 means the object will stop dead on
		/// </summary>
		public float Friction
		{
			get => _friction;
			set => SetFriction(value);
		}

		/// <summary>
		/// 0 - 9 range. When a collision occurs and it has risidual motion along the surface of collision if its square magnitude is less
		/// than glue friction will be set to the maximum for the collision resolution.
		/// </summary>
		public float Glue
		{
			get => _glue;
			set => SetGlue(value);
		}

		/// <summary>
		/// if true, Physics.gravity will be taken into account each frame
		/// </summary>
		public bool ShouldUseGravity = true;

		/// <summary>
		/// velocity of this rigidbody
		/// </summary>
		public Vector2 Velocity;

		/// <summary>
		/// rigidbodies with a mass of 0 are considered immovable. Changing velocity and collisions will have no effect on them.
		/// </summary>
		/// <value><c>true</c> if is immovable; otherwise, <c>false</c>.</value>
		public bool IsImmovable => _mass < 0.0001f;

		float _mass = 10f;
		float _elasticity = 0.5f;
		float _friction = 0.5f;
		float _glue = 0.01f;
		float _inverseMass;
		Collider _collider;

		// Polling-based trigger detection, deduplicated at the EXTERNAL trigger level.
		// When the rigidbody entity has multiple colliders (e.g. a Body + GroundCollider with
		// IsTrigger=true), each one would otherwise generate its own pair against the same
		// external trigger. The naive listener pattern (Enabled = false on every Exit) breaks
		// when one local collider leaves while the others are still inside. Tracking the
		// external trigger as the unit of state collapses these into a single Enter/Stay/Exit
		// lifecycle per external trigger.
		HashSet<Collider> _activeExternalTriggers = new HashSet<Collider>();
		HashSet<Collider> _previousExternalTriggers = new HashSet<Collider>();
		// Remembers which of our local colliders first touched each external trigger this frame.
		// Used to pass a meaningful `local` argument to ITriggerListener callbacks.
		Dictionary<Collider, Collider> _externalToLocal = new Dictionary<Collider, Collider>();
		List<ITriggerListener> _tempTriggerList = new List<ITriggerListener>();


		public ArcadeRigidbody()
		{
			_inverseMass = 1 / _mass;
		}


		#region Fluent setters

		/// <summary>
		/// mass of this rigidbody. A 0 mass will make this an immovable object.
		/// </summary>
		/// <returns>The mass.</returns>
		/// <param name="mass">Mass.</param>
		public ArcadeRigidbody SetMass(float mass)
		{
			_mass = Mathf.Clamp(mass, 0, float.MaxValue);

			if (_mass > 0.0001f)
				_inverseMass = 1 / _mass;
			else
				_inverseMass = 0f;
			return this;
		}

		/// <summary>
		/// 0 - 1 range where 0 is no bounce and 1 is perfect reflection
		/// </summary>
		/// <returns>The elasticity.</returns>
		/// <param name="value">Value.</param>
		public ArcadeRigidbody SetElasticity(float value)
		{
			_elasticity = Mathf.Clamp01(value);
			return this;
		}

		/// <summary>
		/// 0 - 1 range. 0 means no friction, 1 means the object will stop dead on
		/// </summary>
		/// <returns>The friction.</returns>
		/// <param name="value">Value.</param>
		public ArcadeRigidbody SetFriction(float value)
		{
			_friction = Mathf.Clamp01(value);
			return this;
		}

		/// <summary>
		/// 0 - 9 range. When a collision occurs and it has risidual motion along the surface of collision if its square magnitude is less
		/// than glue friction will be set to the maximum for the collision resolution.
		/// </summary>
		/// <returns>The glue.</returns>
		/// <param name="value">Value.</param>
		public ArcadeRigidbody SetGlue(float value)
		{
			_glue = Mathf.Clamp(value, 0, 10);
			return this;
		}

		/// <summary>
		/// velocity of this rigidbody
		/// </summary>
		/// <returns>The velocity.</returns>
		/// <param name="velocity">Velocity.</param>
		public ArcadeRigidbody SetVelocity(Vector2 velocity)
		{
			Velocity = velocity;
			return this;
		}

		#endregion


		/// <summary>
		/// add an instant force impulse to the rigidbody using its mass. force is an acceleration in pixels per second per second. The
		/// force is multiplied by 100000 to make the values more reasonable to use.
		/// </summary>
		/// <param name="force">Force.</param>
		public void AddImpulse(Vector2 force)
		{
			if (!IsImmovable)
				Velocity += force * 100000 * (_inverseMass * Time.DeltaTime * Time.DeltaTime);
		}

		public override void OnStart()
		{
			_collider = Entity.GetComponent<Collider>();
			Debug.WarnIf(_collider == null, "ArcadeRigidbody has no Collider. ArcadeRigidbody requires a Collider!");
		}

		public virtual void Update()
		{
			if (IsImmovable || _collider == null)
			{
				Velocity = Vector2.Zero;
				return;
			}

			if (ShouldUseGravity)
				Velocity += Physics.Gravity * Time.DeltaTime;

			Entity.Transform.Position += Velocity * Time.DeltaTime;

			CollisionResult collisionResult;

			// fetch anything that we might collide with at our new position
			var neighbors = Physics.BoxcastBroadphaseExcludingSelf(_collider, _collider.CollidesWithLayers);
			foreach (var neighbor in neighbors)
			{
				// if the neighbor collider is of the same entity, ignore it
				if (neighbor.Entity == Entity)
				{
					continue;
				}

				if (!_collider.IsTrigger && !neighbor.IsTrigger)
					if (_collider.CollidesWith(neighbor, out collisionResult))
					{
						// if the neighbor has an ArcadeRigidbody we handle full collision response. If not, we calculate things based on the
						// neighbor being immovable.
						var neighborRigidbody = neighbor.Entity.GetComponent<ArcadeRigidbody>();
						if (neighborRigidbody != null)
						{
							ProcessOverlap(neighborRigidbody, ref collisionResult.MinimumTranslationVector);
							ProcessCollision(neighborRigidbody, ref collisionResult.MinimumTranslationVector);
						}
						else
						{
							// neighbor has no ArcadeRigidbody so we assume its immovable and only move ourself
							Entity.Transform.Position -= collisionResult.MinimumTranslationVector;
							var relativeVelocity = Velocity;
							CalculateResponseVelocity(ref relativeVelocity,
								ref collisionResult.MinimumTranslationVector,
								out relativeVelocity);
							Velocity += relativeVelocity;
						}
					}
			}

			UpdateTriggerInteractions();
		}


		/// <summary>
		/// Polls the broadphase per-frame and fires OnTriggerEnter / OnTriggerStay / OnTriggerExit
		/// on ITriggerListeners. Events are deduplicated at the external-trigger level: each
		/// external trigger gets exactly one Enter when the first local collider touches it,
		/// continuous Stay while any local collider remains inside, and exactly one Exit when
		/// the last local collider leaves.
		/// </summary>
		void UpdateTriggerInteractions()
		{
			if (_collider == null)
			{
				ReleasePreviousTriggers();
				return;
			}

			var colliders = Entity.GetComponents<Collider>();
			for (var i = 0; i < colliders.Count; i++)
			{
				var local = colliders[i];
				if (!local.Enabled)
					continue;

				var localBounds = local.Bounds;
				var neighbors = Physics.BoxcastBroadphase(localBounds, local.CollidesWithLayers);
				foreach (var neighbor in neighbors)
				{
					if (neighbor.Entity == Entity)
						continue;
					if (!local.IsTrigger && !neighbor.IsTrigger)
						continue;

					RegisterActiveExternal(neighbor, local);
				}
			}
			ListPool<Collider>.Free(colliders);

			// Inclusive rescue pass: pairs whose AABBs touch on an exact edge (e.g. player.bottom
			// == floor.top after MTV) fail the broadphase's strict-< intersection test. Re-check
			// every previously-known external with inclusive <= so they don't fall out spuriously.
			if (_previousExternalTriggers.Count > 0)
			{
				foreach (var external in _previousExternalTriggers)
				{
					if (_activeExternalTriggers.Contains(external))
						continue;
					if (external.Entity == null || !external.Enabled)
						continue;

					var localRescue = ResolveInclusiveOverlap(external);
					if (localRescue != null)
						RegisterActiveExternal(external, localRescue);
				}
			}

			// Exit: any external present last frame but not this frame
			foreach (var external in _previousExternalTriggers)
			{
				if (_activeExternalTriggers.Contains(external))
					continue;

				_externalToLocal.TryGetValue(external, out var localForExit);
				if (localForExit == null || !localForExit.Enabled)
					localForExit = _collider;
				NotifyExternalTrigger(external, localForExit, TriggerEventType.Exit);
				_externalToLocal.Remove(external);
			}

			// swap sets to keep per-frame allocations at zero
			var swap = _previousExternalTriggers;
			_previousExternalTriggers = _activeExternalTriggers;
			_activeExternalTriggers = swap;
			_activeExternalTriggers.Clear();
		}


		void RegisterActiveExternal(Collider external, Collider local)
		{
			// First sighting this frame? Fire Enter or Stay, depending on prior frame state.
			if (!_activeExternalTriggers.Add(external))
				return;

			_externalToLocal[external] = local;
			NotifyExternalTrigger(external, local,
				_previousExternalTriggers.Contains(external) ? TriggerEventType.Stay : TriggerEventType.Enter);
		}


		Collider ResolveInclusiveOverlap(Collider external)
		{
			var externalBounds = external.Bounds;
			var colliders = Entity.GetComponents<Collider>();
			Collider result = null;
			for (var i = 0; i < colliders.Count; i++)
			{
				var local = colliders[i];
				if (!local.Enabled)
					continue;
				if (!local.IsTrigger && !external.IsTrigger)
					continue;

				var localBounds = local.Bounds;
				if (localBounds.Left   <= externalBounds.Right  &&
					externalBounds.Left  <= localBounds.Right   &&
					localBounds.Top    <= externalBounds.Bottom &&
					externalBounds.Top   <= localBounds.Bottom)
				{
					result = local;
					break;
				}
			}
			ListPool<Collider>.Free(colliders);
			return result;
		}


		void ReleasePreviousTriggers()
		{
			if (_previousExternalTriggers.Count == 0)
				return;

			foreach (var external in _previousExternalTriggers)
			{
				_externalToLocal.TryGetValue(external, out var local);
				NotifyExternalTrigger(external, local ?? _collider, TriggerEventType.Exit);
			}
			_previousExternalTriggers.Clear();
			_externalToLocal.Clear();
		}


		enum TriggerEventType { Enter, Stay, Exit }


		void NotifyExternalTrigger(Collider external, Collider local, TriggerEventType eventType)
		{
			// Dispatch on our entity's listeners (the rigidbody side)
			Entity.GetComponents(_tempTriggerList);
			for (var i = 0; i < _tempTriggerList.Count; i++)
				DispatchTrigger(_tempTriggerList[i], eventType, external, local);
			_tempTriggerList.Clear();

			// Dispatch on the external entity's listeners (the trigger zone side)
			if (external.Entity != null)
			{
				external.Entity.GetComponents(_tempTriggerList);
				for (var i = 0; i < _tempTriggerList.Count; i++)
					DispatchTrigger(_tempTriggerList[i], eventType, local, external);
				_tempTriggerList.Clear();
			}
		}


		static void DispatchTrigger(ITriggerListener listener, TriggerEventType eventType, Collider other, Collider local)
		{
			switch (eventType)
			{
				case TriggerEventType.Enter: listener.OnTriggerEnter(other, local); break;
				case TriggerEventType.Stay:  listener.OnTriggerStay(other, local);  break;
				case TriggerEventType.Exit:  listener.OnTriggerExit(other, local);  break;
			}
		}

		/// <summary>
		/// separates two overlapping rigidbodies. Handles the case of either being immovable as well.
		/// </summary>
		/// <param name="other">Other.</param>
		/// <param name="minimumTranslationVector"></param>
		void ProcessOverlap(ArcadeRigidbody other, ref Vector2 minimumTranslationVector)
		{
			if (IsImmovable)
			{
				other.Entity.Transform.Position += minimumTranslationVector;
			}
			else if (other.IsImmovable)
			{
				Entity.Transform.Position -= minimumTranslationVector;
			}
			else
			{
				Entity.Transform.Position -= minimumTranslationVector * 0.5f;
				other.Entity.Transform.Position += minimumTranslationVector * 0.5f;
			}
		}

		/// <summary>
		/// handles the collision of two non-overlapping rigidbodies. New velocities will be assigned to each rigidbody as appropriate.
		/// </summary>
		/// <param name="other">Other.</param>
		/// <param name="inverseMTV">Inverse MT.</param>
		void ProcessCollision(ArcadeRigidbody other, ref Vector2 minimumTranslationVector)
		{
			// we compute a response for the two colliding objects. The calculations are based on the relative velocity of the objects
			// which gets reflected along the collided surface normal. Then a part of the response gets added to each object based on mass.
			var relativeVelocity = Velocity - other.Velocity;

			CalculateResponseVelocity(ref relativeVelocity, ref minimumTranslationVector, out relativeVelocity);

			// now we use the masses to linearly scale the response on both rigidbodies
			var totalInverseMass = _inverseMass + other._inverseMass;
			var ourResponseFraction = _inverseMass / totalInverseMass;
			var otherResponseFraction = other._inverseMass / totalInverseMass;

			Velocity += relativeVelocity * ourResponseFraction;
			other.Velocity -= relativeVelocity * otherResponseFraction;
		}

		/// <summary>
		/// given the relative velocity between the two objects and the MTV this method modifies the relativeVelocity to make it a collision
		/// response.
		/// </summary>
		/// <param name="relativeVelocity">Relative velocity.</param>
		/// <param name="minimumTranslationVector">Minimum translation vector.</param>
		void CalculateResponseVelocity(ref Vector2 relativeVelocity, ref Vector2 minimumTranslationVector,
									   out Vector2 responseVelocity)
		{
			// first, we get the normalized MTV in the opposite direction: the surface normal
			var inverseMTV = minimumTranslationVector * -1f;
			Vector2 normal;
			Vector2.Normalize(ref inverseMTV, out normal);

			// the velocity is decomposed along the normal of the collision and the plane of collision.
			// The elasticity will affect the response along the normal (normalVelocityComponent) and the friction will affect
			// the tangential component of the velocity (tangentialVelocityComponent)
			float n;
			Vector2.Dot(ref relativeVelocity, ref normal, out n);

			var normalVelocityComponent = normal * n;
			var tangentialVelocityComponent = relativeVelocity - normalVelocityComponent;

			if (n > 0.0f)
				normalVelocityComponent = Vector2.Zero;

			// if the squared magnitude of the tangential component is less than glue then we bump up the friction to the max
			var coefficientOfFriction = _friction;
			if (tangentialVelocityComponent.LengthSquared() < _glue)
				coefficientOfFriction = 1.01f;

			// elasticity affects the normal component of the velocity and friction affects the tangential component
			responseVelocity = -(1.0f + _elasticity) * normalVelocityComponent -
							   coefficientOfFriction * tangentialVelocityComponent;
		}
	}
}
