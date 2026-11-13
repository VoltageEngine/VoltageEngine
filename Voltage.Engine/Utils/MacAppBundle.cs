using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Voltage
{
	/// <summary>
	/// A game published as a macOS .app keeps its files in Contents/Resources, so <see cref="AppContext.BaseDirectory"/>
	/// is pointed there. MonoGame's TitleContainer already looks there, and its libraries load from Contents/Frameworks.
	/// </summary>
	internal static class MacAppBundle
	{
		[ModuleInitializer]
		internal static void UseBundleResources()
		{
			if (!OperatingSystem.IsMacOS())
				return;

			var executableDir = AppContext.BaseDirectory.TrimEnd('/');
			if (!executableDir.EndsWith("/Contents/MacOS", StringComparison.Ordinal))
				return;

			var resources = Path.Combine(Path.GetDirectoryName(executableDir)!, "Resources");
			if (File.Exists(Path.Combine(resources, "ProjectSettings.json")))
				AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", resources + "/");
		}
	}
}
