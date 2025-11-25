using Voltage;
using Voltage.Editor.Core;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Inspectors.TypeInspectors;

/// <summary>
/// special Inspector that handles Entity references displaying a button that opens the inspector for the Entity
/// </summary>
public class EntityFieldInspector : AbstractTypeInspector
{
	public override void DrawMutable()
	{
		var entity = GetValue<Entity>();

		if (VoltageEditorUtils.LabelButton(_name, entity.Name))
			Voltage.Core.GetGlobalManager<ImGuiManager>().OpenSeparateEntityInspector(entity);
		HandleTooltip();
	}
}