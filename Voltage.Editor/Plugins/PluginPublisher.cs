using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Plugins
{
	public enum PublishStepState
	{
		Pending,
		Running,
		Done,

		/// <summary>Not needed - nothing to commit, or pushing was turned off.</summary>
		Skipped,

		Failed,
	}

	/// <summary>One command in the publish sequence, and how it went.</summary>
	public class PublishStep
	{
		public string Label;

		/// <summary>What will run, for the preview. Not a string that gets executed.</summary>
		public string Command;

		public PublishStepState State = PublishStepState.Pending;

		/// <summary>Outcome, or - on failure - what went wrong and what to do about it.</summary>
		public string Message;
	}

	/// <summary>Something that has to be dealt with before publishing can start.</summary>
	public class PublishBlocker
	{
		public string Label;
		public string Detail;

		/// <summary>Command or action that clears it, or null when there is nothing to run.</summary>
		public string Fix;

		/// <summary>Advisory only: publishing proceeds, but the user should know.</summary>
		public bool IsWarning;
	}

	/// <summary>
	/// A prepared publish: what will run, what stops it, and - once started - how far it got.
	/// </summary>
	public class PublishPlan
	{
		public string PluginId;
		public string DisplayName;
		public string FolderPath;
		public string RepoRoot;
		public string CurrentVersion;
		public string NewVersion;
		public string Tag;
		public string CommitMessage;

		/// <summary>
		/// What changed, in the author's words. Becomes the commit body and the annotated tag's body, which is
		/// what GitHub shows under the commit subject and uses for the release notes.
		/// </summary>
		public string ChangeDescription;

		public bool Push;
		public string Branch;
		public string Repository;

		public readonly List<PublishBlocker> Problems = new();
		public readonly List<PublishStep> Steps = new();

		public volatile bool Running;
		public volatile bool Finished;
		public volatile bool Succeeded;

		/// <summary>What happened overall, and what is left to do by hand.</summary>
		public string Summary;

		public IEnumerable<PublishBlocker> Blockers => Problems.Where(p => !p.IsWarning);
		public IEnumerable<PublishBlocker> Warnings => Problems.Where(p => p.IsWarning);

		public bool CanPublish => !Running && !Finished && !Blockers.Any();
	}

	/// <summary>
	/// The write half of publishing: bump plugin.json, commit, tag, push. The read-only
	/// <see cref="PluginPublishReadiness"/> stays the verifier - this is deliberately a separate type so
	/// that "the readiness check never writes" remains true of the class that says it.
	///
	/// <para>Everything here ends in a public release that CI acts on, so nothing runs until
	/// <see cref="Prepare"/> has cleared it, every command is shown before it runs, and a failure stops the
	/// sequence where it stands rather than trying to unwind it - an automatic rollback of a half-published
	/// release is far more dangerous than an accurate description of where it stopped.</para>
	/// </summary>
	public static class PluginPublisher
	{
		/// <summary>The plan being run or last run. One at a time; publishing is not a background chore.</summary>
		public static PublishPlan Current { get; private set; }

		public static bool IsRunning => Current is { Running: true };

		/// <summary>
		/// Builds the plan and everything that stops it. Read-only: it runs git queries and touches no
		/// file, so it is safe to call while the popup is open and the version field is still being typed.
		/// </summary>
		public static PublishPlan Prepare(PluginInstance plugin, string newVersion, string commitMessage, bool push,
			string changeDescription = null)
		{
			var plan = new PublishPlan
			{
				PluginId = plugin?.Id,
				DisplayName = plugin?.DisplayName,
				FolderPath = plugin == null ? null : PluginPublishReadiness.ResolveFolder(plugin),
				CurrentVersion = plugin?.Manifest?.Version,
				NewVersion = newVersion?.Trim(),
				CommitMessage = string.IsNullOrWhiteSpace(commitMessage) ? null : commitMessage.Trim(),
				ChangeDescription = string.IsNullOrWhiteSpace(changeDescription) ? null : changeDescription.Trim(),
				Push = push,
			};

			plan.Tag = string.IsNullOrWhiteSpace(plan.NewVersion) ? null : "v" + plan.NewVersion;

			CheckEnvironment(plan);
			CheckVersion(plan);
			CheckRepository(plan);
			CheckOwnership(plan);
			CheckTagIsFree(plan);
			BuildSteps(plan);

			return plan;
		}

		#region Preflight

		private static void CheckEnvironment(PublishPlan plan)
		{
			if (string.IsNullOrWhiteSpace(plan.FolderPath) || !Directory.Exists(plan.FolderPath))
			{
				Block(plan, "Plugin folder",
					$"'{plan.PluginId}' does not resolve to a folder on disk, so there is nothing to publish. " +
					"Only a plugin added as a local folder can be published from the editor.");
				return;
			}

			var version = Git(plan.FolderPath, "--version");
			if (version.Failed)
			{
				Block(plan, "Git",
					"Could not run git: " + version.Error + " Install git and make sure it is on your PATH, " +
					"then reopen the editor so it picks up the new PATH.");
				return;
			}

			if (!Directory.Exists(Path.Combine(plan.FolderPath, ".git"))
			    && Git(plan.FolderPath, "rev-parse --git-dir").Failed)
			{
				Block(plan, "Git repository",
					"This folder is not inside a git repository, so there is no history to commit or tag.",
					"git init && git add -A && git commit -m \"Initial commit\"");
				return;
			}

			var identity = Git(plan.FolderPath, "config user.email");
			if (identity.Failed || string.IsNullOrWhiteSpace(identity.Output))
			{
				Block(plan, "Git identity",
					"git has no user.email configured, so it cannot create a commit.",
					"git config --global user.name \"Your Name\" && git config --global user.email \"you@example.com\"");
			}

			var branch = Git(plan.FolderPath, "rev-parse --abbrev-ref HEAD");
			if (branch.Failed)
			{
				// A repository with no commits yet has no HEAD to name.
				Block(plan, "Branch",
					"Could not determine the current branch: " + branch.Error +
					" If this repository has no commits yet, make one before publishing.");
			}
			else
			{
				plan.Branch = branch.Output.Trim();
				if (plan.Branch == "HEAD")
				{
					Block(plan, "Branch",
						"HEAD is detached, so a commit would not belong to any branch and CI would never see it.",
						"git checkout main");
				}
			}

			var toplevel = Git(plan.FolderPath, "rev-parse --show-toplevel");
			if (!toplevel.Failed && !string.IsNullOrWhiteSpace(toplevel.Output))
			{
				plan.RepoRoot = toplevel.Output.Trim();
				if (!SamePath(plan.RepoRoot, plan.FolderPath))
				{
					Warn(plan, "Plugin is a subfolder",
						$"The repository root is {plan.RepoRoot}. Only changes inside the plugin folder are " +
						$"committed, but the tag {plan.Tag} covers the whole repository - which is a problem if " +
						"more than one plugin lives here.");
				}
			}
		}

		private static void CheckVersion(PublishPlan plan)
		{
			if (string.IsNullOrWhiteSpace(plan.NewVersion))
			{
				Block(plan, "Version", "Enter the version to publish.");
				return;
			}

			if (!SemVerRange.TryParse(plan.NewVersion, out _, out _, out _, out _))
			{
				Block(plan, "Version",
					$"'{plan.NewVersion}' is not a version number. Use major.minor.patch, for example 1.3.0.");
				return;
			}

			if (plan.NewVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
			{
				Block(plan, "Version",
					"Enter the version without a leading 'v' - plugin.json stores 1.3.0 and the tag becomes v1.3.0.");
				return;
			}

			if (string.IsNullOrWhiteSpace(plan.CurrentVersion))
			{
				Warn(plan, "Current version",
					"plugin.json has no Version to compare against, so nothing checks that this is a step forward.");
				return;
			}

			if (!SemVerRange.IsNewer(plan.NewVersion, plan.CurrentVersion))
			{
				Block(plan, "Version",
					$"{plan.NewVersion} is not newer than the current {plan.CurrentVersion}. A published version " +
					"is permanent - installs pin it by URL - so it can only go forward.");
			}

			if (string.IsNullOrWhiteSpace(plan.CommitMessage))
				Block(plan, "Commit message", "Enter a commit message describing what changed.");
		}

		private static void CheckRepository(PublishPlan plan)
		{
			if (plan.FolderPath == null || plan.Branch == null)
				return;

			var manifestPath = Path.Combine(plan.FolderPath, PluginManifest.FileName);
			if (!File.Exists(manifestPath))
			{
				Block(plan, "Manifest", $"No {PluginManifest.FileName} in {plan.FolderPath}.");
			}
			else if (FindVersionField(File.ReadAllText(manifestPath)) == null)
			{
				Block(plan, "Manifest",
					$"{PluginManifest.FileName} has no \"Version\" field to update. Add one before publishing.");
			}

			var remote = Git(plan.FolderPath, "remote get-url origin");
			if (remote.Failed || string.IsNullOrWhiteSpace(remote.Output))
			{
				if (plan.Push)
				{
					Block(plan, "Remote",
						"There is no 'origin' remote, so there is nowhere to push to and CI can never build this " +
						"release. Add one, or turn off pushing and push by hand later.",
						"git remote add origin https://github.com/<owner>/<repo>.git");
				}

				return;
			}

			plan.Repository = PluginPublishReadiness.ParseGitHubRepository(remote.Output.Trim());
			if (plan.Repository == null && plan.Push)
			{
				Warn(plan, "Remote",
					"origin is not a github.com URL. The push will still run, but the editor cannot check " +
					"afterwards whether a release was built.");
			}
		}

		/// <summary>
		/// The gate that matters. Having a plugin as a local folder proves nothing - it is equally true of
		/// someone else's plugin you cloned. If a registry already publishes this id from a different
		/// repository, then this is a fork, and releasing it under the same id would shadow the original
		/// for everyone.
		/// </summary>
		private static void CheckOwnership(PublishPlan plan)
		{
			if (plan.PluginId == null)
				return;

			var listed = PluginRegistryIndex.FindById(plan.PluginId);
			if (listed == null)
			{
				if (!PluginRegistryIndex.HasEntries)
				{
					Warn(plan, "Ownership",
						"No registry is loaded, so the editor cannot check whether someone else already " +
						$"publishes '{plan.PluginId}'. Open Browse Plugins to fetch the catalogue first.");
				}

				return;
			}

			var listedRepo = PluginPublishReadiness.ParseGitHubRepository(listed.Git ?? listed.Homepage ?? string.Empty);
			if (listedRepo != null && plan.Repository != null
			    && string.Equals(listedRepo, plan.Repository, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			Block(plan, "Ownership",
				listedRepo == null
					? $"'{plan.PluginId}' is already listed in a registry, from a source the editor cannot " +
					  "identify. Publishing could shadow it."
					: $"'{plan.PluginId}' is published from {listedRepo}, not this checkout. This is a fork - " +
					  "releasing it under the same id would shadow the original everywhere that id resolves.",
				"Give this plugin its own Id in plugin.json. The id is permanent, so change it before the first release.");
		}

		private static void CheckTagIsFree(PublishPlan plan)
		{
			if (plan.FolderPath == null || plan.Tag == null || plan.Branch == null)
				return;

			var local = Git(plan.FolderPath, $"tag --list {plan.Tag}");
			if (!local.Failed && local.Output.Split('\n').Any(t => t.Trim() == plan.Tag))
			{
				Block(plan, "Tag",
					$"{plan.Tag} already exists in this repository. Publish a different version, or delete the " +
					"tag if it was never pushed.",
					$"git tag -d {plan.Tag}");
				return;
			}

			if (!plan.Push)
				return;

			// Short timeout: this is a courtesy check run while the dialog is open, not the operation
			// itself. If origin is slow the push will say so properly.
			var remote = Git(plan.FolderPath, $"ls-remote --tags origin {plan.Tag}", 15_000);
			if (remote.Failed)
			{
				Warn(plan, "Remote tags",
					"Could not reach origin to check whether the tag already exists: " + remote.Error +
					" The push will report the real error if there is one.");
				return;
			}

			if (remote.Output.Contains("refs/tags/" + plan.Tag, StringComparison.Ordinal))
			{
				Block(plan, "Tag",
					$"{plan.Tag} is already on origin, so this version has been published before. Published " +
					"versions are immutable - publish a higher version instead.");
			}
		}

		#endregion

		#region Plan

		private static void BuildSteps(PublishPlan plan)
		{
			var message = plan.CommitMessage ?? "";
			plan.Steps.Clear();

			plan.Steps.Add(new PublishStep
			{
				Label = "Set version in plugin.json",
				Command = $"{PluginManifest.FileName}: Version -> {plan.NewVersion}",
			});

			// Scoped to the plugin folder on purpose: "git add -A" alone stages the whole repository, which
			// would sweep unrelated work into a release commit when the plugin is a subfolder.
			plan.Steps.Add(new PublishStep { Label = "Stage changes", Command = "git add -A -- ." });
			plan.Steps.Add(new PublishStep { Label = "Commit", Command = $"git commit -m \"{message}\"" });
			plan.Steps.Add(new PublishStep { Label = "Create tag", Command = $"git tag -a {plan.Tag} -m \"{message}\"" });

			if (!plan.Push)
				return;

			// Branch before tag: CI runs on the tag, and a tag whose commit is not on any pushed branch
			// builds something nobody can check out.
			plan.Steps.Add(new PublishStep { Label = "Push branch", Command = $"git push origin {plan.Branch}" });
			plan.Steps.Add(new PublishStep { Label = "Push tag", Command = $"git push origin {plan.Tag}" });
		}

		#endregion

		#region Run

		/// <summary>
		/// Runs the plan on a worker. Stops at the first failure, leaving everything before it in place -
		/// the summary says exactly what was done and what to finish by hand.
		/// </summary>
		public static bool Start(PublishPlan plan)
		{
			if (plan == null || !plan.CanPublish || IsRunning)
				return false;

			Current = plan;
			plan.Running = true;

			Task.Run(() =>
			{
				try
				{
					Run(plan);
				}
				catch (Exception ex)
				{
					plan.Succeeded = false;
					plan.Summary = "Publish stopped unexpectedly: " + ex.Message;
					PluginLog.Warn($"Publish of '{plan.PluginId}' threw: {ex}");
				}
				finally
				{
					plan.Running = false;
					plan.Finished = true;
				}
			});

			return true;
		}

		private static void Run(PublishPlan plan)
		{
			var steps = plan.Steps;
			var i = 0;

			// 1. plugin.json
			var bump = steps[i++];
			bump.State = PublishStepState.Running;
			try
			{
				var path = Path.Combine(plan.FolderPath, PluginManifest.FileName);
				var text = File.ReadAllText(path);
				var match = FindVersionField(text);
				if (match == null)
				{
					Fail(plan, bump, $"{PluginManifest.FileName} has no \"Version\" field. Nothing was changed.");
					return;
				}

				// Targeted replacement rather than a deserialize/serialize round-trip, so comments,
				// formatting, field order and any field this editor does not know about all survive.
				File.WriteAllText(path,
					text.Substring(0, match.Groups[1].Index) + plan.NewVersion +
					text.Substring(match.Groups[1].Index + match.Groups[1].Length));

				bump.State = PublishStepState.Done;
				bump.Message = $"{plan.CurrentVersion ?? "(none)"} -> {plan.NewVersion}";
			}
			catch (Exception ex)
			{
				Fail(plan, bump, $"Could not write {PluginManifest.FileName}: {ex.Message} Nothing was committed.");
				return;
			}

			// 2. stage
			var stage = steps[i++];
			if (!RunGitStep(plan, stage, "add -A -- ."))
				return;

			// 3 + 4. commit and tag, both taking their message from a file. A change description is free text -
			// newlines, quotes, backslashes - and quoting that through a process argument loses to the first one
			// of them. The file also keeps the subject and the body in one place, so the commit and the tag say
			// exactly the same thing.
			var messageFile = WriteMessageFile(plan);

			try
			{
				var commit = steps[i++];
				commit.State = PublishStepState.Running;
				var staged = Git(plan.FolderPath, "diff --cached --name-only");
				if (!staged.Failed && string.IsNullOrWhiteSpace(staged.Output))
				{
					// The version bump is itself a change, so this only happens if plugin.json already said
					// the new version - a retry after a failure part-way through.
					commit.State = PublishStepState.Skipped;
					commit.Message = "Nothing to commit; the working tree already matches. Continuing to the tag.";
				}
				else if (!RunGitStep(plan, commit, CommitArguments(plan, messageFile)))
				{
					return;
				}

				var tag = steps[i++];
				if (!RunGitStep(plan, tag, TagArguments(plan, messageFile)))
					return;
			}
			finally
			{
				if (messageFile != null)
				{
					try { File.Delete(messageFile); } catch { /* a temp file left behind is not worth a failure */ }
				}
			}

			if (!plan.Push)
			{
				plan.Succeeded = true;
				plan.Summary =
					$"Committed and tagged {plan.Tag} locally. Nothing has been published yet - push the branch " +
					$"and the tag when you are ready:\n" +
					$"    git push origin {plan.Branch}\n" +
					$"    git push origin {plan.Tag}";
				return;
			}

			// 5. push branch
			var pushBranch = steps[i++];
			if (!RunGitStep(plan, pushBranch, $"push origin {plan.Branch}"))
			{
				plan.Summary +=
					$"\n\nThe commit and the tag {plan.Tag} exist locally, so nothing is lost. Fix the problem " +
					$"above and push both:\n    git push origin {plan.Branch}\n    git push origin {plan.Tag}";
				return;
			}

			// 6. push tag - the point of no return: this is what CI builds a public release from.
			var pushTag = steps[i];
			if (!RunGitStep(plan, pushTag, $"push origin {plan.Tag}"))
			{
				plan.Summary +=
					$"\n\nThe branch was pushed, so the commit is on origin; only the tag is missing. Retry with:" +
					$"\n    git push origin {plan.Tag}";
				return;
			}

			plan.Succeeded = true;
			plan.Summary =
				$"Published {plan.Tag}. CI is now building the release" +
				(plan.Repository != null ? $" - watch it at https://github.com/{plan.Repository}/actions" : "") +
				".\n\nTwo things are still outstanding: the release has to finish building before anything can " +
				"install it, and the registry entry has to point at the new version before it shows up in " +
				"Browse Plugins. Press Check below to follow both.";
		}

		/// <summary>
		/// Writes the commit/tag message: the subject line, then the change description as the body, separated by
		/// the blank line git expects - which is what makes GitHub show the first line as the title and the rest
		/// as the description underneath it.
		/// </summary>
		/// <returns>The temp file, or null when it could not be written - the caller then falls back to -m.</returns>
		private static string WriteMessageFile(PublishPlan plan)
		{
			var subject = plan.CommitMessage ?? $"{plan.DisplayName} {plan.NewVersion}";

			var message = string.IsNullOrWhiteSpace(plan.ChangeDescription)
				? subject
				: subject + "\n\n" + plan.ChangeDescription.Replace("\r\n", "\n").Trim();

			try
			{
				var path = Path.Combine(Path.GetTempPath(), $"voltage-publish-{Guid.NewGuid():N}.txt");
				File.WriteAllText(path, message + "\n", new System.Text.UTF8Encoding(false));
				return path;
			}
			catch
			{
				return null;
			}
		}

		// --cleanup=whitespace, not the default: the default strips '#' lines, and a description may legitimately
		// start one ("#42 fixed"). Whitespace-only cleanup keeps every line the author wrote.
		private static string CommitArguments(PublishPlan plan, string messageFile) =>
			messageFile != null
				? $"commit --cleanup=whitespace --file {Quote(messageFile)}"
				: $"commit -m {Quote(plan.CommitMessage)}";

		private static string TagArguments(PublishPlan plan, string messageFile) =>
			messageFile != null
				? $"tag -a {plan.Tag} --cleanup=whitespace --file {Quote(messageFile)}"
				: $"tag -a {plan.Tag} -m {Quote(plan.CommitMessage)}";

		private static bool RunGitStep(PublishPlan plan, PublishStep step, string arguments)
		{
			step.State = PublishStepState.Running;

			var result = Git(plan.FolderPath, arguments, TimeoutForGit(arguments));
			if (!result.Failed)
			{
				step.State = PublishStepState.Done;
				step.Message = FirstLine(result.Output);
				return true;
			}

			Fail(plan, step, Explain(arguments, result.Error, plan));
			return false;
		}

		private static void Fail(PublishPlan plan, PublishStep step, string message)
		{
			step.State = PublishStepState.Failed;
			step.Message = message;
			plan.Succeeded = false;
			plan.Summary = $"Stopped at \"{step.Label}\": {message}";

			foreach (var later in plan.Steps.SkipWhile(s => s != step).Skip(1))
			{
				later.State = PublishStepState.Skipped;
				later.Message = "Not reached.";
			}

			PluginLog.Warn($"Publish of '{plan.PluginId}' failed at {step.Label}: {message}");
		}

		/// <summary>
		/// Turns git's stderr into something actionable. Every one of these is a failure a plugin author
		/// hits sooner or later, and git's own wording assumes a terminal the editor does not have.
		/// </summary>
		private static string Explain(string arguments, string error, PublishPlan plan)
		{
			var text = error ?? "";
			var isPush = arguments.StartsWith("push", StringComparison.Ordinal);

			if (text.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase))
			{
				return "git could not authenticate with origin. The editor runs git without a terminal, so it " +
				       "cannot answer a password prompt. Push once from a terminal to cache your credentials " +
				       "(or set up an SSH key or a credential helper), then publish again.\n\ngit said: " + FirstLine(text);
			}

			if (text.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("rejected", StringComparison.OrdinalIgnoreCase) && isPush)
			{
				return $"origin has commits that you do not have, so the push was rejected. Pull them first, then " +
				       $"publish again:\n    git pull --rebase origin {plan.Branch}\n\ngit said: " + FirstLine(text);
			}

			if (text.Contains("does not appear to be a git repository", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("Could not read from remote repository", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("repository not found", StringComparison.OrdinalIgnoreCase))
			{
				return "origin could not be reached. Check that the remote URL is right and that you are online:\n" +
				       "    git remote -v\n\ngit said: " + FirstLine(text);
			}

			if (text.Contains("no upstream", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("does not match any", StringComparison.OrdinalIgnoreCase))
			{
				return $"origin has no branch called '{plan.Branch}' yet. Push it once to create it:\n" +
				       $"    git push -u origin {plan.Branch}\n\ngit said: " + FirstLine(text);
			}

			if (text.Contains("already exists", StringComparison.OrdinalIgnoreCase))
			{
				return $"{plan.Tag} already exists. Publish a higher version, or delete the tag if it was never " +
				       $"pushed: git tag -d {plan.Tag}\n\ngit said: " + FirstLine(text);
			}

			if (text.Contains("Please tell me who you are", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("user.email", StringComparison.OrdinalIgnoreCase))
			{
				return "git has no identity configured, so it cannot create the commit:\n" +
				       "    git config --global user.name \"Your Name\"\n" +
				       "    git config --global user.email \"you@example.com\"";
			}

			if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase))
			{
				return "git timed out. Check your network connection and whether origin is reachable, then " +
				       "publish again.";
			}

			if (text.Contains("pre-commit", StringComparison.OrdinalIgnoreCase)
			    || text.Contains("hook", StringComparison.OrdinalIgnoreCase))
			{
				return "A git hook rejected this. Fix whatever it reported and publish again.\n\ngit said: " + text;
			}

			return string.IsNullOrWhiteSpace(text) ? "git failed without reporting a reason." : text;
		}

		#endregion

		#region Plumbing

		/// <summary>
		/// The "Version": "x.y.z" field, with group 1 spanning just the value. Anchored on the opening
		/// quote so it cannot match SchemaVersion or EditorPluginApiVersion.
		/// </summary>
		private static Match FindVersionField(string manifestText)
		{
			var match = Regex.Match(manifestText, "\"Version\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match : null;
		}

		private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

		private static string FirstLine(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var line = text.Trim().Split('\n')[0].Trim();
			return line.Length > 0 ? line : null;
		}

		private static bool SamePath(string a, string b)
		{
			try
			{
				return string.Equals(
					Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
					Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
					StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		// A push crosses the network; the local commands do not.
		private static int TimeoutForGit(string arguments) =>
			arguments.StartsWith("push", StringComparison.Ordinal) || arguments.StartsWith("ls-remote", StringComparison.Ordinal)
				? 120_000
				: 30_000;

		private static void Block(PublishPlan plan, string label, string detail, string fix = null) =>
			plan.Problems.Add(new PublishBlocker { Label = label, Detail = detail, Fix = fix });

		private static void Warn(PublishPlan plan, string label, string detail, string fix = null) =>
			plan.Problems.Add(new PublishBlocker { Label = label, Detail = detail, Fix = fix, IsWarning = true });

		private readonly struct GitResult
		{
			public readonly string Output;
			public readonly string Error;
			public readonly bool Failed;

			public GitResult(string output, string error, bool failed)
			{
				Output = output;
				Error = error;
				Failed = failed;
			}
		}

		private static GitResult Git(string workingDir, string arguments, int timeoutMs = 30_000)
		{
			try
			{
				var info = new ProcessStartInfo
				{
					FileName = "git",
					Arguments = arguments,
					WorkingDirectory = workingDir,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};

				// Without a terminal there is nobody to answer a credential prompt, so fail fast and say so
				// rather than hanging until the timeout.
				info.Environment["GIT_TERMINAL_PROMPT"] = "0";

				using var process = Process.Start(info);
				if (process == null)
					return new GitResult(null, "Could not start git. Is it installed and on PATH?", true);

				var stdout = process.StandardOutput.ReadToEndAsync();
				var stderr = process.StandardError.ReadToEndAsync();

				if (!process.WaitForExit(timeoutMs))
				{
					try { process.Kill(true); } catch { /* already gone */ }
					return new GitResult(null, "git timed out.", true);
				}

				return process.ExitCode == 0
					? new GitResult(stdout.Result, null, false)
					: new GitResult(stdout.Result, stderr.Result.Trim(), true);
			}
			catch (Exception ex)
			{
				return new GitResult(null, ex.Message, true);
			}
		}

		#endregion
	}
}
