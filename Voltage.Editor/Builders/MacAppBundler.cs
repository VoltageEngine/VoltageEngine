using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Builders;

/// <summary>
/// Turns a macOS publish folder into <c>&lt;Project&gt;.app</c>: binaries in Contents/MacOS, MonoGame's libraries in
/// Contents/Frameworks, and the game's files in Contents/Resources, where the engine points the base directory.
/// </summary>
public static class MacAppBundler
{
	/// <summary>True for the macOS targets, which publish as an app bundle.</summary>
	public static bool Applies(BuildPlatform platform) => platform.RuntimeIdentifier.StartsWith("osx", StringComparison.Ordinal);

	/// <summary>The bundle a macOS build leaves in <paramref name="buildDir"/>.</summary>
	public static string AppPath(IGameProject project, string buildDir) => Path.Combine(buildDir, project.ProjectName + ".app");

	/// <summary>Moves the publish output into the bundle, then writes Info.plist, the icon and the signature.</summary>
	public static bool Package(IGameProject project, string buildDir, bool aot)
	{
		try
		{
			var app = AppPath(project, buildDir);
			var contents = Path.Combine(app, "Contents");
			var macOS = Path.Combine(contents, "MacOS");
			var frameworks = Path.Combine(contents, "Frameworks");
			var resources = Path.Combine(contents, "Resources");
			if (!IsMachO(Path.Combine(buildDir, project.ProjectName)))
			{
				Debug.Error($"No macOS executable named {project.ProjectName} in {buildDir}.", "MacAppBundler");
				return false;
			}

			Directory.CreateDirectory(macOS);
			Directory.CreateDirectory(frameworks);
			Directory.CreateDirectory(resources);

			foreach (var dir in Directory.GetDirectories(buildDir))
			{
				// Symbols stay beside the app so they don't ship inside it.
				if (Path.GetFileName(dir) == Path.GetFileName(app) || dir.EndsWith(".dsym", StringComparison.OrdinalIgnoreCase))
					continue;
				Directory.Move(dir, Path.Combine(resources, Path.GetFileName(dir)));
			}

			foreach (var file in Directory.GetFiles(buildDir))
			{
				var name = Path.GetFileName(file);
				string destination;
				if (IsMachO(file))
					destination = IsMonoGameLibrary(name) ? frameworks : macOS;
				else if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
				{
					// A native build has no managed code for these, so they stay beside the app too.
					if (aot) continue;
					destination = macOS;
				}
				else if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
				         name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
				         name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
					destination = macOS;
				else
					destination = resources;
				File.Move(file, Path.Combine(destination, name));
			}

			var icon = OperatingSystem.IsMacOS() && WriteIcon(project, Path.Combine(resources, project.ProjectName + ".icns"));
			File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(project, icon));

			if (OperatingSystem.IsMacOS())
				Sign(app);
			else
				EditorDebug.Warn($"{Path.GetFileName(app)} is unsigned: codesign only runs on macOS, and Apple Silicon Macs refuse unsigned code.", "MacAppBundler");

			EditorDebug.Log($"Packaged {app}", "MacAppBundler");
			return true;
		}
		catch (Exception ex)
		{
			Debug.Error($"Could not package the macOS app: {ex.Message}");
			return false;
		}
	}

	/// <summary>The executable inside the bundle, or null when there is no bundle.</summary>
	public static string FindExecutable(IGameProject project, string buildDir)
	{
		var exe = Path.Combine(AppPath(project, buildDir), "Contents", "MacOS", project.ProjectName);
		return File.Exists(exe) ? exe : null;
	}

	private static bool IsMonoGameLibrary(string name) =>
		name.StartsWith("libSDL2", StringComparison.Ordinal) || name.StartsWith("libopenal", StringComparison.Ordinal);

	private static bool IsMachO(string path)
	{
		if (!File.Exists(path))
			return false;
		var magic = new byte[4];
		using (var stream = File.OpenRead(path))
			if (stream.Read(magic, 0, 4) < 4)
				return false;
		var value = BitConverter.ToUInt32(magic, 0);
		return value is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE or 0xCAFEBABE or 0xBEBAFECA;
	}

	private static string InfoPlist(IGameProject project, bool hasIcon)
	{
		var info = File.Exists(Path.Combine(project.ProjectPath, "Properties", "AssemblyInfo.cs"))
			? File.ReadAllText(Path.Combine(project.ProjectPath, "Properties", "AssemblyInfo.cs"))
			: "";
		var company = AssemblyAttribute(info, "AssemblyCompany");
		var copyright = AssemblyAttribute(info, "AssemblyCopyright");
		var identifier = string.Join(".", new[] { "com", IdentifierPart(company), IdentifierPart(project.ProjectName) }.Where(p => p.Length > 0));
		var version = project.Version?.ToString() ?? "1.0";

		string Entry(string key, string value) => $"\t<key>{key}</key>\n\t<string>{SecurityElement.Escape(value)}</string>\n";

		return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
		       "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
		       "<plist version=\"1.0\">\n<dict>\n" +
		       Entry("CFBundleName", project.ProjectName) +
		       Entry("CFBundleDisplayName", project.ProjectName) +
		       Entry("CFBundleIdentifier", identifier) +
		       Entry("CFBundleExecutable", project.ProjectName) +
		       Entry("CFBundlePackageType", "APPL") +
		       Entry("CFBundleShortVersionString", version) +
		       Entry("CFBundleVersion", version) +
		       (hasIcon ? Entry("CFBundleIconFile", project.ProjectName) : "") +
		       (string.IsNullOrWhiteSpace(copyright) ? "" : Entry("NSHumanReadableCopyright", copyright)) +
		       Entry("LSApplicationCategoryType", "public.app-category.games") +
		       Entry("LSMinimumSystemVersion", "12.0") +
		       "\t<key>NSHighResolutionCapable</key>\n\t<true/>\n" +
		       "</dict>\n</plist>\n";
	}

	private static string AssemblyAttribute(string source, string attribute) =>
		Regex.Match(source, $@"\[assembly:\s*{attribute}\(\s*""(?<value>[^""]*)""\s*\)\]").Groups["value"].Value.Trim();

	private static string IdentifierPart(string text) =>
		Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9-]", "");

	/// <summary>Converts the project's Icon.ico (or Icon.bmp) to an .icns with sips and iconutil.</summary>
	private static bool WriteIcon(IGameProject project, string icns)
	{
		var source = new[] { "Icon.ico", "Icon.bmp" }.Select(n => Path.Combine(project.ProjectPath, n)).FirstOrDefault(File.Exists);
		if (source == null)
		{
			EditorDebug.Warn("No Icon.ico or Icon.bmp in the project; the app gets the default icon.", "MacAppBundler");
			return false;
		}

		var work = Path.Combine(Path.GetTempPath(), "voltage-icon-" + Guid.NewGuid().ToString("N"));
		var iconset = Path.Combine(work, "icon.iconset");
		try
		{
			Directory.CreateDirectory(iconset);
			var png = Path.Combine(work, "icon.png");
			if (!Run("/usr/bin/sips", "-s", "format", "png", source, "--out", png))
				return false;
			foreach (var size in new[] { 16, 32, 128 })
			{
				if (!Run("/usr/bin/sips", "-z", $"{size}", $"{size}", png, "--out", Path.Combine(iconset, $"icon_{size}x{size}.png")) ||
				    !Run("/usr/bin/sips", "-z", $"{size * 2}", $"{size * 2}", png, "--out", Path.Combine(iconset, $"icon_{size}x{size}@2x.png")))
					return false;
			}
			File.Copy(png, Path.Combine(iconset, "icon_256x256.png"));
			return Run("/usr/bin/iconutil", "-c", "icns", iconset, "-o", icns);
		}
		finally
		{
			try { Directory.Delete(work, true); } catch (IOException) { }
		}
	}

	/// <summary>Signs with the identity in the engine's mac-signing-identity.txt, like the editor app, or ad-hoc without one.</summary>
	private static void Sign(string app)
	{
		var identity = SigningIdentity();
		if (identity != null && Run("/usr/bin/codesign", "--force", "--deep", "--sign", identity, app))
			return;
		if (identity != null)
			EditorDebug.Warn($"Signing with \"{identity}\" failed; signing ad-hoc instead.", "MacAppBundler");
		if (!Run("/usr/bin/codesign", "--force", "--deep", "--sign", "-", app))
			EditorDebug.Warn($"Could not sign {Path.GetFileName(app)}; Apple Silicon Macs will refuse to open it.", "MacAppBundler");
	}

	private static string SigningIdentity()
	{
		var file = Path.Combine(GameBuilder.FindEditorProjectDir(), "mac-signing-identity.txt");
		return File.Exists(file)
			? File.ReadLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'))
			: null;
	}

	private static bool Run(string tool, params string[] arguments)
	{
		var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		using var process = Process.Start(info);
		if (process == null)
			return false;
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode == 0)
			return true;
		EditorDebug.Warn($"{tool} failed (exit code {process.ExitCode}): {(error + output.Result).Trim()}", "MacAppBundler");
		return false;
	}
}
