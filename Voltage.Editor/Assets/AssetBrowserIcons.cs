using System;
using System.Collections.Generic;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Assets
{
    /// <summary>
    /// Resolves an asset's browser icon.
    ///
    /// <para>Built-in types map by <see cref="AssetKind"/> to textures preloaded by
    /// <see cref="ImguiImageLoader"/>. A descriptor may instead supply its own
    /// <see cref="AssetTypeDescriptor.IconPath"/> — how a plugin gives its file type a real icon rather
    /// than the generic fallback. Those are bound on first use and cached, since binding a texture is not
    /// something to do per frame per row.</para>
    /// </summary>
    public static class AssetBrowserIcons
    {
        private static readonly Dictionary<string, IntPtr> _customIcons = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns the ImGui texture pointer for the given <paramref name="kind"/>.</summary>
        public static IntPtr GetIconId(AssetKind kind) => kind switch
        {
            AssetKind.Texture     => ImguiImageLoader.AssetIconTexture,
            AssetKind.Prefab      => ImguiImageLoader.AssetIconPrefab,
            AssetKind.Scene       => ImguiImageLoader.AssetIconScene,
            AssetKind.Script      => ImguiImageLoader.AssetIconScript,
			AssetKind.Audio       => ImguiImageLoader.AssetIconAudio,
			AssetKind.Tileset     => ImguiImageLoader.AssetIconTileset,
			AssetKind.Effect      => ImguiImageLoader.AssetIconUnsupported,
            _                     => ImguiImageLoader.AssetIconUnsupported,
        };

        /// <summary>
        /// Icon for a descriptor: its own <see cref="AssetTypeDescriptor.IconPath"/> when that resolves, otherwise the kind's built-in icon.
        /// </summary>
        public static IntPtr GetIconId(AssetTypeDescriptor descriptor)
        {
            if (descriptor == null)
                return ImguiImageLoader.AssetIconUnsupported;

            var custom = ResolveCustom(descriptor.IconPath);
            return custom != IntPtr.Zero ? custom : GetIconId(descriptor.Kind);
        }

        private static IntPtr ResolveCustom(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath))
                return IntPtr.Zero;

            if (_customIcons.TryGetValue(iconPath, out var cached))
                return cached;

            var bound = IntPtr.Zero;
            try
            {
                var manager = Core.GetGlobalManager<ImGuiManager>();
                var texture = manager != null ? Core.Content.LoadTexture(iconPath) : null;
                if (texture != null)
                    bound = manager.BindTexture(texture);
            }
            catch (Exception ex)
            {
                EditorDebug.Log($"AssetBrowserIcons: could not load icon '{iconPath}': {ex.Message}", "AssetBrowser");
            }

            _customIcons[iconPath] = bound;
            return bound;
        }

        /// <summary>Drops cached icons, e.g. after plugins are reloaded and their content folders moved.</summary>
        public static void ClearCache() => _customIcons.Clear();
    }
}
