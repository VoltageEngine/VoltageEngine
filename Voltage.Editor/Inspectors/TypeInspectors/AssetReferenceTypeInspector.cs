using System.IO;
using ImGuiNET;
using Voltage.Editor.Utils;
using Voltage.Editor.Windows;
using Voltage.Serialization;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors.TypeInspectors;

/// <summary>
/// Inspector for a <see cref="AssetReference"/> field. Renders a drop slot; drag any asset from the
/// Asset Browser onto it to assign it. The reference stores the asset's stable <c>.meta</c> GUID
/// (plus a project-relative hint path), so it survives renaming/moving the file.
/// </summary>
public class AssetReferenceTypeInspector : AbstractTypeInspector
{
	public override void DrawMutable()
	{
		var current = (AssetReference)_getter(_target);

		ImGuiSafe.TextSafe(_name);
		ImGui.SameLine();

		var label = current.IsValid
			? (current.AssetName ?? current.AssetPath)
			: "None (AssetReference)";

		var buttonColor = current.IsValid
			? new Num.Vector4(0.25f, 0.45f, 0.55f, 0.9f)
			: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);

		ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { W = 1f });
		ImGui.Button($"{label}##assetref_{_scopeId}", new Num.Vector2(-1, 0));
		ImGui.PopStyleColor(2);

		if (current.IsValid && ImGui.IsItemHovered())
		{
			ImGui.BeginTooltip();
			ImGuiSafe.TextSafe($"Path: {current.AssetPath}");
			ImGuiSafe.TextSafe($"GUID: {current.AssetGuid}");
			ImGui.EndTooltip();
		}
		else
		{
			HandleTooltip();
		}

		// Accept a drag from the Asset Browser (VOLTAGE_ASSET_REF). The dragged editor-side
		// AssetReference carries the GUID + project-relative hint path.
		if (ImGui.BeginDragDropTarget())
		{
			var payload = ImGui.AcceptDragDropPayload(AssetBrowserWindow.DragDropPayloadId);
			bool accepted;
			unsafe { accepted = payload.NativePtr != null; }

			if (accepted && !AssetBrowserWindow.DraggedReference.IsEmpty)
			{
				var dr = AssetBrowserWindow.DraggedReference;
				// Consume the pending drag so the viewport poller and Scene Graph drop target don't
				// also handle this drop and spawn a stray entity.
				AssetBrowserWindow.DraggedReference = Voltage.Editor.Assets.AssetReference.Empty;

				var assetRef = new AssetReference
				{
					AssetGuid = dr.Guid,
					AssetPath = dr.HintPath,
					AssetName = Path.GetFileNameWithoutExtension(dr.HintPath),
				};
				SetValueWithUndo(assetRef, $"Assign {_name}");
			}

			ImGui.EndDragDropTarget();
		}

		// Right-click: clear.
		if (current.IsValid && ImGui.BeginPopupContextItem($"assetref_ctx_{_scopeId}"))
		{
			if (ImGui.Selectable("Clear"))
				SetValueWithUndo(default(AssetReference), $"Clear {_name}");
			ImGui.EndPopup();
		}
	}
}
