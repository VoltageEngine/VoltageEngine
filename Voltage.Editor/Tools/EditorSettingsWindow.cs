using ImGuiNET;
using Voltage.Editor.Persistence;
using Voltage.Editor.Scripting;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Tools
{
	/// <summary>
	/// Editor settings window for managing editor preferences
	/// </summary>
	public class EditorSettingsWindow
	{
		private bool _isOpen = false;
		
		// Scripts
		private static PersistentBool _autoCloseScriptProgress = new("EditorSettings_AutoCloseScriptProgress", true);
		private static PersistentBool _autoReloadSceneAfterScriptCompile= new("EditorSettings_AutoReloadSceneAfterScriptCompile", true);

		// Effects
		private static PersistentBool _autoCloseEffectsProgress = new("EditorSettings_AutoCloseEffectsProgress", true);

		public static bool AutoCloseScriptProgress
		{
			get => _autoCloseScriptProgress.Value;
			set => _autoCloseScriptProgress.Value = value;
		}
		
		public static bool AutoReloadSceneAfterScriptCompile
		{
			get => _autoReloadSceneAfterScriptCompile.Value;
			set => _autoReloadSceneAfterScriptCompile.Value = value;
		}

		public static bool AutoCloseEffectsProgress
		{
			get => _autoCloseEffectsProgress.Value;
			set => _autoCloseEffectsProgress.Value = value;
		}

		public bool IsOpen
		{
			get => _isOpen;
			set => _isOpen = value;
		}
		
		public void Draw()
		{
			if (!_isOpen)
				return;
			
			ImGui.SetNextWindowSize(new Num.Vector2(600, 400), ImGuiCond.FirstUseEver);
			
			bool open = _isOpen;
			if (ImGui.Begin("Editor Settings", ref open))
			{
				_isOpen = open;
				
				ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1.0f, 1.0f), "Editor Settings");
				ImGui.Separator();
				
				VoltageEditorUtils.MediumVerticalSpace();
				
				// Scripts tab
				if (ImGui.CollapsingHeader("Scripts", ImGuiTreeNodeFlags.DefaultOpen))
				{
					ImGui.Indent();
					
					bool compileOnStartup = ScriptManager.CompileOnStartup;
					if (ImGui.Checkbox("Compile Scripts on Startup", ref compileOnStartup))
					{
						ScriptManager.CompileOnStartup = compileOnStartup;
					}
					
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip("Automatically compile all scripts when the editor starts (requires restart to take effect)");
					}
					
					VoltageEditorUtils.SmallVerticalSpace();
					
					bool autoCloseScriptProgress = AutoCloseScriptProgress;
					if (ImGui.Checkbox("Close Progress Bar When Finished##Scripts", ref autoCloseScriptProgress))
					{
						AutoCloseScriptProgress = autoCloseScriptProgress;
					}
					
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip("Automatically close the script compilation progress window after 2 seconds when compilation completes");
					}

					VoltageEditorUtils.SmallVerticalSpace();

					bool autoReloadScene = AutoReloadSceneAfterScriptCompile;
					if (ImGui.Checkbox("Reload The Scene On Script Compilation End##Scripts", ref autoReloadScene))
					{
						AutoReloadSceneAfterScriptCompile = autoReloadScene;
					}

					ImGui.Unindent();
				}
				
				VoltageEditorUtils.MediumVerticalSpace();
				
				// Effects tab
				if (ImGui.CollapsingHeader("Effects", ImGuiTreeNodeFlags.DefaultOpen))
				{
					ImGui.Indent();
					
					bool autoCloseEffectsProgress = AutoCloseEffectsProgress;
					if (ImGui.Checkbox("Close Progress Bar When Finished##Effects", ref autoCloseEffectsProgress))
					{
						AutoCloseEffectsProgress = autoCloseEffectsProgress;
					}
					
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip("Automatically close the effects compilation progress window after 2 seconds when compilation completes");
					}
					
					ImGui.Unindent();
				}
				
				ImGui.End();
			}
			
			if (!open)
			{
				_isOpen = false;
			}
		}
	}
}