using System.Runtime.CompilerServices;

namespace Voltage.Editor
{
	/// <summary>
	/// Installs the SDL2 native-library resolver for the editor assembly so direct
	/// "SDL2.dll" P/Invokes (e.g. <see cref="Assets.SdlFileDropWatcher"/>) resolve to the
	/// correct platform library on Linux/macOS. See <see cref="Voltage.SdlNative"/>.
	/// </summary>
	internal static class SdlNativeInit
	{
		[ModuleInitializer]
		internal static void Register() => Voltage.SdlNative.Register(typeof(SdlNativeInit).Assembly);
	}
}
