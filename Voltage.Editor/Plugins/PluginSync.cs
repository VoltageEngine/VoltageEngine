using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Syncs resolved plugin payloads from their immutable source (cache / bundled / dev folder) into the
	/// project's gitignored <c>PluginLibs/&lt;id&gt;/</c> folder — the per-project, on-disk home of everything
	/// a plugin contributes (managed DLLs, natives, sources, content). The game csproj and the editor both
	/// consume plugins exclusively from here, mirroring the EngineLibs precedent.
	/// </summary>
	public static class PluginSync
	{
		public const string PluginLibsFolderName = "PluginLibs";

		/// <summary>Marker file recording which content hash a synced payload came from, to skip no-op syncs.</summary>
		private const string SyncMarkerFileName = ".voltage-synced-hash";

		public static string GetPluginLibsPath(string projectPath)
		{
			return Path.Combine(projectPath, PluginLibsFolderName);
		}

		public static string GetPluginPayloadPath(string projectPath, string pluginId)
		{
			return Path.Combine(GetPluginLibsPath(projectPath), pluginId);
		}

		/// <summary>
		/// Mirrors a resolved plugin's payload into PluginLibs/&lt;id&gt;/. Pinned payloads are skipped when the
		/// recorded sync marker already matches their content hash; dev payloads mirror on every call (cheap:
		/// size + last-write-time comparison per file, stale files deleted).
		/// Returns the project payload directory.
		/// </summary>
		public static string SyncPlugin(string projectPath, ResolvedPlugin resolved)
		{
			var destDir = GetPluginPayloadPath(projectPath, resolved.Manifest.Id);
			var markerPath = Path.Combine(destDir, SyncMarkerFileName);

			var upToDate = !resolved.IsDev && resolved.ContentHash != null && File.Exists(markerPath)
				&& File.ReadAllText(markerPath).Trim() == resolved.ContentHash;

			if (!upToDate)
			{
				// SDK-pulled files and generated trimmer roots are not part of the package payload —
				// preserve them across the mirror; the pulls below refresh them anyway.
				MirrorDirectory(resolved.PayloadDir, destDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				{
					SyncMarkerFileName, TrimmerRootsFileName,
				});

				if (!resolved.IsDev && resolved.ContentHash != null)
					File.WriteAllText(markerPath, resolved.ContentHash);
				else if (File.Exists(markerPath))
					File.Delete(markerPath); // Dev payloads are unpinned — never claim a hash.

				EditorDebug.Log($"Synced plugin '{resolved.Manifest.Id}' ({resolved.Manifest.Version}) to PluginLibs.", "Plugins");
			}

			// External SDK pulls run on EVERY sync call (even when the payload itself is up to date):
			// the user may have configured the SDK path after the first sync, and pulled files must
			// never be considered part of the immutable payload.
			ApplyExternalSdkPulls(resolved.Manifest, destDir);

			return destDir;
		}

		/// <summary>
		/// Copies files from the user's locally installed external SDKs (FMOD etc.) into the plugin's
		/// project payload, per the manifest's ExternalSdks.Pulls. NDA-protected files therefore only
		/// ever exist inside the gitignored PluginLibs — never in the package, cache, or repository.
		/// A required SDK that is not configured makes the plugin Unavailable (throws).
		/// </summary>
		private static void ApplyExternalSdkPulls(PluginManifest manifest, string payloadDir)
		{
			if (manifest.ExternalSdks == null || manifest.ExternalSdks.Count == 0)
				return;

			foreach (var sdk in manifest.ExternalSdks)
			{
				var sdkRoot = PluginUserSettings.ResolveSdkRoot(sdk);
				if (sdkRoot == null)
				{
					if (sdk.Required)
					{
						var hint = string.IsNullOrWhiteSpace(sdk.EnvVar) ? "" : $" (or set the {sdk.EnvVar} environment variable)";
						throw new PluginResolveException(
							$"Plugin '{manifest.Id}' needs the external SDK '{sdk.DisplayName ?? sdk.Id}'. " +
							$"Set its install path in the Plugin Manager{hint}.");
					}

					EditorDebug.Warn($"Plugin '{manifest.Id}': optional SDK '{sdk.Id}' not configured — related files skipped.", "Plugins");
					continue;
				}

				var pulled = 0;
				foreach (var pull in sdk.Pulls ?? Enumerable.Empty<PluginSdkPull>())
				{
					if (string.IsNullOrWhiteSpace(pull?.From) || string.IsNullOrWhiteSpace(pull.To))
						continue;

					var destDir = Path.Combine(payloadDir, PluginManifest.NormalizeRelative(pull.To));

					// The last path segment of From may be a glob ("libfmod.so*").
					var fromRel = PluginManifest.NormalizeRelative(pull.From);
					var fromDir = Path.Combine(sdkRoot, Path.GetDirectoryName(fromRel) ?? "");
					var fromPattern = Path.GetFileName(fromRel);

					if (!Directory.Exists(fromDir))
						continue;

					foreach (var srcFile in Directory.EnumerateFiles(fromDir, fromPattern))
					{
						Directory.CreateDirectory(destDir);
						var destFile = Path.Combine(destDir, Path.GetFileName(srcFile));

						var srcInfo = new FileInfo(srcFile);
						if (File.Exists(destFile))
						{
							var destInfo = new FileInfo(destFile);
							if (srcInfo.Length == destInfo.Length && srcInfo.LastWriteTimeUtc <= destInfo.LastWriteTimeUtc)
								continue;
						}

						File.Copy(srcFile, destFile, overwrite: true);
						pulled++;
					}
				}

				if (pulled > 0)
					EditorDebug.Log($"Plugin '{manifest.Id}': pulled {pulled} file(s) from SDK '{sdk.Id}' at {sdkRoot}.", "Plugins");

				// After pulls, every manifest-listed payload file must exist — a pull list that doesn't
				// actually produce the promised files is a packaging bug worth failing loudly on.
				if (sdk.Required)
					VerifySdkProducedFiles(manifest, payloadDir, sdk);
			}
		}

		private static void VerifySdkProducedFiles(PluginManifest manifest, string payloadDir, PluginExternalSdk sdk)
		{
			if (manifest.Gameplay?.ManagedAssemblies == null)
				return;

			foreach (var rel in manifest.Gameplay.ManagedAssemblies)
			{
				var path = Path.Combine(payloadDir, PluginManifest.NormalizeRelative(rel));
				if (!File.Exists(path))
					throw new PluginResolveException(
						$"Plugin '{manifest.Id}': assembly '{rel}' is still missing after applying SDK '{sdk.Id}' pulls. " +
						"Check that the configured SDK path points at the right install.");
			}
		}

		/// <summary>
		/// Deletes PluginLibs subfolders that no longer correspond to a plugin in plugins.json
		/// (removed or renamed plugins), keeping the folder an exact reflection of the config.
		/// </summary>
		public static void RemoveStalePayloads(string projectPath, IEnumerable<string> activePluginIds)
		{
			var pluginLibs = GetPluginLibsPath(projectPath);
			if (!Directory.Exists(pluginLibs))
				return;

			var active = new HashSet<string>(activePluginIds, StringComparer.OrdinalIgnoreCase);

			foreach (var dir in Directory.GetDirectories(pluginLibs))
			{
				var name = Path.GetFileName(dir);
				if (active.Contains(name))
					continue;

				try
				{
					Directory.Delete(dir, recursive: true);
					EditorDebug.Log($"Removed stale plugin payload: {name}", "Plugins");
				}
				catch (Exception ex)
				{
					// Locked DLLs (already loaded this session) can block deletion — harmless, retried next open.
					EditorDebug.Warn($"Could not remove stale plugin payload '{name}': {ex.Message}", "Plugins");
				}
			}
		}

		/// <summary>
		/// Makes <paramref name="destDir"/> an exact copy of <paramref name="sourceDir"/>: copies new/changed
		/// files (size + last-write-time), deletes files/dirs that no longer exist in the source, skips .git.
		/// </summary>
		private static void MirrorDirectory(string sourceDir, string destDir, HashSet<string> preserveDestFiles)
		{
			Directory.CreateDirectory(destDir);

			var sourceFiles = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
				.Select(f => Path.GetRelativePath(sourceDir, f))
				.Where(rel => !IsExcluded(rel))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			// Delete destination files that vanished from the source.
			foreach (var destFile in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
			{
				var rel = Path.GetRelativePath(destDir, destFile);
				if (preserveDestFiles.Contains(rel) || sourceFiles.Contains(rel))
					continue;

				try
				{
					File.Delete(destFile);
				}
				catch (Exception ex)
				{
					EditorDebug.Warn($"Could not delete stale file '{rel}': {ex.Message}", "Plugins");
				}
			}

			// Copy new and changed files.
			foreach (var rel in sourceFiles)
			{
				var src = Path.Combine(sourceDir, rel);
				var dest = Path.Combine(destDir, rel);

				if (File.Exists(dest))
				{
					var srcInfo = new FileInfo(src);
					var destInfo = new FileInfo(dest);
					if (srcInfo.Length == destInfo.Length && srcInfo.LastWriteTimeUtc <= destInfo.LastWriteTimeUtc)
						continue;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
				File.Copy(src, dest, overwrite: true);
			}

			// Prune directories that ended up empty after deletions.
			foreach (var dir in Directory.EnumerateDirectories(destDir, "*", SearchOption.AllDirectories)
				         .OrderByDescending(d => d.Length))
			{
				if (!Directory.EnumerateFileSystemEntries(dir).Any())
					Directory.Delete(dir);
			}
		}

		private static bool IsExcluded(string relativePath)
		{
			var normalized = relativePath.Replace('\\', '/');
			return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, ".git", StringComparison.OrdinalIgnoreCase)
				|| normalized.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase);
		}

		#region Game build file generation

		public const string PropsFileName = "Plugins.g.props";
		public const string BootstrapFileName = "PluginBootstrap.g.cs";
		public const string TrimmerRootsFileName = "PluginTrimmerRoots.xml";

		/// <summary>
		/// Regenerates the MSBuild/bootstrap glue that wires plugins into the game build, all inside the
		/// gitignored PluginLibs folder (never touching the user's csproj beyond its one-time Import line):
		/// <list type="bullet">
		///   <item><c>PluginLibs/Plugins.g.props</c> — References (runtime-flavor DLLs), Compile items for
		///     source roots + bootstrap, TrimmerRootDescriptors, and per-RID native copy targets</item>
		///   <item><c>PluginLibs/PluginBootstrap.g.cs</c> — module initializer that AOT-roots each plugin
		///     assembly and forces its registrations on CoreCLR</item>
		///   <item><c>PluginLibs/&lt;id&gt;/PluginTrimmerRoots.xml</c> — preserve-all for plugin assemblies</item>
		/// </list>
		/// Skips plugins that are Disabled/Unavailable/Failed — the build gate in
		/// <see cref="SyncForBuild"/> decides whether that is fatal.
		/// </summary>
		public static void GenerateBuildFiles(string projectPath, IReadOnlyList<PluginInstance> plugins)
		{
			var pluginLibs = GetPluginLibsPath(projectPath);
			Directory.CreateDirectory(pluginLibs);

			var active = plugins
				.Where(p => p.State is PluginState.Restored or PluginState.Loaded && p.Manifest is { IsGameplay: true })
				.ToList();

			var bootstrapRootTypes = new List<string>();
			var props = new StringBuilder();

			props.AppendLine("<Project>");
			props.AppendLine("\t<!-- AUTO-GENERATED by the Voltage Editor from plugins.json / plugins.lock.json. DO NOT EDIT. -->");
			props.AppendLine("\t<!-- Regenerated on project open and before every game build. -->");

			props.AppendLine("\t<ItemGroup>");

			foreach (var plugin in active)
			{
				var gameplay = plugin.Manifest.Gameplay;
				var id = plugin.Manifest.Id;

				// Prebuilt managed DLLs: statically referenced (runtime flavor) so they compile into the
				// AOT image — published games cannot Assembly.LoadFrom.
				// Forward slashes throughout: MSBuild on Windows accepts them everywhere, while
				// backslashes inside Exists()/globs are unreliable on macOS/Linux.
				foreach (var rel in gameplay.ManagedAssemblies ?? Enumerable.Empty<string>())
				{
					var simpleName = Path.GetFileNameWithoutExtension(rel);
					var hintPath = $"$(MSBuildThisFileDirectory){id}/{PluginManifest.NormalizeRelative(rel).Replace(Path.DirectorySeparatorChar, '/')}";
					props.AppendLine($"\t\t<Reference Include=\"{XmlEscape(simpleName)}\">");
					props.AppendLine($"\t\t\t<HintPath>{XmlEscape(hintPath)}</HintPath>");
					props.AppendLine("\t\t</Reference>");
				}

				if (gameplay.ManagedAssemblies is { Count: > 0 })
				{
					WritePluginTrimmerRoots(projectPath, plugin);
					props.AppendLine($"\t\t<TrimmerRootDescriptor Include=\"$(MSBuildThisFileDirectory){XmlEscape(id)}/{TrimmerRootsFileName}\" />");
					bootstrapRootTypes.AddRange(plugin.EffectiveRootTypes);
				}

				// Source-form plugins compile together with the game code; their generated module
				// initializers then run natively, no bootstrap entry needed.
				foreach (var srcRoot in gameplay.SourceRoots ?? Enumerable.Empty<string>())
				{
					var relDir = PluginManifest.NormalizeRelative(srcRoot).Replace(Path.DirectorySeparatorChar, '/');
					props.AppendLine($"\t\t<Compile Include=\"$(MSBuildThisFileDirectory){XmlEscape(id)}/{XmlEscape(relDir)}/**/*.cs\" />");
				}
			}

			if (bootstrapRootTypes.Count > 0)
				props.AppendLine($"\t\t<Compile Include=\"$(MSBuildThisFileDirectory){BootstrapFileName}\" />");

			props.AppendLine("\t</ItemGroup>");

			AppendNativeCopyTargets(props, active);

			props.AppendLine("</Project>");

			File.WriteAllText(Path.Combine(pluginLibs, PropsFileName), props.ToString());

			WriteBootstrap(pluginLibs, bootstrapRootTypes);
		}

		/// <summary>
		/// The generated bootstrap gives ILC a hard <c>typeof</c> root for every plugin assembly and forces
		/// their module initializers on CoreCLR. Note: a bare <c>typeof(X)</c> does NOT trigger module
		/// initializers on CoreCLR (ECMA-335 runs them on first static-member access or method invocation),
		/// hence the explicit RunModuleConstructor. NativeAOT runs all in-image initializers eagerly at
		/// startup; there the call is a harmless no-op (any PNSE is swallowed).
		/// </summary>
		private static void WriteBootstrap(string pluginLibs, List<string> rootTypes)
		{
			var bootstrapPath = Path.Combine(pluginLibs, BootstrapFileName);

			if (rootTypes.Count == 0)
			{
				if (File.Exists(bootstrapPath))
					File.Delete(bootstrapPath);
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("// Generated by the Voltage Editor from plugins.json. DO NOT EDIT.");
			sb.AppendLine("// Roots every plugin assembly for NativeAOT and forces its [ModuleInitializer]");
			sb.AppendLine("// registrations (ComponentIdRegistry / ComponentAotFactory / AOT deserializers)");
			sb.AppendLine("// to run at game startup on CoreCLR.");
			sb.AppendLine("namespace Voltage.Generated");
			sb.AppendLine("{");
			sb.AppendLine("\tinternal static class VoltagePluginBootstrap");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t[global::System.Runtime.CompilerServices.ModuleInitializer]");
			sb.AppendLine("\t\tinternal static void Init()");
			sb.AppendLine("\t\t{");

			foreach (var rootType in rootTypes.Distinct())
			{
				sb.AppendLine($"\t\t\ttry {{ global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(global::{rootType}).Module.ModuleHandle); }}");
				sb.AppendLine("\t\t\tcatch (global::System.Exception) { }");
			}

			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			File.WriteAllText(bootstrapPath, sb.ToString());
		}

		/// <summary>Per-plugin trimmer roots: preserve the whole plugin assembly, like the engine's TrimmerRoots.xml.</summary>
		private static void WritePluginTrimmerRoots(string projectPath, PluginInstance plugin)
		{
			var gameplay = plugin.Manifest.Gameplay;

			var assemblyNames = gameplay.TrimmerRootAssemblies is { Count: > 0 }
				? gameplay.TrimmerRootAssemblies
				: gameplay.ManagedAssemblies.Select(Path.GetFileNameWithoutExtension).ToList();

			var sb = new StringBuilder();
			sb.AppendLine("<!-- AUTO-GENERATED by the Voltage Editor. Preserves plugin assemblies from AOT trimming. -->");
			sb.AppendLine("<linker>");
			foreach (var name in assemblyNames.Distinct(StringComparer.OrdinalIgnoreCase))
				sb.AppendLine($"\t<assembly fullname=\"{XmlEscape(name)}\" preserve=\"all\" />");
			sb.AppendLine("</linker>");

			File.WriteAllText(Path.Combine(GetPluginPayloadPath(projectPath, plugin.Manifest.Id), TrimmerRootsFileName), sb.ToString());
		}

		/// <summary>
		/// Native copy targets, modeled on the editor's CopyMonoGameNativeLibsToPublish pattern:
		/// publish path via ResolvedFileToPublish, plus a plain copy for non-publish builds (dotnet run).
		/// Falls back to the host RID when RuntimeIdentifier is unset.
		/// </summary>
		private static void AppendNativeCopyTargets(StringBuilder props, List<PluginInstance> active)
		{
			var withNatives = active
				.Where(p => p.Manifest.Gameplay?.Natives is { Count: > 0 })
				.ToList();

			if (withNatives.Count == 0)
				return;

			props.AppendLine("\t<PropertyGroup>");
			props.AppendLine("\t\t<_VoltagePluginNativeRid Condition=\"'$(RuntimeIdentifier)' != ''\">$(RuntimeIdentifier)</_VoltagePluginNativeRid>");
			props.AppendLine("\t\t<_VoltagePluginNativeRid Condition=\"'$(_VoltagePluginNativeRid)' == ''\">$(NETCoreSdkRuntimeIdentifier)</_VoltagePluginNativeRid>");
			props.AppendLine("\t</PropertyGroup>");

			// NOTE: the naive one-liner (`<ResolvedFileToPublish Include="glob" RelativePath="%(Filename)%(Extension)" ...`)
			// is a trap — the bare %(Filename) batches over EXISTING ResolvedFileToPublish items and copies
			// natives onto their names (observed: a dylib landing on the game's .pdb). Collect into a scratch
			// item first, then transform with item-qualified metadata, which is per-item and unambiguous.
			props.AppendLine("\t<Target Name=\"CopyVoltagePluginNativesToPublish\" AfterTargets=\"ComputeFilesToPublish\">");
			props.AppendLine("\t\t<ItemGroup>");
			foreach (var plugin in withNatives)
			{
				var nativeDir = $"$(MSBuildThisFileDirectory){plugin.Manifest.Id}/native/$(_VoltagePluginNativeRid)";
				props.AppendLine($"\t\t\t<_VoltagePluginNativePublishFiles Include=\"{XmlEscape(nativeDir)}/*.*\" Condition=\"Exists('{XmlEscape(nativeDir)}')\" />");
			}
			props.AppendLine("\t\t\t<ResolvedFileToPublish Include=\"@(_VoltagePluginNativePublishFiles)\" RelativePath=\"%(_VoltagePluginNativePublishFiles.Filename)%(_VoltagePluginNativePublishFiles.Extension)\" CopyToPublishDirectory=\"PreserveNewest\" Condition=\"'@(_VoltagePluginNativePublishFiles)' != ''\" />");
			props.AppendLine("\t\t</ItemGroup>");
			props.AppendLine("\t</Target>");

			props.AppendLine("\t<Target Name=\"CopyVoltagePluginNativesToBuild\" AfterTargets=\"Build\">");
			props.AppendLine("\t\t<ItemGroup>");
			foreach (var plugin in withNatives)
			{
				var nativeDir = $"$(MSBuildThisFileDirectory){plugin.Manifest.Id}/native/$(_VoltagePluginNativeRid)";
				props.AppendLine($"\t\t\t<_VoltagePluginNativeFiles Include=\"{XmlEscape(nativeDir)}/*.*\" Condition=\"Exists('{XmlEscape(nativeDir)}')\" />");
			}
			props.AppendLine("\t\t</ItemGroup>");
			props.AppendLine("\t\t<Copy SourceFiles=\"@(_VoltagePluginNativeFiles)\" DestinationFolder=\"$(OutDir)\" SkipUnchangedFiles=\"true\" Condition=\"'@(_VoltagePluginNativeFiles)' != ''\" />");
			props.AppendLine("\t</Target>");
		}

		/// <summary>
		/// Pre-publish gate: verifies every enabled plugin restored successfully and regenerates the build
		/// glue from current state. A missing/failed plugin fails the build here with a clear message —
		/// silently shipping a game without a plugin the scenes depend on is worse than a red build.
		/// </summary>
		public static bool SyncForBuild(IGameProject project, out string error)
		{
			error = null;

			var manager = PluginManager.Instance;
			var broken = manager.Plugins
				.Where(p => p.State is PluginState.Unavailable or PluginState.Failed)
				.ToList();

			if (broken.Count > 0)
			{
				error = "Cannot build: plugin(s) unavailable — " +
					string.Join("; ", broken.Select(p => $"'{p.Id}' ({p.Error})")) +
					". Fix or disable them in the Plugin Manager.";
				return false;
			}

			try
			{
				// Dev plugins compile from their working folder in the editor, but game builds consume
				// the PluginLibs copy — re-mirror them now so the build sees the latest sources/DLLs.
				foreach (var plugin in manager.Plugins)
				{
					if (plugin.Entry is { Dev: true } && plugin.Resolved != null)
						SyncPlugin(project.ProjectPath, plugin.Resolved);
				}

				GenerateBuildFiles(project.ProjectPath, manager.Plugins);
				return true;
			}
			catch (Exception ex)
			{
				error = $"Failed to generate plugin build files: {ex.Message}";
				return false;
			}
		}

		/// <summary>Copies each active plugin's declared content into the build output's Content folder.</summary>
		public static bool CopyPluginContentToBuild(string buildDir)
		{
			try
			{
				foreach (var plugin in PluginManager.Instance.Plugins)
				{
					if (plugin.State is not (PluginState.Restored or PluginState.Loaded))
						continue;

					var contentGlobs = plugin.Manifest?.Gameplay?.Content;
					if (contentGlobs == null || contentGlobs.Count == 0 || plugin.PayloadPath == null)
						continue;

					var copied = 0;
					foreach (var glob in contentGlobs)
					{
						// Globs are rooted at the package ("content/**"); copy matches preserving their
						// path relative to the content root folder itself.
						var normalized = PluginManifest.NormalizeRelative(glob);
						var rootDir = normalized.Split(Path.DirectorySeparatorChar)[0];
						var sourceRoot = Path.Combine(plugin.PayloadPath, rootDir);
						if (!Directory.Exists(sourceRoot))
							continue;

						foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
						{
							var rel = Path.GetRelativePath(sourceRoot, file);
							var dest = Path.Combine(buildDir, "Content", rel);
							Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
							File.Copy(file, dest, overwrite: true);
							copied++;
						}
					}

					if (copied > 0)
						EditorDebug.Log($"Copied {copied} content file(s) from plugin '{plugin.Id}'.", "Plugins");
				}

				return true;
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to copy plugin content: {ex.Message}", "Plugins");
				return false;
			}
		}

		private static string XmlEscape(string value)
		{
			return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
		}

		#endregion
	}
}
