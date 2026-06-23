using System;
using Voltage.Persistence;


namespace Voltage
{
	/// <summary>
	/// Base class for components that belong to a <see cref="Scene"/> rather than an <see cref="Entity"/>.
	/// Derive from this class to implement scene-scoped behavior (e.g. LevelManager, AudioMixer, GameRules).
	/// <para>Mark derived classes <c>partial</c> so the source generator can emit the serialization code
	/// required to save and reload their fields in .vscene files.</para>
	/// </summary>
	public abstract class SceneComponent : IComparable<SceneComponent>
	{
		/// <summary>
		/// the scene this SceneComponent is attached to
		/// </summary>
		[JsonExclude]
		public Scene Scene;

		/// <summary>
		/// true if the SceneComponent is enabled. Changes in state result in onEnabled/onDisable being called.
		/// </summary>
		/// <value><c>true</c> if enabled; otherwise, <c>false</c>.</value>
		public bool Enabled
		{
			get => _enabled;
			set => SetEnabled(value);
		}

		/// <summary>
		/// update order of the SceneComponents on this Scene
		/// </summary>
		/// <value>The order.</value>
		public int UpdateOrder { get; protected set; } = 0;

		/// <summary>
		/// Whether this SceneComponent should be saved to the .vscene file.
		/// Set to true by the editor when the component is added at edit-time.
		/// </summary>
		public bool IsSerialized { get; internal set; }

		/// <summary>
		/// Display name used in the inspector. Defaults to the type name.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Mirrors the same field on <see cref="ComponentData"/> so the source-generated Data
		/// property can read/write it without conditional logic. Has no selection semantics
		/// for SceneComponents (they are not entities in the viewport).
		/// </summary>
		[HideInInspector]
		public bool CanBeSelected = true;

		/// <summary>
		/// Gets or sets the serializable data snapshot for this SceneComponent.
		/// The source generator emits an override for partial subclasses.
		/// Engine-built SceneComponents with custom fields must provide their own override.
		/// </summary>
		public virtual ComponentData Data
		{
			get => null;
			set { }
		}

		/// <summary>
		/// Called by the reference resolver after scene load to restore Component/Entity/Transform
		/// reference fields. The source generator emits an override for partial subclasses that
		/// have such fields — zero reflection, NativeAOT-safe.
		/// </summary>
		public virtual void ApplyResolvedReferences(ComponentData data, Scene scene) { }

		/// <summary>
		/// Rewrites EntityPersistentId on every EntityReference and ComponentReference field
		/// whose stored entity id is a key in <paramref name="remap"/>. Mirror of the same
		/// virtual on <see cref="Component"/>; overridden by the source generator for partial
		/// SceneComponent subclasses.
		/// </summary>
		public virtual void RemapReferences(System.Collections.Generic.Dictionary<Guid, Guid> remap) { }

		/// <summary>
		/// Temporary storage for ComponentData assigned during scene load, passed to
		/// <see cref="Voltage.Serialization.ComponentReferenceResolver"/> after all entities
		/// are instantiated. Cleared by the resolver once references are wired up.
		/// </summary>
		[JsonExclude]
		protected internal ComponentData _pendingLoadedData;

		/// <summary>
		/// Guards against calling <see cref="OnStart"/> more than once (e.g. if components
		/// are added at runtime after the initial scene startup).
		/// </summary>
		internal bool _onStartCalled;

		bool _enabled = true;

		protected SceneComponent()
		{
			Name = GetType().Name;
		}

		public void SetSerialized(bool isOn) => IsSerialized = isOn;

		#region SceneComponent Lifecycle

		/// <summary>
		/// Called once after the SceneComponent is added to the scene and all entities have been loaded.
		/// Override to perform initialization that depends on the scene being ready.
		/// </summary>
		public virtual void OnStart()
		{
		}

		/// <summary>
		/// called when this SceneComponent is enabled
		/// </summary>
		public virtual void OnEnabled()
		{
		}


		/// <summary>
		/// called when the this SceneComponent is disabled
		/// </summary>
		public virtual void OnDisabled()
		{
		}


		/// <summary>
		/// called when this SceneComponent is removed from the Scene
		/// </summary>
		public virtual void OnRemovedFromScene()
		{
		}


		/// <summary>
		/// called each frame before the Entities are updated
		/// </summary>
		public virtual void Update()
		{
		}

		#endregion


		#region Fluent setters

		/// <summary>
		/// enables/disables this SceneComponent
		/// </summary>
		/// <returns>The enabled.</returns>
		/// <param name="isEnabled">If set to <c>true</c> is enabled.</param>
		public SceneComponent SetEnabled(bool isEnabled)
		{
			if (_enabled != isEnabled)
			{
				_enabled = isEnabled;

				if (_enabled)
					OnEnabled();
				else
					OnDisabled();
			}

			return this;
		}


		/// <summary>
		/// sets the updateOrder for the SceneComponent and triggers a sort of the SceneComponents
		/// </summary>
		/// <returns>The update order.</returns>
		/// <param name="updateOrder">Update order.</param>
		public SceneComponent SetUpdateOrder(int updateOrder)
		{
			if (UpdateOrder != updateOrder)
			{
				UpdateOrder = updateOrder;
				Core.Scene._sceneComponents.Sort();
			}

			return this;
		}

		#endregion


		int IComparable<SceneComponent>.CompareTo(SceneComponent other)
		{
			return UpdateOrder.CompareTo(other.UpdateOrder);
		}
	}
}