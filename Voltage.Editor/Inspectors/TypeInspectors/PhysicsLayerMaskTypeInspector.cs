using System.Collections.Generic;
using ImGuiNET;
using Voltage.Project;

namespace Voltage.Editor.Inspectors.TypeInspectors
{
	/// <summary>
	/// Renders a checklist for a physics layer bitmask (e.g. CollidesWithLayers).
	/// </summary>
	public class PhysicsLayerMaskTypeInspector : AbstractTypeInspector
	{
		private string[] _layerNames;
		private int[] _layerBitValues;

		public override void Initialize()
		{
			base.Initialize();
			RefreshLayers();
		}

		private void RefreshLayers()
		{
			var layers = ProjectSettings.Instance.Physics.PhysicsLayers;
			var names = new List<string>();
			var bits = new List<int>();

			foreach (var kvp in layers)
			{
				names.Add(kvp.Key);
				bits.Add(1 << kvp.Value);
			}

			_layerNames = names.ToArray();
			_layerBitValues = bits.ToArray();
		}

		public override void DrawMutable()
		{
			var currentMask = GetValue<int>();

			ImGui.Text(_name);
			ImGui.Indent();

			var newMask = currentMask;
			for (var i = 0; i < _layerNames.Length; i++)
			{
				var isChecked = currentMask == Physics.AllLayers || (currentMask & _layerBitValues[i]) != 0;
				if (ImGui.Checkbox(_layerNames[i], ref isChecked))
				{
					if (isChecked)
						newMask |= _layerBitValues[i];
					else
						newMask &= ~_layerBitValues[i];
				}
			}

			if (newMask != currentMask)
				SetValueWithUndo(newMask, _name);

			ImGui.Unindent();
			HandleTooltip();
		}
	}
}