using System;
using System.IO;
using ImGuiNET;
using Voltage;
using Voltage.Editor.FilePickers;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Utils;
using Voltage.Utils.Extensions;

namespace Voltage.Editor.Inspectors.TypeInspectors
{
    /// <summary>
    /// Inspector for string fields/properties decorated with <see cref="FilePathAttribute"/>.
    /// Renders a read-only text display of the current path and a "Browse…" button that opens
    /// a <see cref="FilePicker"/> popup rooted at the active project's Content folder.
    /// The stored value is always a relative path from the project root.
    /// </summary>
    public class FilePathTypeInspector : AbstractTypeInspector
    {
        private string _popupId;
        private FilePathAttribute _filePathAttribute;

        public override void Initialize()
        {
            base.Initialize();
            _filePathAttribute = _memberInfo.GetAttribute<FilePathAttribute>();
            _popupId = $"FilePicker_{_scopeId}";
        }

        public override void DrawMutable()
        {
            var currentValue = GetValue<string>() ?? string.Empty;

            // Label column
            ImGui.Text(_name);
            ImGui.SameLine();

            // Show the current path as a selectable read-only input
            ImGui.SetNextItemWidth(-80);
            ImGui.InputText($"##{_scopeId}_path", ref currentValue, 512, ImGuiInputTextFlags.ReadOnly);

            ImGui.SameLine();

            if (ImGui.Button($"Browse##{_scopeId}"))
            {
                ImGui.OpenPopup(_popupId);

                var project = ProjectManager.Instance?.CurrentProject;
                var startPath = project != null && Directory.Exists(project.ProjectPath)
                    ? project.ProjectPath
                    : Environment.CurrentDirectory;

                // Re-create the picker each time the popup opens so the start path is fresh
                FilePicker.RemoveFilePicker(this);
                FilePicker.GetFilePicker(this, startPath, _filePathAttribute?.Filter);
            }

            HandleTooltip();

            // Draw the popup modal
            bool popupOpen = true;
            if (ImGui.BeginPopupModal(_popupId, ref popupOpen, ImGuiWindowFlags.NoTitleBar))
            {
                var picker = FilePicker.GetFilePicker(this, Environment.CurrentDirectory, _filePathAttribute?.Filter);
                picker.DontAllowTraverselBeyondRootFolder = true;

                if (picker.Draw())
                {
                    // Convert the absolute path to a relative path from the project root
                    var project = ProjectManager.Instance?.CurrentProject;
                    var relativePath = picker.SelectedFile;

                    if (project != null && !string.IsNullOrEmpty(relativePath))
                    {
                        try
                        {
                            relativePath = Path.GetRelativePath(project.ProjectPath, relativePath);
                        }
                        catch
                        {
                            Debug.Error($"Couldn't get the local path of the file: {picker.SelectedFile}");
                        }
                    }

                    SetValueWithUndo(relativePath, _name);
                    FilePicker.RemoveFilePicker(this);
                }

                ImGui.EndPopup();
            }
        }
    }
}