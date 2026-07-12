using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// The result of resolving one plugins.json entry: a validated manifest, the immutable directory the
	/// payload can be synced from, and the pin data recorded into the lockfile.
	/// </summary>
	public class ResolvedPlugin
	{
		public PluginManifest Manifest;

		/// <summary>Directory the payload is synced into PluginLibs from (cache entry, bundled folder, or dev folder).</summary>
		public string PayloadDir;

		/// <summary>"sha256:…" content hash; null for dev-mode path plugins.</summary>
		public string ContentHash;

		/// <summary>Resolved git commit SHA (git sources only).</summary>
		public string Commit;

		/// <summary>Dev-mode path plugin: unpinned, re-synced every open.</summary>
		public bool IsDev;
	}

	/// <summary>Thrown when a plugin cannot be acquired or fails pin verification. Message is user-facing.</summary>
	public class PluginResolveException : Exception
	{
		public PluginResolveException(string message) : base(message)
		{
		}
	}

	/// <summary>
	/// Acquires plugin packages from their declared sources into the immutable per-user cache and
	/// verifies them against the lockfile. Sources: bundled (ships with the editor), local path,
	/// git URL (pinned ref, user's ambient credentials), https zip.
	/// </summary>
	public static class PluginResolver
	{
		public const string BundledPluginsFolderName = "BundledPlugins";

		/// <summary>
		/// Resolves a single plugins.json entry. When <paramref name="lockEntry"/> is present the acquired
		/// payload must match its pins (hash mismatch = hard error, except bundled plugins which follow the
		/// editor's version). Pass <paramref name="allowRepin"/> for an explicit user-driven update, which
		/// accepts new content and returns fresh pins.
		/// </summary>
		public static ResolvedPlugin Resolve(ProjectPluginEntry entry, PluginLockEntry lockEntry, string projectPath, bool allowRepin = false)
		{
			if (entry.Source == null || !entry.Source.IsValid())
				throw new PluginResolveException($"Plugin '{entry.Id}' has an invalid source in plugins.json — exactly one of Bundled/Git/Zip/Path must be set.");

			if (entry.Source.Bundled)
				return ResolveBundled(entry, lockEntry);

			if (!string.IsNullOrWhiteSpace(entry.Source.Path))
				return ResolvePath(entry, lockEntry, projectPath, allowRepin);

			if (!string.IsNullOrWhiteSpace(entry.Source.Git))
				return ResolveGit(entry, lockEntry, allowRepin);

			return ResolveZip(entry, lockEntry, allowRepin);
		}

		#region Bundled

		/// <summary>
		/// Bundled plugins live under the editor install ("&lt;editor&gt;/BundledPlugins/&lt;folder&gt;/") and are
		/// versioned by the editor itself, so a content-hash change after an editor update is expected —
		/// the lock is refreshed with a log line instead of failing.
		/// </summary>
		private static ResolvedPlugin ResolveBundled(ProjectPluginEntry entry, PluginLockEntry lockEntry)
		{
			var packageRoot = FindBundledPackage(entry.Id);
			if (packageRoot == null)
				throw new PluginResolveException($"Bundled plugin '{entry.Id}' not found under {GetBundledPluginsRoot()}. The editor install may be incomplete.");

			var manifest = PluginManifest.LoadFrom(packageRoot);
			EnsureManifestIdMatches(entry, manifest);

			var hash = PluginCache.ComputeContentHash(packageRoot);
			if (lockEntry?.ContentHash != null && lockEntry.ContentHash != hash)
				EditorDebug.Log($"Bundled plugin '{entry.Id}' changed with the editor ({lockEntry.Version} → {manifest.Version}); lock updated.", "Plugins");

			return new ResolvedPlugin
			{
				Manifest = manifest,
				PayloadDir = packageRoot,
				ContentHash = hash,
			};
		}

		public static string GetBundledPluginsRoot()
		{
			return Path.Combine(AppContext.BaseDirectory, BundledPluginsFolderName);
		}

		/// <summary>
		/// Ids of all plugins bundled with this editor build (scans the BundledPlugins folder). Used to
		/// populate the "Add bundled plugin" dropdown in the Plugin Manager.
		/// </summary>
		public static IReadOnlyList<string> GetAvailableBundledPluginIds()
		{
			var ids = new List<string>();
			var root = GetBundledPluginsRoot();
			if (!Directory.Exists(root))
				return ids;

			foreach (var dir in Directory.GetDirectories(root))
			{
				var manifestPath = Path.Combine(dir, PluginManifest.FileName);
				if (!File.Exists(manifestPath))
					continue;

				try
				{
					var manifest = Voltage.Persistence.Json.FromJson<PluginManifest>(File.ReadAllText(manifestPath));
					if (!string.IsNullOrWhiteSpace(manifest?.Id))
						ids.Add(manifest.Id);
				}
				catch (Exception ex)
				{
					EditorDebug.Warn($"Skipping bundled plugin folder '{dir}': {ex.Message}", "Plugins");
				}
			}

			return ids;
		}

		/// <summary>Finds the bundled package folder whose plugin.json declares the given id.</summary>
		private static string FindBundledPackage(string id)
		{
			var root = GetBundledPluginsRoot();
			if (!Directory.Exists(root))
				return null;

			foreach (var dir in Directory.GetDirectories(root))
			{
				var manifestPath = Path.Combine(dir, PluginManifest.FileName);
				if (!File.Exists(manifestPath))
					continue;

				try
				{
					var manifest = Voltage.Persistence.Json.FromJson<PluginManifest>(File.ReadAllText(manifestPath));
					if (manifest != null && string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
						return dir;
				}
				catch (Exception ex)
				{
					EditorDebug.Warn($"Skipping bundled plugin folder '{dir}': {ex.Message}", "Plugins");
				}
			}

			return null;
		}

		#endregion

		#region Local path

		private static ResolvedPlugin ResolvePath(ProjectPluginEntry entry, PluginLockEntry lockEntry, string projectPath, bool allowRepin)
		{
			var sourceDir = entry.Source.Path;
			if (!System.IO.Path.IsPathRooted(sourceDir))
				sourceDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectPath, sourceDir));

			if (!Directory.Exists(sourceDir))
				throw new PluginResolveException($"Source folder not found: {sourceDir}");

			var manifest = PluginManifest.LoadFrom(sourceDir);
			EnsureManifestIdMatches(entry, manifest);

			// Dev mode: sync straight from the working folder, unpinned. For iterating on a plugin
			// while using it — the payload refreshes on every project open.
			if (entry.Dev)
			{
				return new ResolvedPlugin
				{
					Manifest = manifest,
					PayloadDir = sourceDir,
					ContentHash = null,
					IsDev = true,
				};
			}

			var hash = PluginCache.ComputeContentHash(sourceDir);
			VerifyAgainstLock(entry, lockEntry, hash, allowRepin,
				$"Update the plugin via the Plugin Manager, or mark the entry \"Dev\": true in plugins.json to iterate unpinned.");

			// Immutable copy into the cache so later restores don't depend on the folder still existing.
			// Cache is keyed by the MANIFEST id (authoritative), so this also works in discovery mode
			// where entry.Id is not known yet (the Add-plugin flow).
			if (!PluginCache.HasEntry(manifest.Id, manifest.Version, hash))
			{
				var staging = CreateStagingDir();
				PluginCache.CopyDirectory(sourceDir, staging);
				PluginCache.CommitToCache(staging, manifest.Id, manifest.Version, hash);
			}

			return new ResolvedPlugin
			{
				Manifest = manifest,
				PayloadDir = PluginCache.GetEntryPath(manifest.Id, manifest.Version, hash),
				ContentHash = hash,
			};
		}

		#endregion

		#region Git / Zip (remote acquisition)

		/// <summary>
		/// Git acquisition via the user's git CLI + ambient credentials (SSH agent / credential helper),
		/// which is what lets private NDA plugin repos work with zero credential handling in the editor.
		/// The ref is resolved to a commit SHA once and pinned in the lockfile; restores fetch exactly
		/// that SHA (shallow), so a later force-pushed tag can never silently change what teammates get.
		/// </summary>
		private static ResolvedPlugin ResolveGit(ProjectPluginEntry entry, PluginLockEntry lockEntry, bool allowRepin)
		{
			var url = entry.Source.Git;

			// Fast path: the lock pins this exact source and the cache already holds the payload.
			if (TryUseCachedLockedPayload(entry, lockEntry, allowRepin, out var cached))
				return cached;

			// Determine the commit to fetch: the lock's pin when valid, else resolve the ref remotely.
			string commit;
			if (!allowRepin && lockEntry != null && lockEntry.Source.Matches(entry.Source) && !string.IsNullOrEmpty(lockEntry.Commit))
				commit = lockEntry.Commit;
			else
				commit = ResolveGitRefToSha(entry.Id, url, entry.Source.Ref);

			var staging = CreateStagingDir();
			try
			{
				RunGit(entry.Id, staging, "init --quiet");
				RunGit(entry.Id, staging, $"remote add origin \"{url}\"");
				RunGit(entry.Id, staging, $"fetch --quiet --depth 1 origin {commit}");
				RunGit(entry.Id, staging, "checkout --quiet FETCH_HEAD");

				var manifest = PluginManifest.LoadFrom(staging);
				EnsureManifestIdMatches(entry, manifest);

				var hash = PluginCache.ComputeContentHash(staging);
				VerifyAgainstLock(entry, lockEntry, hash, allowRepin,
					"The pinned commit's content changed unexpectedly — this should not happen for an immutable SHA; delete the lock entry to re-resolve.");

				var cachePath = PluginCache.CommitToCache(staging, manifest.Id, manifest.Version, hash);

				return new ResolvedPlugin
				{
					Manifest = manifest,
					PayloadDir = cachePath,
					ContentHash = hash,
					Commit = commit,
				};
			}
			catch
			{
				TryDeleteStaging(staging);
				throw;
			}
		}

		/// <summary>Https zip acquisition, verified by sha256 content hash against the lockfile.</summary>
		private static ResolvedPlugin ResolveZip(ProjectPluginEntry entry, PluginLockEntry lockEntry, bool allowRepin)
		{
			var url = entry.Source.Zip;

			if (TryUseCachedLockedPayload(entry, lockEntry, allowRepin, out var cached))
				return cached;

			var staging = CreateStagingDir();
			var zipPath = staging + ".zip";
			try
			{
				using (var http = new System.Net.Http.HttpClient())
				{
					http.Timeout = TimeSpan.FromMinutes(5);
					using var response = http.GetAsync(url).GetAwaiter().GetResult();
					if (!response.IsSuccessStatusCode)
						throw new PluginResolveException($"Plugin '{entry.Id}': download failed ({(int)response.StatusCode} {response.ReasonPhrase}) from {url}");

					using var file = File.Create(zipPath);
					response.Content.CopyToAsync(file).GetAwaiter().GetResult();
				}

				System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging);

				// Accept both layouts: plugin.json at the archive root, or inside a single top-level
				// folder (the shape GitHub's "download as zip" produces).
				var packageRoot = staging;
				if (!File.Exists(Path.Combine(packageRoot, PluginManifest.FileName)))
				{
					var subDirs = Directory.GetDirectories(staging);
					if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], PluginManifest.FileName)))
						packageRoot = subDirs[0];
				}

				var manifest = PluginManifest.LoadFrom(packageRoot);
				EnsureManifestIdMatches(entry, manifest);

				var hash = PluginCache.ComputeContentHash(packageRoot);
				VerifyAgainstLock(entry, lockEntry, hash, allowRepin,
					"The file served at the zip URL changed since it was locked. Update the plugin via the Plugin Manager if this is intentional.");

				var cachePath = PluginCache.CommitToCache(packageRoot, manifest.Id, manifest.Version, hash);
				if (packageRoot != staging)
					TryDeleteStaging(staging);

				return new ResolvedPlugin
				{
					Manifest = manifest,
					PayloadDir = cachePath,
					ContentHash = hash,
				};
			}
			catch
			{
				TryDeleteStaging(staging);
				throw;
			}
			finally
			{
				if (File.Exists(zipPath))
					File.Delete(zipPath);
			}
		}

		/// <summary>
		/// Offline-friendly fast path shared by git/zip: when the lock pins this exact source and the
		/// cache already holds the pinned payload, use it without touching the network.
		/// </summary>
		private static bool TryUseCachedLockedPayload(ProjectPluginEntry entry, PluginLockEntry lockEntry, bool allowRepin, out ResolvedPlugin resolved)
		{
			resolved = null;

			if (allowRepin || lockEntry?.ContentHash == null || !lockEntry.Source.Matches(entry.Source))
				return false;

			if (!PluginCache.HasEntry(entry.Id, lockEntry.Version, lockEntry.ContentHash))
				return false;

			var cachePath = PluginCache.GetEntryPath(entry.Id, lockEntry.Version, lockEntry.ContentHash);
			var manifest = PluginManifest.LoadFrom(cachePath);
			EnsureManifestIdMatches(entry, manifest);

			resolved = new ResolvedPlugin
			{
				Manifest = manifest,
				PayloadDir = cachePath,
				ContentHash = lockEntry.ContentHash,
				Commit = lockEntry.Commit,
			};
			return true;
		}

		/// <summary>
		/// Resolves a git ref (tag, branch, or SHA) to a full commit SHA via <c>git ls-remote</c>.
		/// Tags win over branches when both exist; annotated tags prefer the peeled (^{}) commit.
		/// </summary>
		private static string ResolveGitRefToSha(string pluginId, string url, string gitRef)
		{
			if (string.IsNullOrWhiteSpace(gitRef))
				throw new PluginResolveException($"Plugin '{pluginId}': git source needs a \"Ref\" (tag, branch, or commit SHA) to pin.");

			// A full 40-hex SHA needs no resolution.
			if (gitRef.Length == 40 && gitRef.All(Uri.IsHexDigit))
				return gitRef.ToLowerInvariant();

			var output = RunGitCapture(pluginId, null, $"ls-remote \"{url}\" \"{gitRef}\" \"{gitRef}^{{}}\" \"refs/tags/{gitRef}\" \"refs/tags/{gitRef}^{{}}\" \"refs/heads/{gitRef}\"");

			string best = null;
			foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				var parts = line.Split('\t');
				if (parts.Length < 2 || parts[0].Length != 40)
					continue;

				var sha = parts[0];
				var refName = parts[1].Trim();

				// Peeled annotated tags are the actual commit — always prefer them.
				if (refName.EndsWith("^{}", StringComparison.Ordinal))
					return sha.ToLowerInvariant();

				best ??= sha;
			}

			if (best == null)
				throw new PluginResolveException($"Plugin '{pluginId}': ref '{gitRef}' not found at {url} (checked tags, branches, and direct refs).");

			return best.ToLowerInvariant();
		}

		/// <summary>Runs git, surfacing stderr verbatim on failure (auth errors must reach the user unedited).</summary>
		private static void RunGit(string pluginId, string workingDir, string arguments)
		{
			RunGitCapture(pluginId, workingDir, arguments);
		}

		private static string RunGitCapture(string pluginId, string workingDir, string arguments)
		{
			var processInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "git",
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			if (workingDir != null)
				processInfo.WorkingDirectory = workingDir;

			using var process = System.Diagnostics.Process.Start(processInfo);
			if (process == null)
				throw new PluginResolveException($"Plugin '{pluginId}': could not start git. Is git installed and on PATH?");

			var stdout = process.StandardOutput.ReadToEndAsync();
			var stderr = process.StandardError.ReadToEndAsync();
			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				throw new PluginResolveException(
					$"Plugin '{pluginId}': git {arguments.Split(' ')[0]} failed (exit {process.ExitCode}).\n{stderr.Result.Trim()}");
			}

			return stdout.Result;
		}

		private static void TryDeleteStaging(string staging)
		{
			try
			{
				if (Directory.Exists(staging))
					Directory.Delete(staging, recursive: true);
			}
			catch
			{
				// Best-effort temp cleanup.
			}
		}

		#endregion

		#region Shared helpers

		private static void EnsureManifestIdMatches(ProjectPluginEntry entry, PluginManifest manifest)
		{
			// Discovery mode (the Add-plugin flow): the entry has no id yet — it is taken from the
			// resolved manifest by the caller, so there is nothing to cross-check.
			if (string.IsNullOrEmpty(entry.Id))
				return;

			if (!string.Equals(entry.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
				throw new PluginResolveException(
					$"plugins.json entry '{entry.Id}' resolved to a package whose plugin.json declares id '{manifest.Id}'. The entry id must match the manifest.");
		}

		/// <summary>
		/// Enforces the lockfile pin: acquired content must hash to what the lock recorded, so teammates
		/// and CI restore identical bytes. An explicit user update (<paramref name="allowRepin"/>) accepts
		/// the new content instead.
		/// </summary>
		private static void VerifyAgainstLock(ProjectPluginEntry entry, PluginLockEntry lockEntry, string actualHash, bool allowRepin, string remedyHint)
		{
			if (lockEntry?.ContentHash == null || allowRepin)
				return;

			if (!lockEntry.Source.Matches(entry.Source))
				return; // Source itself changed in plugins.json — treat as a new resolution, re-pin below.

			if (lockEntry.ContentHash != actualHash)
				throw new PluginResolveException(
					$"Plugin '{entry.Id}': content does not match plugins.lock.json (expected {lockEntry.ContentHash}, got {actualHash}). " + remedyHint);
		}

		internal static string CreateStagingDir()
		{
			var staging = Path.Combine(Path.GetTempPath(), "VoltagePluginStaging", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(staging);
			return staging;
		}

		#endregion
	}
}
