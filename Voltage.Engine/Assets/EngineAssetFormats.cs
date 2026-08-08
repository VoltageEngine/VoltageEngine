using System.Runtime.CompilerServices;

namespace Voltage.Assets
{
	/// <summary>
	/// Installs the built-in formats from a <c>[ModuleInitializer]</c>, so the registry is populated before any engine or game code runs rather than depending on who touches an IO class first.
	/// </summary>
	internal static class EngineAssetFormats
	{
		[ModuleInitializer]
		internal static void Install() => AssetFileRegistry.EnsureEngineFormatsRegistered();

		/// <summary>Do not call directly — going through the registry is what keeps it idempotent.</summary>
		internal static void RegisterAll()
		{
			AssetFileRegistry.Register(Voltage.Tilesets.TilesetAssetIO.Format);
			AssetFileRegistry.Register(Voltage.Cinematics.TimelineAssetIO.Format);
			AssetFileRegistry.Register(Voltage.Data.DataAssetIO.Format);
		}
	}
}
