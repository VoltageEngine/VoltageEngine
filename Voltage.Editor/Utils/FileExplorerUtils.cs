using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Utils
{
	/// <summary>Opens the OS file manager at a path: Windows Explorer, macOS Finder, or the Linux default.</summary>
	public static class FileExplorerUtils
	{
		/// <summary>
		/// Shows <paramref name="absolutePath"/> in the OS file manager - a file is revealed with its folder open
		/// and the file highlighted, a folder is opened directly. A path that no longer exists falls back to the
		/// nearest surviving ancestor, so the menu item never silently does nothing.
		/// </summary>
		public static void Reveal(string absolutePath)
		{
			if (string.IsNullOrWhiteSpace(absolutePath))
				return;

			var isDirectory = Directory.Exists(absolutePath);

			if (!isDirectory && !File.Exists(absolutePath))
			{
				var parent = Path.GetDirectoryName(absolutePath);
				while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
					parent = Path.GetDirectoryName(parent);

				if (string.IsNullOrEmpty(parent))
				{
					EditorDebug.Log($"Open In File Explorer: '{absolutePath}' no longer exists.", "AssetBrowser");
					return;
				}

				absolutePath = parent;
				isDirectory = true;
			}

			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					RevealWindows(absolutePath, isDirectory);
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					RevealMac(absolutePath, isDirectory);
				else
					RevealLinux(absolutePath, isDirectory);
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"Open In File Explorer: could not open '{absolutePath}': {ex.Message}", "AssetBrowser");
			}
		}

		// explorer.exe parses its own command line rather than taking argv, so /select, needs the quoted path
		// glued straight after the comma - ArgumentList would quote it as one token and open the wrong thing.
		private static void RevealWindows(string path, bool isDirectory)
		{
			var arguments = isDirectory ? $"\"{path}\"" : $"/select,\"{path}\"";

			Start(new ProcessStartInfo("explorer.exe", arguments)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
			});
		}

		private static void RevealMac(string path, bool isDirectory)
		{
			var info = new ProcessStartInfo("open")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			// -R reveals the file inside its folder; a folder is just opened.
			if (!isDirectory)
				info.ArgumentList.Add("-R");

			info.ArgumentList.Add(path);
			Start(info);
		}

		private static void RevealLinux(string path, bool isDirectory)
		{
			// Linux has no portable "reveal". The freedesktop FileManager1 interface does it on Nautilus, Dolphin
			// and Nemo, so try that for files and settle for opening the containing folder when it is unavailable.
			if (!isDirectory && TryShowItemViaDBus(path))
				return;

			var folder = isDirectory ? path : Path.GetDirectoryName(path);
			if (string.IsNullOrEmpty(folder))
				return;

			var info = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			info.ArgumentList.Add(folder);
			Start(info);
		}

		private static bool TryShowItemViaDBus(string path)
		{
			try
			{
				var info = new ProcessStartInfo("dbus-send")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				};

				info.ArgumentList.Add("--session");
				info.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
				info.ArgumentList.Add("--type=method_call");
				info.ArgumentList.Add("/org/freedesktop/FileManager1");
				info.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
				info.ArgumentList.Add($"array:string:{new Uri(path).AbsoluteUri}");
				info.ArgumentList.Add("string:");

				using var process = Process.Start(info);
				if (process == null)
					return false;

				// Blocks the UI briefly, but only on Linux and only long enough to learn whether a file manager
				// answered; treat a hanging call as success rather than opening a second window on top of it.
				if (!process.WaitForExit(1000))
					return true;

				return process.ExitCode == 0;
			}
			catch
			{
				return false;
			}
		}

		private static void Start(ProcessStartInfo info)
		{
			using var process = Process.Start(info);
		}
	}
}
