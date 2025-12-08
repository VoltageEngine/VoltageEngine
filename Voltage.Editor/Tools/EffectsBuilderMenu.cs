using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Voltage.Editor.Utils;
using Voltage.Svg;

namespace Voltage.Editor.Tools
{
	public class EffectsBuilderMenu
	{
		public static void RenderToolsMenu()
		{
			if (!ImGui.BeginMenu("Tools"))
			{
				return;
			}

			if (ImGui.BeginMenu("Build"))
			{
				if (ImGui.MenuItem("Build Effects"))
				{
					BuildEffects();
				}
				ImGui.EndMenu();
			}

			ImGui.EndMenu();
		}

		private static void BuildEffects()
		{
			try
			{
				var projectDir = FindProjectDir();
				var shaderSrcDir = Path.Combine(projectDir, "DefaultContent");
				var shaderOutDir = Path.Combine(projectDir, "Content", "Effects");

				if (!Directory.Exists(shaderSrcDir))
				{
					NotificationSystem.ShowTimedNotification($"Build Effects: source folder not found: {shaderSrcDir}");
					return;
				}

				Directory.CreateDirectory(shaderOutDir);

				var fxFiles = Directory.GetFiles(shaderSrcDir, "*.fx", SearchOption.AllDirectories);
				if (fxFiles.Length == 0)
				{
					NotificationSystem.ShowTimedNotification("Build Effects: no .fx files found");
					return;
				}

				var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "mgfxc.exe" : "mgfxc";
				var sbSummary = new StringBuilder();
				int successCount = 0, failCount = 0;

				foreach (var fx in fxFiles)
				{
					var relative = Path.GetRelativePath(shaderSrcDir, fx);
					var filename = Path.GetFileNameWithoutExtension(fx);
					var outFile = Path.Combine(shaderOutDir, $"{filename}.mgfxo"); // matches original mgfxo extension
					Directory.CreateDirectory(Path.GetDirectoryName(outFile) ?? shaderOutDir);

					var startInfo = new ProcessStartInfo
					{
						FileName = exeName,
						Arguments = $"\"{fx}\" \"{outFile}\"",
						CreateNoWindow = true,
						UseShellExecute = false,
						RedirectStandardOutput = true,
						RedirectStandardError = true
					};

					try
					{
						using var proc = Process.Start(startInfo);
						if (proc == null)
						{
							sbSummary.AppendLine($"{relative}: failed to start process");
							failCount++;
							continue;
						}

						var stdout = proc.StandardOutput.ReadToEnd();
						var stderr = proc.StandardError.ReadToEnd();
						proc.WaitForExit();

						if (proc.ExitCode == 0)
						{
							successCount++;
							sbSummary.AppendLine($"{relative}: OK");
						}
						else
						{
							failCount++;
							sbSummary.AppendLine($"{relative}: FAILED (exit {proc.ExitCode})");
							if (!string.IsNullOrWhiteSpace(stdout))
								sbSummary.AppendLine($"OUT: {stdout}");
							if (!string.IsNullOrWhiteSpace(stderr))
								sbSummary.AppendLine($"ERR: {stderr}");
						}
					}
					catch (Exception ex)
					{
						failCount++;
						sbSummary.AppendLine($"{relative}: Exception: {ex.Message}");
					}
				}

				NotificationSystem.ShowTimedNotification($"Build Effects finished. Success: {successCount}, Failed: {failCount}");
#if DEBUG
				Debug.Info(sbSummary.ToString());
#endif
			}
			catch (Exception ex)
			{
				NotificationSystem.ShowTimedNotification($"Build Effects error: {ex.Message}");
			}
		}

		private static string FindProjectDir()
		{
			var dir = AppContext.BaseDirectory;
			var di = new DirectoryInfo(dir);
			while (di != null)
			{
				if (File.Exists(Path.Combine(di.FullName, "Voltage.Editor.csproj")))
					return di.FullName;
				di = di.Parent;
			}

			// fallback to base directory
			return AppContext.BaseDirectory;
		}
	}
}
