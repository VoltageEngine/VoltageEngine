using System;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Assets
{
    /// <summary>
    /// Thin helper that maps an <see cref="AssetKind"/> to the corresponding ImGui texture IntPtr
    /// loaded by <see cref="ImguiImageLoader"/>.
    ///
    /// Phase-2 note: if new <see cref="AssetKind"/> values are added (e.g. <c>Audio</c>),
    /// add a field to <see cref="ImguiImageLoader"/> and a case here.
    /// </summary>
    public static class AssetBrowserIcons
    {
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
    }
}
