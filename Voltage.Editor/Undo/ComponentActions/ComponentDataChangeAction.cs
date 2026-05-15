using System;
using Voltage;
using Voltage.Editor.Undo.Core;
using Voltage.Persistence;
using Voltage.Serialization;

namespace Voltage.Editor.Undo.ComponentActions
{
	public class ComponentDataChangeAction : EditorChangeTracker.IEditorAction
	{
		private readonly Component _component;
		private readonly ComponentData _oldData;
		private readonly ComponentData _newData;
		private readonly string _description;

		public string Description => _description;

		public ComponentDataChangeAction(Component component, ComponentData oldData, ComponentData newData, string description)
		{
			_component = component;
			_oldData = CloneComponentData(oldData);
			_newData = CloneComponentData(newData);
			_description = description;
		}

		private ComponentData CloneComponentData(ComponentData data)
		{
			if (data == null) return null;

			try
			{
				// Use JSON serialization for reliable cloning
				var jsonSettings = new JsonSettings
				{
					PrettyPrint = false,
					TypeNameHandling = TypeNameHandling.Auto,
					PreserveReferencesHandling = false
				};

				var json = Json.ToJson(data, jsonSettings);
				return (ComponentData)Json.FromJson(json, data.GetType());
			}
			catch
			{
				// If JSON fails, try ICloneable
				if (data is ICloneable cloneable)
					return (ComponentData)cloneable.Clone();

				return data;
			}
		}

		public void Undo()
		{
			if (_component == null || _oldData == null)
				return;

			ApplyData(CloneComponentData(_oldData));
		}

		public void Redo()
		{
			if (_component == null || _newData == null)
				return;

			ApplyData(CloneComponentData(_newData));
		}

		/// <summary>
		/// Applies <paramref name="cloned"/> to the component and re-wires any
		/// Entity/Component reference fields that the generated Data setter skips.
		/// </summary>
		private void ApplyData(ComponentData cloned)
		{
			// Must be set BEFORE "_component.Data = cloned" to not lose data during copying
			_component._pendingLoadedData = cloned;
			_component.Data = cloned;

			if (Voltage.Core.Scene != null)
				ComponentReferenceResolver.ReResolveComponent(_component, Voltage.Core.Scene);
		}
	}
}