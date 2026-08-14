using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	public enum PublishCheckState
	{
		Ok,
		Warning,

		/// <summary>Publishing cannot proceed until this is dealt with.</summary>
		Blocked,

		/// <summary>Could not be determined - no remote, no network, not a git repo.</summary>
		Unknown,
	}

	public class PublishCheck
	{
		public string Label;
		public PublishCheckState State;
		public string Detail;

		/// <summary>Shell command that resolves it, or null when there is nothing to run.</summary>
		public string Fix;
	}

	public class PublishReport
	{
		public string PluginId;
		public string FolderPath;
		public string Version;
		public string Tag;
		public string Repository;
		public bool Running;
		public string FatalError;
		public readonly List<PublishCheck> Checks = new();
	}

	/// <summary>Names which step of the publish sequence is missing, and the command that fixes it: every one of them otherwise presents as the plugin simply not appearing in Browse Plugins. Never writes, and makes at most one anonymous GitHub request per explicit refresh.</summary>
	public static class PluginPublishReadiness
	{
		private static readonly Dictionary<string, PublishReport> _reports = new(StringComparer.Ordinal);
		private static readonly HttpClient _http = CreateClient();

		private static HttpClient CreateClient()
		{
			var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("VoltageEditor");
			client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return client;
		}

		/// <summary>The last report for this plugin, or null when it has never been checked.</summary>
		public static PublishReport Get(string pluginId) =>
			pluginId != null && _reports.TryGetValue(pluginId, out var report) ? report : null;

		/// <summary>True when this plugin is one the user authors, so the panel is worth showing.</summary>
		public static bool IsAuthorable(PluginInstance plugin) =>
			plugin?.Entry != null && !string.IsNullOrWhiteSpace(plugin.Entry.Source?.Path);

		public static void RefreshAsync(PluginInstance plugin)
		{
			if (!IsAuthorable(plugin))
				return;

			var id = plugin.Id;
			var folder = ResolveFolder(plugin);
			var version = plugin.Manifest?.Version;

			var report = new PublishReport
			{
				PluginId = id,
				FolderPath = folder,
				Version = version,
				Tag = string.IsNullOrEmpty(version) ? null : "v" + version,
				Running = true,
			};
			_reports[id] = report;

			Task.Run(() =>
			{
				try
				{
					Build(report);
				}
				catch (Exception ex)
				{
					report.FatalError = ex.Message;
					PluginLog.Warn($"Publish check for '{id}' failed: {ex.Message}");
				}
				finally
				{
					report.Running = false;
				}
			});
		}

		/// <summary>The plugin's folder on disk, resolving a project-relative Path source.</summary>
		public static string ResolveFolder(PluginInstance plugin)
		{
			var path = plugin.Entry.Source.Path;
			if (Path.IsPathRooted(path))
				return path;

			var projectPath = ProjectManager.Instance.HasActiveProject
				? ProjectManager.Instance.CurrentProject.ProjectPath
				: null;

			return projectPath == null ? path : Path.GetFullPath(Path.Combine(projectPath, path));
		}

		private static void Build(PublishReport report)
		{
			var checks = report.Checks;

			if (string.IsNullOrEmpty(report.Version))
			{
				checks.Add(new PublishCheck
				{
					Label = "Manifest version",
					State = PublishCheckState.Blocked,
					Detail = "plugin.json has no Version.",
				});
				return;
			}

			if (!Directory.Exists(Path.Combine(report.FolderPath, ".git")))
			{
				checks.Add(new PublishCheck
				{
					Label = "Git repository",
					State = PublishCheckState.Unknown,
					Detail = "This folder is not a git repository, so nothing can be published from it.",
					Fix = "git init && git add -A && git commit -m \"Initial commit\"",
				});
				return;
			}

			var hasRemote = ResolveRemote(report, checks);
			if (!CheckIdentity(report, checks, hasRemote))
				return;

			CheckWorkingTree(report, checks);
			var tagExists = CheckLocalTag(report, checks);
			var tagPushed = CheckTagPushed(report, checks, tagExists, hasRemote);
			CheckRelease(report, checks, tagPushed);
			CheckRegistry(report, checks);
		}

		/// <summary>Whether this checkout owns the plugin's id. A registry already publishing it from a different repository makes this a fork, and releasing under the same id would shadow the original. False means publishing should not be attempted at all.</summary>
		private static bool CheckIdentity(PublishReport report, List<PublishCheck> checks, bool hasRemote)
		{
			var listed = PluginRegistryIndex.HasEntries
				? PluginRegistryIndex.Entries.FirstOrDefault(e =>
					string.Equals(e.Id, report.PluginId, StringComparison.Ordinal))
				: null;

			if (listed == null)
			{
				checks.Add(new PublishCheck
				{
					Label = "Ownership",
					State = PublishCheckState.Ok,
					Detail = hasRemote
						? $"'{report.PluginId}' is not published by anyone else, so this repository owns it."
						: $"'{report.PluginId}' is unclaimed.",
				});
				return true;
			}

			var listedRepo = ParseGitHubRepository(listed.Git ?? listed.Homepage ?? string.Empty);
			var sameRepo = listedRepo != null && report.Repository != null &&
			               string.Equals(listedRepo, report.Repository, StringComparison.OrdinalIgnoreCase);

			if (sameRepo)
			{
				checks.Add(new PublishCheck
				{
					Label = "Ownership",
					State = PublishCheckState.Ok,
					Detail = $"This is the repository {report.PluginId} is published from.",
				});
				return true;
			}

			checks.Add(new PublishCheck
			{
				Label = "Ownership",
				State = PublishCheckState.Blocked,
				Detail = listedRepo == null
					? $"'{report.PluginId}' is already listed in a registry, but from a source this editor cannot identify. Publishing could shadow it."
					: $"'{report.PluginId}' is published from {listedRepo}, not this checkout. This is a fork - releasing it under the same id would shadow the original.",
				Fix = "Give it your own Id in plugin.json (the id is permanent, so pick it before the first release).",
			});
			return false;
		}

		private static void CheckWorkingTree(PublishReport report, List<PublishCheck> checks)
		{
			var status = Git(report.FolderPath, "status --porcelain");
			if (status.Failed)
			{
				checks.Add(new PublishCheck
				{
					Label = "Working tree",
					State = PublishCheckState.Unknown,
					Detail = status.Error,
				});
				return;
			}

			var dirty = status.Output.Split('\n').Count(l => l.Trim().Length > 0);
			checks.Add(dirty == 0
				? new PublishCheck { Label = "Working tree", State = PublishCheckState.Ok, Detail = "Clean." }
				: new PublishCheck
				{
					Label = "Working tree",
					State = PublishCheckState.Warning,
					Detail = $"{dirty} uncommitted change(s). A release is built from the tagged commit, so these would not be in it.",
					Fix = "git add -A && git commit -m \"...\"",
				});
		}

		private static bool CheckLocalTag(PublishReport report, List<PublishCheck> checks)
		{
			var tags = Git(report.FolderPath, "tag --list");
			if (tags.Failed)
			{
				checks.Add(new PublishCheck { Label = "Tag", State = PublishCheckState.Unknown, Detail = tags.Error });
				return false;
			}

			var all = tags.Output.Split('\n').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			if (all.Contains(report.Tag, StringComparer.Ordinal))
			{
				checks.Add(new PublishCheck
				{
					Label = "Tag",
					State = PublishCheckState.Ok,
					Detail = $"{report.Tag} exists locally.",
				});
				return true;
			}

			// A near-miss like v1.0 for 1.0.0 is worth calling out separately from having no tag.
			var near = all.FirstOrDefault(t => t.TrimStart('v').Split('-')[0] != report.Version &&
			                                   t.StartsWith("v", StringComparison.Ordinal) &&
			                                   report.Version.StartsWith(t.TrimStart('v'), StringComparison.Ordinal));

			checks.Add(new PublishCheck
			{
				Label = "Tag",
				State = PublishCheckState.Blocked,
				Detail = near != null
					? $"No {report.Tag}. Found {near}, which does not match plugin.json Version {report.Version} - CI rejects a mismatch."
					: $"No {report.Tag} for plugin.json Version {report.Version}.",
				Fix = $"git tag -a {report.Tag} -m \"{report.PluginId} {report.Version}\"",
			});
			return false;
		}

		/// <summary>Records the origin repository. False when there is no remote to publish to.</summary>
		private static bool ResolveRemote(PublishReport report, List<PublishCheck> checks)
		{
			var remote = Git(report.FolderPath, "remote get-url origin");
			if (remote.Failed || string.IsNullOrWhiteSpace(remote.Output))
			{
				checks.Add(new PublishCheck
				{
					Label = "Remote",
					State = PublishCheckState.Blocked,
					Detail = "No 'origin' remote, so there is nowhere to publish to.",
					Fix = "git remote add origin https://github.com/<owner>/<repo>.git",
				});
				return false;
			}

			report.Repository = ParseGitHubRepository(remote.Output.Trim());
			return true;
		}

		private static bool CheckTagPushed(PublishReport report, List<PublishCheck> checks, bool tagExists, bool hasRemote)
		{
			if (!hasRemote)
				return false;

			if (!tagExists)
			{
				checks.Add(new PublishCheck
				{
					Label = "Tag pushed",
					State = PublishCheckState.Blocked,
					Detail = "Create the tag first.",
				});
				return false;
			}

			var lsRemote = Git(report.FolderPath, $"ls-remote --tags origin {report.Tag}");
			if (lsRemote.Failed)
			{
				checks.Add(new PublishCheck
				{
					Label = "Tag pushed",
					State = PublishCheckState.Unknown,
					Detail = "Could not reach the remote: " + lsRemote.Error,
				});
				return false;
			}

			var pushed = lsRemote.Output.Contains(report.Tag, StringComparison.Ordinal);
			checks.Add(pushed
				? new PublishCheck { Label = "Tag pushed", State = PublishCheckState.Ok, Detail = $"{report.Tag} is on origin." }
				: new PublishCheck
				{
					Label = "Tag pushed",
					State = PublishCheckState.Blocked,
					Detail = $"{report.Tag} exists locally but not on origin, so CI has never run for it.",
					Fix = $"git push origin {report.Tag}",
				});

			return pushed;
		}

		private static void CheckRelease(PublishReport report, List<PublishCheck> checks, bool tagPushed)
		{
			if (report.Repository == null)
			{
				checks.Add(new PublishCheck
				{
					Label = "Release asset",
					State = PublishCheckState.Unknown,
					Detail = "The origin remote is not a github.com URL, so the release cannot be checked from here.",
				});
				return;
			}

			if (!tagPushed)
			{
				checks.Add(new PublishCheck
				{
					Label = "Release asset",
					State = PublishCheckState.Blocked,
					Detail = "Push the tag first - the release workflow runs on a v* tag.",
				});
				return;
			}

			try
			{
				var url = $"https://api.github.com/repos/{report.Repository}/releases/tags/{report.Tag}";
				var response = _http.GetAsync(url).GetAwaiter().GetResult();

				if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					checks.Add(new PublishCheck
					{
						Label = "Release asset",
						State = PublishCheckState.Blocked,
						Detail = $"No release for {report.Tag} yet. If you just pushed the tag, CI may still be building.",
						Fix = $"gh run watch --repo {report.Repository} --exit-status",
					});
					return;
				}

				if (!response.IsSuccessStatusCode)
				{
					checks.Add(new PublishCheck
					{
						Label = "Release asset",
						State = PublishCheckState.Unknown,
						Detail = $"GitHub returned {(int)response.StatusCode}. Anonymous requests are limited to 60 an hour.",
					});
					return;
				}

				var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				var hasZip = Regex.IsMatch(body, "\"name\"\\s*:\\s*\"[^\"]+\\.zip\"");

				checks.Add(hasZip
					? new PublishCheck { Label = "Release asset", State = PublishCheckState.Ok, Detail = $"{report.Tag} has a .zip asset." }
					: new PublishCheck
					{
						Label = "Release asset",
						State = PublishCheckState.Blocked,
						// The registry points at the zip, so a release without one cannot be installed.
						Detail = $"The {report.Tag} release exists but has no .zip attached, so nothing can install it.",
						Fix = $"gh run list --repo {report.Repository}",
					});
			}
			catch (Exception ex)
			{
				checks.Add(new PublishCheck
				{
					Label = "Release asset",
					State = PublishCheckState.Unknown,
					Detail = "Could not reach GitHub: " + ex.Message,
				});
			}
		}

		private static void CheckRegistry(PublishReport report, List<PublishCheck> checks)
		{
			if (!PluginRegistryIndex.HasEntries)
			{
				checks.Add(new PublishCheck
				{
					Label = "Registry listing",
					State = PublishCheckState.Unknown,
					Detail = "No registry loaded. Open Browse Plugins to fetch it.",
				});
				return;
			}

			var entry = PluginRegistryIndex.Entries.FirstOrDefault(e =>
				string.Equals(e.Id, report.PluginId, StringComparison.Ordinal));

			if (entry == null)
			{
				checks.Add(new PublishCheck
				{
					Label = "Registry listing",
					State = PublishCheckState.Blocked,
					Detail = "Not listed in any registry, so it cannot appear in Browse Plugins.",
					Fix = "The release runs a submit job that opens a pull request against the registry - merge it. " +
					      "That job needs a REGISTRY_TOKEN secret on this repository; without one it only prints the " +
					      "entry to add by hand, and says so in the run's warnings.",
				});
				return;
			}

			var listedTag = entry.Ref?.Trim();
			checks.Add(string.Equals(listedTag, report.Tag, StringComparison.Ordinal)
				? new PublishCheck
				{
					Label = "Registry listing",
					State = PublishCheckState.Ok,
					Detail = $"Listed at {listedTag} in {entry.RegistryName ?? "a registry"}.",
				}
				: new PublishCheck
				{
					Label = "Registry listing",
					State = PublishCheckState.Warning,
					Detail = $"Listed at {listedTag ?? "(no ref)"}, but this plugin is at {report.Tag}. Installs get the older version.",
					Fix = "Merge the registry pull request for this release. If the release ran without opening one, " +
					      "check the run for a warning about a missing REGISTRY_TOKEN secret.",
				});
		}

		/// <summary>github.com "owner/repo" for a remote URL, or null when it is not a GitHub remote.</summary>
		public static string ParseGitHubRepository(string remoteUrl)
		{
			var match = Regex.Match(remoteUrl, @"github\.com[:/]+([^/]+)/(.+?)(?:\.git)?/?$");
			return match.Success ? $"{match.Groups[1].Value}/{match.Groups[2].Value}" : null;
		}

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

		private static GitResult Git(string workingDir, string arguments)
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

				// A private remote would otherwise block on a credential prompt with no terminal to answer it.
				info.Environment["GIT_TERMINAL_PROMPT"] = "0";

				using var process = Process.Start(info);
				if (process == null)
					return new GitResult(null, "Could not start git. Is it installed and on PATH?", true);

				var stdout = process.StandardOutput.ReadToEndAsync();
				var stderr = process.StandardError.ReadToEndAsync();

				if (!process.WaitForExit(15000))
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
	}
}
