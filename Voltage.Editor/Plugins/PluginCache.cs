using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Voltage.Editor.DebugUtils;
using Voltage.Utils;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Per-user, cross-project download cache for plugin packages, NuGet-style:
	/// <c>&lt;storage root&gt;/PluginCache/&lt;id&gt;/&lt;version&gt;+&lt;hash8&gt;/</c>. Entries are written once
	/// (acquire → hash → move into place) and treated as immutable afterwards, so restores are cheap
	/// cache hits verified purely by the lockfile's content hash.
	/// </summary>
	public static class PluginCache
	{
		public const string CacheFolderName = "PluginCache";
		private const string HashPrefix = "sha256:";

		public static string GetCacheRoot()
		{
			return Path.Combine(Storage.GetStorageRoot(), CacheFolderName);
		}

		/// <summary>Cache directory for a given plugin id + version + content hash ("sha256:…").</summary>
		public static string GetEntryPath(string id, string version, string contentHash)
		{
			return Path.Combine(GetCacheRoot(), id, $"{version}+{ShortHash(contentHash)}");
		}

		/// <summary>True when the cache already holds this exact payload.</summary>
		public static bool HasEntry(string id, string version, string contentHash)
		{
			var path = GetEntryPath(id, version, contentHash);
			return Directory.Exists(path) && File.Exists(Path.Combine(path, PluginManifest.FileName));
		}

		/// <summary>
		/// Moves an acquired package directory into the cache under its computed identity and returns the
		/// final cache path. The staging directory must contain a valid plugin.json; it is consumed (moved).
		/// If an identical entry already exists, the staging dir is discarded and the existing entry returned.
		/// </summary>
		public static string CommitToCache(string stagingDir, string id, string version, string contentHash)
		{
			var entryPath = GetEntryPath(id, version, contentHash);
			if (Directory.Exists(entryPath))
			{
				TryDeleteDirectory(stagingDir);
				return entryPath;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
			try
			{
				Directory.Move(stagingDir, entryPath);
			}
			catch (IOException)
			{
				// Cross-volume move (temp on a different drive than storage) — fall back to copy.
				CopyDirectory(stagingDir, entryPath);
				TryDeleteDirectory(stagingDir);
			}

			return entryPath;
		}

		/// <summary>
		/// Computes the canonical content hash of a package directory: sha256 over each file's
		/// forward-slash relative path (ordinal-sorted, lowercase-invariant) followed by its bytes.
		/// .git directories are excluded so git and zip acquisitions of the same payload hash identically.
		/// </summary>
		public static string ComputeContentHash(string packageRoot)
		{
			using var sha = SHA256.Create();
			using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);

			var files = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
				.Select(f => (Absolute: f, Relative: Path.GetRelativePath(packageRoot, f).Replace('\\', '/')))
				.Where(f => !f.Relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
				            && !string.Equals(f.Relative, ".git", StringComparison.OrdinalIgnoreCase)
				            && !f.Relative.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase))
				.OrderBy(f => f.Relative, StringComparer.Ordinal);

			foreach (var file in files)
			{
				var pathBytes = Encoding.UTF8.GetBytes(file.Relative.ToLowerInvariant() + "\n");
				stream.Write(pathBytes, 0, pathBytes.Length);

				using var fs = File.OpenRead(file.Absolute);
				fs.CopyTo(stream);
			}

			stream.FlushFinalBlock();
			return HashPrefix + Convert.ToHexString(sha.Hash!).ToLowerInvariant();
		}

		/// <summary>Recursively copies a directory, skipping .git internals.</summary>
		public static void CopyDirectory(string sourceDir, string destDir)
		{
			Directory.CreateDirectory(destDir);

			foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
			{
				var rel = Path.GetRelativePath(sourceDir, dir);
				if (IsGitPath(rel))
					continue;
				Directory.CreateDirectory(Path.Combine(destDir, rel));
			}

			foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
			{
				var rel = Path.GetRelativePath(sourceDir, file);
				if (IsGitPath(rel))
					continue;
				File.Copy(file, Path.Combine(destDir, rel), overwrite: true);
			}
		}

		private static bool IsGitPath(string relativePath)
		{
			var normalized = relativePath.Replace('\\', '/');
			return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, ".git", StringComparison.OrdinalIgnoreCase);
		}

		private static string ShortHash(string contentHash)
		{
			var hex = contentHash != null && contentHash.StartsWith(HashPrefix, StringComparison.Ordinal)
				? contentHash.Substring(HashPrefix.Length)
				: contentHash ?? "";
			return hex.Length >= 8 ? hex.Substring(0, 8) : hex;
		}

		private static void TryDeleteDirectory(string dir)
		{
			try
			{
				if (Directory.Exists(dir))
					Directory.Delete(dir, recursive: true);
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Could not clean up directory '{dir}': {ex.Message}", "PluginCache");
			}
		}
	}
}
