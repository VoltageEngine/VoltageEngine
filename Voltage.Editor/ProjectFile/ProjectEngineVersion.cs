using System;
using System.IO;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.ProjectFile
{
	public enum ProjectVersionState
	{
		/// <summary>Project targets exactly this engine.</summary>
		Match,

		/// <summary>Created before the field existed, so the target is unknown.</summary>
		Unstamped,

		/// <summary>Project targets an older engine. Safe to open; upgrade when you mean to.</summary>
		Older,

		/// <summary>Project targets a newer engine. The dangerous direction: saving can drop what this build did not understand.</summary>
		Newer,

		/// <summary>Recorded version is not a version this build can parse.</summary>
		Unreadable,
	}

	/// <summary>Compares the engine version a project targets against the running editor. Plugins are pinned by the lockfile; nothing did that for the engine itself.</summary>
	public static class ProjectEngineVersion
	{
		public static ProjectVersionState State { get; private set; } = ProjectVersionState.Match;

		/// <summary>What the project file records, or null when it predates the field.</summary>
		public static string ProjectVersion { get; private set; }

		public static string EditorVersion => VoltageVersion.Engine;

		/// <summary>Human-readable summary of <see cref="State"/>, or null when there is nothing to say.</summary>
		public static string Summary { get; private set; }

		/// <summary>Called on project open. Logs the mismatch; the UI reads the state to show it.</summary>
		public static void Evaluate(ProjectCreatorWindow.ProjectMetadata metadata)
		{
			ProjectVersion = metadata?.EngineVersion;
			Summary = null;

			if (string.IsNullOrWhiteSpace(ProjectVersion))
			{
				State = ProjectVersionState.Unstamped;
				Summary = $"This project does not record an engine version. It is now on {EditorVersion}; " +
				          "stamp it so teammates on a different build get a warning instead of silent surprises.";
				return;
			}

			if (!TryParse(ProjectVersion, out var project) || !TryParse(EditorVersion, out var editor))
			{
				State = ProjectVersionState.Unreadable;
				Summary = $"This project records engine version '{ProjectVersion}', which is not a version " +
				          $"this build ({EditorVersion}) can compare against.";
				return;
			}

			var comparison = project.CompareTo(editor);
			if (comparison == 0)
			{
				State = ProjectVersionState.Match;
				return;
			}

			if (comparison > 0)
			{
				State = ProjectVersionState.Newer;
				Summary = $"This project targets Voltage {ProjectVersion}, but this editor is {EditorVersion}. " +
				          "Scenes may contain settings this build does not understand, and saving them here can " +
				          "drop those settings. Update the editor before editing.";
				EditorDebug.Warn(Summary, "Project");
				return;
			}

			State = ProjectVersionState.Older;
			Summary = $"This project targets Voltage {ProjectVersion}; this editor is {EditorVersion}. " +
			          "Opening is safe. Upgrade the project when the whole team is on this version.";
			EditorDebug.Log(Summary, "Project");
		}

		/// <summary>Records this editor's version in the project file, committed like any other change. Upgrades only: lowering it removes the warning while leaving content the older build cannot read. allowDowngrade is for a caller that has established it is safe.</summary>
		public static bool StampCurrentVersion(IGameProject project, string voltageFilePath, out string message,
			bool allowDowngrade = false)
		{
			message = null;

			if (project is not RuntimeGameProject runtime || string.IsNullOrWhiteSpace(voltageFilePath))
			{
				message = "No open project to stamp.";
				return false;
			}

			if (!File.Exists(voltageFilePath))
			{
				message = $"Project file not found: {voltageFilePath}";
				return false;
			}

			var recorded = runtime.Metadata?.EngineVersion;
			if (!allowDowngrade &&
			    TryParse(recorded, out var recordedVersion) &&
			    TryParse(EditorVersion, out var editorVersion) &&
			    recordedVersion > editorVersion)
			{
				message = $"This project targets Voltage {recorded} and this editor is {EditorVersion}. " +
				          "Lowering the recorded version would not make the project readable here - it would " +
				          "only remove the warning telling you it is not. Open it with " +
				          $"{recorded} or newer instead.";
				EditorDebug.Warn(message, "Project");
				return false;
			}

			try
			{
				var previous = runtime.Metadata.EngineVersion;
				runtime.Metadata.EngineVersion = EditorVersion;

				File.WriteAllText(voltageFilePath, Voltage.Persistence.Json.ToJson(
					runtime.Metadata, new Voltage.Persistence.JsonSettings { PrettyPrint = true }));

				Evaluate(runtime.Metadata);

				message = string.IsNullOrWhiteSpace(previous)
					? $"Project now targets Voltage {EditorVersion}. Commit the .voltage file."
					: $"Project moved from Voltage {previous} to {EditorVersion}. Commit the .voltage file.";

				EditorDebug.Log(message, "Project");
				return true;
			}
			catch (Exception ex)
			{
				message = $"Could not update the project file: {ex.Message}";
				EditorDebug.Error(message, "Project");
				return false;
			}
		}

		/// <summary>Semver-ish parse, always normalised to three components: Version.TryParse leaves an absent component at -1, so "0.1" would compare as older than "0.1.0".</summary>
		private static bool TryParse(string text, out Version version)
		{
			version = null;
			if (string.IsNullOrWhiteSpace(text))
				return false;

			var trimmed = text.Trim();
			var cut = trimmed.IndexOfAny(new[] { '-', '+' });
			if (cut >= 0)
				trimmed = trimmed.Substring(0, cut);

			var parts = trimmed.Split('.');
			var numbers = new int[3];

			for (var i = 0; i < 3; i++)
			{
				if (i >= parts.Length)
					continue;   // absent component is zero, not -1

				if (!int.TryParse(parts[i], out var value) || value < 0)
					return false;

				numbers[i] = value;
			}

			version = new Version(numbers[0], numbers[1], numbers[2]);
			return true;
		}
	}
}
