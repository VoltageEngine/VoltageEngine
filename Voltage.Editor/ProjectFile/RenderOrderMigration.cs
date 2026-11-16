using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Voltage.Editor.DebugUtils;
using Voltage.Project;

namespace Voltage.Editor.ProjectFile
{
	/// <summary>Rewrites a project saved when lower render layers drew in front, negating every stored layer so nothing moves on screen.</summary>
	public static class RenderOrderMigration
	{
		// Matches the key both in plain JSON and inside the escaped component JSON strings scenes embed.
		private static readonly Regex StoredLayer = new(@"(""RenderLayer\\?""\s*:\s*)(-?\d+)", RegexOptions.Compiled);
		private static readonly string[] Extensions = { ".vscene", ".vprefab", ".vasset" };
		private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "EngineLibs", "PluginLibs", "node_modules", "Backups" };

		/// <summary>The scene, prefab and data asset text with every stored RenderLayer negated, and how many were changed.</summary>
		public static string FlipStoredLayers(string text, out int count)
		{
			var flipped = 0;
			var result = StoredLayer.Replace(text, m =>
			{
				if (!long.TryParse(m.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
					return m.Value;
				flipped++;
				return m.Groups[1].Value + (-value).ToString(CultureInfo.InvariantCulture);
			});
			count = flipped;
			return result;
		}

		/// <summary>Migrates once, when ProjectSettings.json has no RenderOrderVersion. Originals are zipped into Backups first, and the settings file is written last so a failure is retried on the next open.</summary>
		public static bool Run(string projectPath, ProjectSettings settings)
		{
			var settingsPath = Path.Combine(projectPath, "ProjectSettings.json");
			if (settings?.Rendering == null || !File.Exists(settingsPath) || File.ReadAllText(settingsPath).Contains("\"RenderOrderVersion\""))
				return false;

			var rewrites = new List<(string Path, byte[] Original, byte[] Flipped)>();
			var values = 0;
			foreach (var path in DataFiles(projectPath))
			{
				var original = File.ReadAllBytes(path);
				var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
				var encoding = new UTF8Encoding(hasBom);
				var text = encoding.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));
				var flipped = FlipStoredLayers(text, out var count);
				if (flipped == text)
					continue;
				rewrites.Add((path, original, encoding.GetPreamble().Concat(encoding.GetBytes(flipped)).ToArray()));
				values += count;
			}

			var oldLayers = settings.Rendering.RenderingLayers ?? new Dictionary<string, int>();
			string backup;
			var written = 0;
			try
			{
				backup = WriteBackup(projectPath, settingsPath, rewrites.Select(r => r.Path));
				foreach (var rewrite in rewrites)
				{
					File.WriteAllBytes(rewrite.Path, rewrite.Flipped);
					written++;
				}

				settings.Rendering.RenderingLayers = oldLayers.ToDictionary(p => p.Key, p => -p.Value);
				settings.Rendering.RenderOrderVersion = 1;
				File.WriteAllText(settingsPath, Voltage.Persistence.Json.ToJson(settings, new Voltage.Persistence.JsonSettings { PrettyPrint = true }), new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				for (var i = 0; i < written; i++)
					File.WriteAllBytes(rewrites[i].Path, rewrites[i].Original);
				settings.Rendering.RenderingLayers = oldLayers;
				EditorDebug.Error($"Render order migration failed and was undone, so layers still use the old order: {ex.Message}", "Project");
				return false;
			}

			EditorDebug.Log($"Higher render layers now draw in front. Flipped {values} stored layer values in {rewrites.Count} files and the project's layer table, so nothing moved on screen. Originals: {backup}", "Project");
			return true;
		}

		private static string WriteBackup(string projectPath, string settingsPath, IEnumerable<string> files)
		{
			var folder = Path.Combine(projectPath, "Backups");
			Directory.CreateDirectory(folder);
			var zipPath = Path.Combine(folder, $"render-order-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip");
			using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
			foreach (var file in files.Prepend(settingsPath))
				zip.CreateEntryFromFile(file, Path.GetRelativePath(projectPath, file).Replace('\\', '/'));
			return zipPath;
		}

		private static IEnumerable<string> DataFiles(string folder)
		{
			foreach (var file in Directory.EnumerateFiles(folder))
				if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
					yield return file;

			foreach (var child in Directory.EnumerateDirectories(folder))
			{
				var name = Path.GetFileName(child);
				// Linked folders can point outside the project or back into it.
				if (name.StartsWith(".") || SkippedFolders.Contains(name) || File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
					continue;
				foreach (var file in DataFiles(child))
					yield return file;
			}
		}
	}
}
