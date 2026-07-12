using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// In-memory model of a plugin package's <c>plugin.json</c> manifest. A plugin package is a folder
	/// (or git repo / zip) whose root contains this manifest plus the payload folders it references:
	/// <list type="bullet">
	///   <item><c>lib/</c> — gameplay managed DLLs (net8.0, built WITHOUT the EDITOR symbol)</item>
	///   <item><c>editor-lib/</c> — optional EDITOR-flavored twins of lib/ (first-party engine modules only)</item>
	///   <item><c>editor/</c> — editor-plugin DLLs (reference Voltage.Editor.dll, implement IEditorPlugin)</item>
	///   <item><c>src/</c> — source-form gameplay code, compiled together with the game's Scripts/</item>
	///   <item><c>native/&lt;rid&gt;/</c> — per-RID native libraries</item>
	///   <item><c>content/</c> — runtime content copied into the game build's Content folder</item>
	/// </list>
	/// All JSON keys are PascalCase (Voltage.Persistence.Json field-name matching).
	/// </summary>
	public class PluginManifest
	{
		public const string FileName = "plugin.json";
		public const string KindGameplay = "gameplay";
		public const string KindEditor = "editor";

		public int SchemaVersion = 1;

		/// <summary>Stable unique id, reverse-domain style (e.g. "voltage.farseer", "com.studio.fmod"). Never changes.</summary>
		public string Id;

		/// <summary>Human-readable display name.</summary>
		public string Name;

		/// <summary>Plugin semver (e.g. "1.2.0").</summary>
		public string Version;

		/// <summary>Short human-readable description shown in the Plugin Manager's Description column.</summary>
		public string Description;

		/// <summary>Optional author/vendor name.</summary>
		public string Author;

		/// <summary>Plugin kinds: "gameplay" and/or "editor".</summary>
		public List<string> Kinds = new();

		/// <summary>Engine version range this plugin supports (e.g. "*", "&gt;=0.1.0", "&gt;=0.1.0 &lt;0.2.0").</summary>
		public string EngineVersion = "*";

		/// <summary>Editor plugin API version this plugin was built against. Hard-checked for editor plugins.</summary>
		public int EditorPluginApiVersion = 1;

		public List<PluginDependency> Dependencies = new();

		/// <summary>Gameplay payload description. Required when Kinds contains "gameplay".</summary>
		public PluginGameplaySection Gameplay;

		/// <summary>Editor payload description. Required when Kinds contains "editor".</summary>
		public PluginEditorSection Editor;

		/// <summary>External SDKs the user must install separately (NDA/non-redistributable, e.g. FMOD).</summary>
		public List<PluginExternalSdk> ExternalSdks = new();

		public bool IsGameplay => Kinds != null && Kinds.Contains(KindGameplay);
		public bool IsEditor => Kinds != null && Kinds.Contains(KindEditor);

		/// <summary>
		/// Loads and validates a manifest from a plugin package root directory.
		/// Throws <see cref="PluginManifestException"/> with a user-facing message on any problem.
		/// </summary>
		public static PluginManifest LoadFrom(string packageRoot)
		{
			var manifestPath = Path.Combine(packageRoot, FileName);
			if (!File.Exists(manifestPath))
				throw new PluginManifestException($"No {FileName} found at package root: {packageRoot}");

			PluginManifest manifest;
			try
			{
				manifest = Json.FromJson<PluginManifest>(File.ReadAllText(manifestPath));
			}
			catch (Exception ex)
			{
				throw new PluginManifestException($"Failed to parse {manifestPath}: {ex.Message}");
			}

			if (manifest == null)
				throw new PluginManifestException($"Failed to parse {manifestPath}: empty or invalid JSON");

			manifest.Validate(packageRoot);
			return manifest;
		}

		/// <summary>
		/// Validates required fields and that all referenced payload files exist under the package root.
		/// </summary>
		public void Validate(string packageRoot)
		{
			if (SchemaVersion != 1)
				throw new PluginManifestException($"Unsupported plugin.json SchemaVersion {SchemaVersion} (editor supports 1). The plugin may require a newer editor.");

			if (string.IsNullOrWhiteSpace(Id))
				throw new PluginManifestException("plugin.json is missing required field 'Id'.");
			if (Id.Any(c => char.IsWhiteSpace(c)) || Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				throw new PluginManifestException($"Plugin id '{Id}' contains whitespace or characters invalid in a folder name.");
			if (string.IsNullOrWhiteSpace(Version))
				throw new PluginManifestException($"plugin.json for '{Id}' is missing required field 'Version'.");
			if (Kinds == null || Kinds.Count == 0)
				throw new PluginManifestException($"plugin.json for '{Id}' must declare at least one kind ('{KindGameplay}' or '{KindEditor}').");

			foreach (var kind in Kinds)
			{
				if (kind != KindGameplay && kind != KindEditor)
					throw new PluginManifestException($"plugin.json for '{Id}' declares unknown kind '{kind}'.");
			}

			if (IsGameplay && Gameplay == null)
				throw new PluginManifestException($"Plugin '{Id}' declares kind '{KindGameplay}' but has no 'Gameplay' section.");
			if (IsEditor && (Editor == null || Editor.Assemblies == null || Editor.Assemblies.Count == 0))
				throw new PluginManifestException($"Plugin '{Id}' declares kind '{KindEditor}' but has no 'Editor.Assemblies'.");

			// Payload files must exist relative to the package root. Files produced later by external SDK
			// pulls are exempt — they only exist after sync applies the pulls into PluginLibs.
			var sdkProducedPaths = GetExternalSdkProducedPrefixes();

			if (Gameplay != null)
			{
				ValidatePayloadFiles(packageRoot, Gameplay.ManagedAssemblies, sdkProducedPaths, "Gameplay.ManagedAssemblies");
				ValidatePayloadFiles(packageRoot, Gameplay.EditorManagedAssemblies, sdkProducedPaths, "Gameplay.EditorManagedAssemblies");

				if (Gameplay.SourceRoots != null)
				{
					foreach (var srcRoot in Gameplay.SourceRoots)
					{
						if (!Directory.Exists(Path.Combine(packageRoot, NormalizeRelative(srcRoot))))
							throw new PluginManifestException($"Plugin '{Id}': source root '{srcRoot}' does not exist in the package.");
					}
				}
			}

			if (Editor != null)
				ValidatePayloadFiles(packageRoot, Editor.Assemblies, sdkProducedPaths, "Editor.Assemblies");
		}

		private void ValidatePayloadFiles(string packageRoot, List<string> relativePaths, HashSet<string> sdkProducedPrefixes, string fieldName)
		{
			if (relativePaths == null)
				return;

			foreach (var rel in relativePaths)
			{
				if (string.IsNullOrWhiteSpace(rel))
					throw new PluginManifestException($"Plugin '{Id}': {fieldName} contains an empty path.");

				var normalized = NormalizeRelative(rel);
				if (normalized.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
					throw new PluginManifestException($"Plugin '{Id}': {fieldName} path '{rel}' escapes the package root.");

				if (File.Exists(Path.Combine(packageRoot, normalized)))
					continue;

				// A file that an external SDK pull will produce is allowed to be absent from the package.
				var isSdkProduced = sdkProducedPrefixes.Any(prefix =>
					normalized.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
				if (!isSdkProduced)
					throw new PluginManifestException($"Plugin '{Id}': {fieldName} file '{rel}' not found in the package.");
			}
		}

		/// <summary>
		/// Destination prefixes ("lib/", "native/win-x64/", ...) that external SDK pulls copy files into.
		/// Manifest-listed payload files under these prefixes may be absent from the package itself.
		/// </summary>
		private HashSet<string> GetExternalSdkProducedPrefixes()
		{
			var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (ExternalSdks == null)
				return prefixes;

			foreach (var sdk in ExternalSdks)
			{
				if (sdk?.Pulls == null)
					continue;
				foreach (var pull in sdk.Pulls)
				{
					if (!string.IsNullOrWhiteSpace(pull?.To))
						prefixes.Add(NormalizeRelative(pull.To).Replace('\\', '/').TrimEnd('/') + "/");
				}
			}

			return prefixes;
		}

		internal static string NormalizeRelative(string rel)
		{
			return rel.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		}
	}

	public class PluginDependency
	{
		/// <summary>Id of the plugin this plugin depends on.</summary>
		public string Id;

		/// <summary>Required version range of the dependency (e.g. "&gt;=1.0.0").</summary>
		public string Version = "*";
	}

	public class PluginGameplaySection
	{
		/// <summary>Package-relative paths of managed DLLs shipped into the game (runtime flavor, no EDITOR).</summary>
		public List<string> ManagedAssemblies = new();

		/// <summary>
		/// Optional EDITOR-flavored twins of <see cref="ManagedAssemblies"/> (same assembly names, compiled with
		/// EDITOR defined). The editor loads these instead of the runtime DLLs when present. Only first-party
		/// engine-module plugins with #if EDITOR sites need this.
		/// </summary>
		public List<string> EditorManagedAssemblies = new();

		/// <summary>Package-relative folders whose .cs files compile together with the game's Scripts.</summary>
		public List<string> SourceRoots = new();

		/// <summary>
		/// One public type (namespace-qualified) per managed assembly, used by the generated game bootstrap to
		/// root the assembly for AOT and force its module initializers. Auto-detected when omitted.
		/// </summary>
		public List<string> RootTypes = new();

		/// <summary>
		/// Assembly simple names to preserve wholesale from trimming in AOT game builds.
		/// Defaults to the assembly names of <see cref="ManagedAssemblies"/> when empty.
		/// </summary>
		public List<string> TrimmerRootAssemblies = new();

		/// <summary>Per-RID native libraries shipped with the game and loaded by the editor.</summary>
		public List<PluginNativeSet> Natives = new();

		/// <summary>Package-relative content globs copied into the game build's Content folder.</summary>
		public List<string> Content = new();
	}

	public class PluginEditorSection
	{
		/// <summary>Package-relative paths of editor-plugin DLLs (contain IEditorPlugin implementations).</summary>
		public List<string> Assemblies = new();
	}

	public class PluginNativeSet
	{
		/// <summary>.NET runtime identifier this set applies to (e.g. "win-x64", "osx-arm64").</summary>
		public string Rid;

		/// <summary>Package-relative file globs (e.g. "native/win-x64/*.dll").</summary>
		public List<string> Files = new();
	}

	/// <summary>
	/// An SDK the user must install themselves because its files cannot be redistributed (NDA, licensing —
	/// e.g. FMOD, console SDKs). At sync time the listed pulls copy files from the user's locally configured
	/// SDK root into the project's PluginLibs payload; those files never enter the package, cache, or git.
	/// </summary>
	public class PluginExternalSdk
	{
		/// <summary>Stable id used for the per-user SDK path setting ("PluginSdk_&lt;Id&gt;").</summary>
		public string Id;

		/// <summary>Shown in the Plugin Manager when prompting the user to configure the SDK path.</summary>
		public string DisplayName;

		/// <summary>Environment variable consulted when no per-user path is configured.</summary>
		public string EnvVar;

		/// <summary>When true, the plugin is unavailable until the SDK path is configured and pulls succeed.</summary>
		public bool Required = true;

		public List<PluginSdkPull> Pulls = new();
	}

	public class PluginSdkPull
	{
		/// <summary>SDK-root-relative source file or glob (e.g. "api/core/lib/x64/fmod.dll").</summary>
		public string From;

		/// <summary>Package-relative destination folder (e.g. "native/win-x64/").</summary>
		public string To;
	}

	/// <summary>Thrown for any invalid, missing, or unparsable plugin manifest. Message is user-facing.</summary>
	public class PluginManifestException : Exception
	{
		public PluginManifestException(string message) : base(message)
		{
		}
	}
}
