using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Editor.Assets;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Windows;

/// <summary>
/// Asset Browser panel.
///
/// Shows project assets in a recursive directory tree that mirrors the on-disk
/// folder hierarchy under each project root.  Each file node shows a type icon,
/// supports drag-drop (VOLTAGE_ASSET_REF payload), double-click scene loading,
/// and hover tooltips.
///
/// Drag-drop behaviour:
///   - Assets whose <see cref="AssetTypeDescriptor.DropFactory"/> is non-null produce a
///     "VOLTAGE_ASSET_REF" drag payload.  The resolved <see cref="AssetReference"/>
///     (GUID + hint path) is stashed in <see cref="DraggedReference"/>.
///   - Assets with a null DropFactory show a "not supported" tooltip during drag and
///     produce no usable payload.
///   - Double-clicking a .vscene row loads that scene.
///
/// OS file-drop (Item 1):
///   Files dragged from the OS file manager are captured via SDL2 SDL_DROPFILE events
///   through <see cref="SdlFileDropWatcher"/>.  Dropped paths are queued on the SDL pump
///   thread and processed each frame in <see cref="Draw"/> on the main thread, targeting
///   <see cref="_hoveredFolderPath"/> or <c>ContentsFolder</c> as the fallback.
///
/// File operations (Item 2):
///   Right-click a file row for Copy / Paste / Duplicate / Delete.
///   Keyboard: Ctrl+C / Ctrl+V / Ctrl+D / Delete while the browser is focused.
///   - Copy remembers the source absolute path in an internal clipboard.
///   - Paste copies into the currently hovered/selected folder (collision-safe).
///   - Duplicate copies in-place with a " (1)" suffix.
///   - Delete removes the file AND its .meta sidecar; shows a confirmation popup.
///   Duplicated/pasted files always get a NEW GUID via Refresh() / GetOrCreateGuid().
///
/// Persistence:
///   Expanded/collapsed tree node state is saved under the key
///   <c>AssetBrowser.CollapsedNodes</c> as a pipe-separated list of
///   project-relative node paths, restored on construction.
/// </summary>
public class AssetBrowserWindow : IDisposable
{
    /// <summary>ImGui drag-drop payload identifier for asset drags from the Asset Browser.</summary>
    public const string DragDropPayloadId = "VOLTAGE_ASSET_REF";

    /// <summary>
    /// Shared slot: the Asset Browser deposits the <see cref="AssetReference"/> here when a
    /// drag starts.  The drop target (<see cref="SceneGraphWindow"/>) reads this slot on
    /// <c>AcceptDragDropPayload</c>.
    /// </summary>
    public static AssetReference DraggedReference;

    private const string WindowTitle     = "Asset Browser###AssetBrowserWindow";
    private const float  IconSize        = 20f;
    private const float  IconTextSpacing = 6f;

    // Separator used to join/split collapsed-node paths in the persistent string.
    private const char CollapsedNodeSep = '|';
    private const string CollapsedNodeKey = "AssetBrowser.CollapsedNodes";

    public bool IsOpen { get; set; } = true;

    private readonly AssetDatabase _db;
    private string _searchFilter = string.Empty;

    // Persistent collapsed-node state.
    // Key: project-relative node path (e.g. "Content/Sprites").
    // A node is in this set when it is COLLAPSED (same semantics as before —
    // tree nodes start open, user can close them).
    private readonly HashSet<string> _collapsedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly PersistentString _collapsedNodesSetting;

    // Track which folder node the mouse is currently hovering over, per-frame.
    // Used as the target for SDL file-drops and file-paste operations.
    private string _hoveredFolderPath = null;

    private readonly SdlFileDropWatcher _sdlDropWatcher;


    // Internal clipboard: absolute paths of the files to be pasted.
    private readonly List<string> _copiedPaths = new();

    // Currently selected file (single-select for now).
    private string _selectedFilePath = null;

    // Delete confirmation state.
    private bool _showDeleteConfirmation = false;
    private string _fileToDelete = null;

    public AssetBrowserWindow()
    {
        _db = new AssetDatabase();
        // Register as the singleton so DropHandlers and other systems can access it.
        AssetDatabase.Instance = _db;

        // Restore persisted collapse state.
        _collapsedNodesSetting = new PersistentString(CollapsedNodeKey, string.Empty);
        LoadCollapsedState();

        // Item 1 — install the SDL2 file-drop watcher (cross-platform).
        _sdlDropWatcher = new SdlFileDropWatcher();
    }

    /// <summary>Called when a new project is loaded so the index reflects the current project.</summary>
    public void OnProjectLoaded()
    {
        // Reload persisted collapse state with the new project context.
        LoadCollapsedState();
        _db.Refresh();
    }

    /// <summary>Called when the active project is unloaded.</summary>
    public void OnProjectUnloaded()
    {
        _db.Refresh();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _sdlDropWatcher.Dispose();
        _db.Dispose();
        if (AssetDatabase.Instance == _db)
            AssetDatabase.Instance = null;
    }

    /// <summary>
    /// Draws the Asset Browser ImGui window.
    /// Call once per frame from the ImGui draw loop.
    /// </summary>
    public void Draw()
    {
        if (!IsOpen)
            return;

        if (!_db.IsIndexed)
            _db.Refresh();

        // Must run on the main thread, which Draw() guarantees.
        DrainSdlFileDrops();

        var open = IsOpen;
        ImGui.Begin(WindowTitle, ref open);
        IsOpen = open;

        if (!IsOpen)
        {
            ImGui.End();
            return;
        }

        DrawToolbar();
        ImGui.Separator();

        // Reset the hovered-folder tracker each frame before drawing the tree.
        _hoveredFolderPath = null;

        if (!ProjectManager.Instance.HasActiveProject)
        {
            ImGui.TextColored(new Num.Vector4(0.6f, 0.6f, 0.6f, 1f), "No project loaded.");
        }
        else if (_db.RootNodes.Count == 0)
        {
            ImGui.TextColored(new Num.Vector4(0.6f, 0.6f, 0.6f, 1f), "No assets found. Press Refresh.");
        }
        else
        {
            DrawTree();
        }

        HandleKeyboardShortcuts();

        ImGui.End();

        // Must be outside Begin/End to be modal.
        DrawDeleteConfirmationPopup();
    }

    /// <summary>
    /// Dequeues SDL DROPFILE paths and copies them into the project.
    /// Target folder = <see cref="_hoveredFolderPath"/> (updated each frame by the tree
    /// renderer); if not hovered over the browser or null, falls back to
    /// <c>project.ContentsFolder</c>.
    /// </summary>
    private void DrainSdlFileDrops()
    {
        if (!_db.IsIndexed)
            return;

        _sdlDropWatcher.DrainPending(sourcePath =>
        {
            if (!File.Exists(sourcePath))
            {
                EditorDebug.Log($"AssetBrowser: SDL drop path '{sourcePath}' is not a file — skipped.", "AssetBrowser");
                return;
            }

            // Determine target directory.
            string targetDir = _hoveredFolderPath;
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                var project = ProjectManager.Instance?.CurrentProject;
                if (project == null)
                {
                    EditorDebug.Log("AssetBrowser: SDL drop received but no project loaded — ignoring.", "AssetBrowser");
                    return;
                }
                targetDir = project.ContentsFolder;
            }

            try
            {
                CopyFileCollisionSafe(sourcePath, targetDir);
            }
            catch (Exception ex)
            {
                EditorDebug.Log($"AssetBrowser: failed to copy '{sourcePath}' → '{targetDir}': {ex.Message}", "AssetBrowser");
            }

            _db.Refresh();
        });
    }

    private void DrawToolbar()
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 80f);
        ImGui.InputTextWithHint("##AssetSearch", "Search...", ref _searchFilter, 128);

        ImGui.SameLine();

        if (ImGui.Button("Refresh"))
            _db.Refresh();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Rescan all project folders and rebuild the asset index.");
    }

    private void DrawTree()
    {
        foreach (var root in _db.RootNodes)
        {
            ImGui.PushID(root.RelativePath);
            DrawFolderNode(root);
            ImGui.PopID();
            VoltageEditorUtils.SmallVerticalSpace();
        }
    }

    /// <summary>
    /// Recursively renders a folder node and its children.
    /// </summary>
    private void DrawFolderNode(AssetFolderNode node)
    {
        bool hasSearchFilter = !string.IsNullOrWhiteSpace(_searchFilter);

        // When searching, skip folders that have no matching descendants.
        if (hasSearchFilter && !FolderHasMatchingItems(node))
            return;

        bool isCollapsed = _collapsedNodes.Contains(node.RelativePath);

        // Build tree node flags: open by default unless explicitly collapsed.
        var flags = ImGuiTreeNodeFlags.OpenOnArrow
                  | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (!isCollapsed)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool nodeOpen = ImGui.TreeNodeEx(node.Label, flags);

        // Track hover for SDL file-drop targeting.
        if (ImGui.IsItemHovered())
            _hoveredFolderPath = node.AbsolutePath;

        // Toggle persistence whenever the user opens or closes the node.
        if (nodeOpen && isCollapsed)
        {
            _collapsedNodes.Remove(node.RelativePath);
            SaveCollapsedState();
        }
        else if (!nodeOpen && !isCollapsed)
        {
            _collapsedNodes.Add(node.RelativePath);
            SaveCollapsedState();
        }

        if (nodeOpen)
        {
            // Recurse into child folders first (alphabetical).
            foreach (var child in node.ChildFolders)
            {
                ImGui.PushID(child.RelativePath);
                DrawFolderNode(child);
                ImGui.PopID();
            }

            // Then draw file leaves.
            for (int i = 0; i < node.Files.Count; i++)
            {
                var item = node.Files[i];

                if (hasSearchFilter &&
                    !item.FileName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.PushID(i);
                DrawAssetRow(item, node.AbsolutePath);
                ImGui.PopID();
            }

            ImGui.TreePop();
        }
    }

    /// <summary>Returns true if <paramref name="node"/> or any descendant has a file
    /// whose name matches the current search filter.</summary>
    private bool FolderHasMatchingItems(AssetFolderNode node)
    {
        foreach (var f in node.Files)
        {
            if (f.FileName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var child in node.ChildFolders)
        {
            if (FolderHasMatchingItems(child))
                return true;
        }

        return false;
    }

    private void DrawAssetRow(AssetItem item, string parentFolderPath)
    {
        var iconId = AssetBrowserIcons.GetIconId(item.Descriptor.Kind);

        if (iconId != IntPtr.Zero)
        {
            ImGui.Image(iconId, new Num.Vector2(IconSize, IconSize));
            ImGui.SameLine(0, IconTextSpacing);
        }

        bool isSelected = string.Equals(_selectedFilePath, item.AbsolutePath, StringComparison.OrdinalIgnoreCase);

        bool clicked = ImGui.Selectable(item.FileName, isSelected,
            ImGuiSelectableFlags.AllowDoubleClick,
            new Num.Vector2(ImGui.GetContentRegionAvail().X, IconSize));

        // Track hover on file rows — the parent folder is still the drop destination.
        if (ImGui.IsItemHovered())
            _hoveredFolderPath = parentFolderPath;

        if (clicked)
            _selectedFilePath = item.AbsolutePath;

        if (clicked && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (item.Descriptor.Kind == AssetKind.Scene)
            {
                var reference = _db.GetReference(item.AbsolutePath);
                DropHandlers.DropScene(reference);
            }
        }

        // Full-path + GUID tooltip on hover.
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextColored(new Num.Vector4(0.7f, 0.9f, 1f, 1f), item.Descriptor.Kind.ToString());
            ImGui.Text(item.AbsolutePath);

            if (AssetDatabase.Instance != null)
            {
                var guid = AssetDatabase.Instance.GetOrCreateGuid(item.AbsolutePath);
                if (guid != Guid.Empty)
                    ImGui.TextColored(new Num.Vector4(0.5f, 0.8f, 0.5f, 1f), guid.ToString());
            }

            ImGui.EndTooltip();
        }

        if (ImGui.BeginPopupContextItem($"##ctx_{item.FileName}"))
        {
            _selectedFilePath = item.AbsolutePath; // ensure selection follows right-click

            if (ImGui.MenuItem("Copy"))
                CopyToInternalClipboard(item.AbsolutePath);

            bool hasCopied = _copiedPaths.Count > 0;
            if (!hasCopied)
                ImGui.BeginDisabled();

            if (ImGui.MenuItem("Paste"))
                PasteFiles(parentFolderPath);

            if (!hasCopied)
                ImGui.EndDisabled();

            if (ImGui.MenuItem("Duplicate"))
                DuplicateFile(item.AbsolutePath);

            ImGui.Separator();

            if (ImGui.MenuItem("Delete"))
                RequestDelete(item.AbsolutePath);

            ImGui.EndPopup();
        }

        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            if (item.Descriptor.DropFactory != null)
            {
                var reference = _db.GetReference(item.AbsolutePath);
                DraggedReference = reference;

                unsafe
                {
                    byte sentinel = 0;
                    ImGui.SetDragDropPayload(DragDropPayloadId, (IntPtr)(&sentinel), 1);
                }

                ImGui.Text(item.FileName);
            }
            else
            {
                ImGui.TextColored(new Num.Vector4(1f, 0.5f, 0.3f, 1f), item.FileName);
                ImGui.SetTooltip("File type is not supported by Drag-N-Drop");
            }

            ImGui.EndDragDropSource();
        }
    }

    private void HandleKeyboardShortcuts()
    {
        // Only handle shortcuts when this window has focus.
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        bool ctrl = ImGui.GetIO().KeyCtrl;

        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.C, false) && _selectedFilePath != null)
            CopyToInternalClipboard(_selectedFilePath);

        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.V, false) && _copiedPaths.Count > 0)
        {
            string targetDir = _hoveredFolderPath;
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                var project = ProjectManager.Instance?.CurrentProject;
                targetDir = project?.ContentsFolder;
            }
            if (!string.IsNullOrEmpty(targetDir))
                PasteFiles(targetDir);
        }

        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D, false) && _selectedFilePath != null)
            DuplicateFile(_selectedFilePath);

        if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) && _selectedFilePath != null)
            RequestDelete(_selectedFilePath);
    }

    /// <summary>Copies the path to the internal clipboard (does NOT touch the OS clipboard).</summary>
    private void CopyToInternalClipboard(string absolutePath)
    {
        _copiedPaths.Clear();
        _copiedPaths.Add(absolutePath);
        EditorDebug.Log($"AssetBrowser: copied '{Path.GetFileName(absolutePath)}' to internal clipboard.", "AssetBrowser");
    }

    /// <summary>
    /// Pastes all internally-copied files into <paramref name="targetDir"/>.
    /// Each pasted file gets a NEW GUID via Refresh/GetOrCreateGuid — the source .meta is NOT copied.
    /// </summary>
    private void PasteFiles(string targetDir)
    {
        if (_copiedPaths.Count == 0 || string.IsNullOrEmpty(targetDir))
            return;

        bool anyPasted = false;
        foreach (var sourcePath in _copiedPaths)
        {
            if (!File.Exists(sourcePath))
            {
                EditorDebug.Log($"AssetBrowser: paste source '{sourcePath}' no longer exists — skipped.", "AssetBrowser");
                continue;
            }

            try
            {
                CopyFileCollisionSafe(sourcePath, targetDir);
                anyPasted = true;
            }
            catch (Exception ex)
            {
                EditorDebug.Log($"AssetBrowser: paste failed for '{sourcePath}': {ex.Message}", "AssetBrowser");
            }
        }

        if (anyPasted)
            _db.Refresh();
    }

    /// <summary>
    /// Duplicates a file in its own folder with a " (1)" suffix (or " (2)", etc.).
    /// The duplicate gets a NEW GUID — the .meta sidecar is NOT copied.
    /// </summary>
    private void DuplicateFile(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            EditorDebug.Log($"AssetBrowser: cannot duplicate '{absolutePath}' — file not found.", "AssetBrowser");
            return;
        }

        try
        {
            string dir = Path.GetDirectoryName(absolutePath)!;
            CopyFileCollisionSafe(absolutePath, dir);
            _db.Refresh();
        }
        catch (Exception ex)
        {
            EditorDebug.Log($"AssetBrowser: duplicate failed for '{absolutePath}': {ex.Message}", "AssetBrowser");
        }
    }

    /// <summary>Triggers the delete confirmation popup for the given file.</summary>
    private void RequestDelete(string absolutePath)
    {
        _fileToDelete = absolutePath;
        _showDeleteConfirmation = true;
    }

    /// <summary>
    /// Draws the modal delete-confirmation popup (mirrors the pattern in SceneGraphWindow).
    /// Deletes the file AND its .meta sidecar on confirmation.
    /// </summary>
    private void DrawDeleteConfirmationPopup()
    {
        if (_showDeleteConfirmation)
        {
            ImGui.OpenPopup("asset-delete-confirmation");
            _showDeleteConfirmation = false;
        }

        var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));

        bool open = true;
        if (ImGui.BeginPopupModal("asset-delete-confirmation", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Delete Asset");
            ImGui.Separator();

            string fileName = _fileToDelete != null ? Path.GetFileName(_fileToDelete) : string.Empty;
            ImGui.TextWrapped($"Are you sure you want to delete '{fileName}'?");
            ImGui.TextColored(new Num.Vector4(1f, 0.6f, 0.2f, 1f), "This action cannot be undone!");

            VoltageEditorUtils.MediumVerticalSpace();

            float buttonWidth  = 80f;
            float spacing      = 10f;
            float totalWidth   = buttonWidth * 2 + spacing;
            float centerStart  = (ImGui.GetWindowSize().X - totalWidth) * 0.5f;
            ImGui.SetCursorPosX(centerStart);

            if (ImGui.Button("Yes", new Num.Vector2(buttonWidth, 0)))
            {
                ExecuteDelete(_fileToDelete);
                _fileToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("No", new Num.Vector2(buttonWidth, 0)))
            {
                _fileToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Deletes a file and its companion .meta sidecar.
    /// Clears the selection and refreshes the index afterwards.
    /// </summary>
    private void ExecuteDelete(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return;

        try
        {
            if (File.Exists(absolutePath))
                File.Delete(absolutePath);

            // Remove the .meta sidecar so no orphan GUIDs clutter the project.
            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            EditorDebug.Log($"AssetBrowser: deleted '{Path.GetFileName(absolutePath)}'.", "AssetBrowser");

            if (string.Equals(_selectedFilePath, absolutePath, StringComparison.OrdinalIgnoreCase))
                _selectedFilePath = null;

            _db.Refresh();
        }
        catch (Exception ex)
        {
            EditorDebug.Log($"AssetBrowser: delete failed for '{absolutePath}': {ex.Message}", "AssetBrowser");
        }
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="targetDir"/>,
    /// resolving name collisions by appending " (1)", " (2)", … without silent overwrite.
    /// Does NOT copy the .meta sidecar — new assets always get a fresh GUID via Refresh.
    /// </summary>
    private static void CopyFileCollisionSafe(string sourcePath, string targetDir)
    {
        if (!File.Exists(sourcePath))
        {
            EditorDebug.Log($"AssetBrowser: source '{sourcePath}' is not a file — skipped.", "AssetBrowser");
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string ext      = Path.GetExtension(sourcePath);
        string destPath = Path.Combine(targetDir, baseName + ext);

        // Resolve name collisions without silent overwrite.
        if (File.Exists(destPath))
        {
            int n = 1;
            do
            {
                destPath = Path.Combine(targetDir, $"{baseName} ({n}){ext}");
                n++;
            }
            while (File.Exists(destPath));
        }

        File.Copy(sourcePath, destPath, overwrite: false);
        EditorDebug.Log($"AssetBrowser: copied '{Path.GetFileName(sourcePath)}' → '{destPath}'", "AssetBrowser");
    }

    private void LoadCollapsedState()
    {
        _collapsedNodes.Clear();
        var stored = _collapsedNodesSetting.Value;
        if (string.IsNullOrEmpty(stored))
            return;

        foreach (var part in stored.Split(CollapsedNodeSep, StringSplitOptions.RemoveEmptyEntries))
            _collapsedNodes.Add(part);
    }

    private void SaveCollapsedState()
    {
        _collapsedNodesSetting.Value = string.Join(CollapsedNodeSep, _collapsedNodes);
    }
}
