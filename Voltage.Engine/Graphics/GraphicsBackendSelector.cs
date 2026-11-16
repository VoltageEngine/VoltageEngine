using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Voltage;

/// <summary>A graphics backend a player or developer can choose; OpenGL is MonoGame's OpenGL (compatibility) build, the rest its native runtimes.</summary>
public enum GraphicsApi { Auto, DirectX12, Vulkan, OpenGL4, OpenGL }

/// <summary>Chooses MonoGame's graphics backend at startup and reports which backends this platform and build can use.</summary>
public static class GraphicsBackendSelector
{
	public const string EnvironmentVariable = "VOLTAGE_GRAPHICS_BACKEND";
	public const string Argument = "--graphics-backend";

	/// <summary>Every choice a settings screen offers, in display order.</summary>
	public static readonly GraphicsApi[] Choices = Enum.GetValues<GraphicsApi>();

	/// <summary>True when this build carries MonoGame's native runtimes (VoltageGraphics=Native) instead of its OpenGL build.</summary>
	public static bool NativeBuild =>
#if VOLTAGE_NATIVE_GRAPHICS
		true;
#else
		false;
#endif

	/// <summary>The native backend loaded at startup, or null in OpenGL builds.</summary>
	public static GraphicsApi? Selected { get; private set; }

	/// <summary>The backend this process renders with.</summary>
	public static GraphicsApi Running => NativeBuild ? Selected ?? GraphicsApi.Auto : GraphicsApi.OpenGL;

	/// <summary>Why each skipped backend was skipped, for the log.</summary>
	public static IReadOnlyList<string> Skipped => _skipped;

	/// <summary>Where the per-user choice lives; the editor moves it into its own data folder before the engine starts.</summary>
	public static string PreferenceFile { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		Assembly.GetEntryAssembly()?.GetName().Name ?? "Voltage", "GraphicsBackend.txt");

	/// <summary>The per-user choice, applied on the next start.</summary>
	public static GraphicsApi Preference
	{
		get
		{
			try { return File.Exists(PreferenceFile) ? Parse(File.ReadAllText(PreferenceFile)) ?? GraphicsApi.Auto : GraphicsApi.Auto; }
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return GraphicsApi.Auto; }
		}
		set
		{
			Directory.CreateDirectory(Path.GetDirectoryName(PreferenceFile));
			File.WriteAllText(PreferenceFile, value.ToString());
			ForgetFailure(value);
		}
	}

	internal static IntPtr Handle { get; private set; }

	static readonly List<string> _skipped = new();
	static readonly Dictionary<GraphicsApi, string> _probes = new();
	static string _root;

	static string BackendsRoot => Path.Combine(AppContext.BaseDirectory, "backends");

	static string LibraryName => OperatingSystem.IsWindows() ? "mgruntime.dll" : OperatingSystem.IsMacOS() ? "libmgruntime.dylib" : "libmgruntime.so";

	static string Folder(GraphicsApi api) => api.ToString().ToLowerInvariant();

	static bool IsNative(GraphicsApi api) => api is GraphicsApi.DirectX12 or GraphicsApi.Vulkan or GraphicsApi.OpenGL4;

	/// <summary>Preference order when nothing is chosen: the newest API with the most mature runtime first, OpenGL 4 last.</summary>
	public static GraphicsApi[] AutoOrder => OperatingSystem.IsWindows()
		? new[] { GraphicsApi.DirectX12, GraphicsApi.Vulkan, GraphicsApi.OpenGL4 }
		: new[] { GraphicsApi.Vulkan, GraphicsApi.OpenGL4 };

	public static string Label(GraphicsApi api) => api switch
	{
		GraphicsApi.DirectX12 => "DirectX 12",
		GraphicsApi.Vulkan => "Vulkan",
		GraphicsApi.OpenGL4 => "OpenGL 4",
		GraphicsApi.OpenGL => "OpenGL (compatibility)",
		_ => "Automatic"
	};

	/// <summary>Null when the choice can run here, otherwise why not. With <paramref name="gameBuild"/> it answers for a game made by Build Game.</summary>
	public static string Unavailable(GraphicsApi api, bool gameBuild = false)
	{
		if (api == GraphicsApi.Auto) return null;
		if (api == GraphicsApi.OpenGL)
			return NativeBuild && !gameBuild ? "This build runs MonoGame's native renderers. OpenGL (compatibility) is a separate build." : null;
		if (api == GraphicsApi.DirectX12 && !OperatingSystem.IsWindows()) return "DirectX 12 needs Windows.";
		if (gameBuild) return "Build Game does not include the native renderers yet, so games run on OpenGL (compatibility).";
		if (!NativeBuild) return "Not included in this build, which uses MonoGame's OpenGL (compatibility) renderer.";
		if (!File.Exists(Path.Combine(BackendsRoot, Folder(api), LibraryName))) return "Not included in this build for this platform.";
		lock (_probes)
		{
			if (!_probes.TryGetValue(api, out var reason))
				_probes[api] = reason = Probe(api);
			return reason;
		}
	}

	/// <summary>True when the backend died while starting on this machine, so Automatic skips it.</summary>
	public static bool FailedBefore(GraphicsApi api)
	{
		try
		{
			var failed = Path.Combine(BackendsRoot, "failed.txt");
			return File.Exists(failed) && File.ReadAllLines(failed).Any(line => Parse(line) == api);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
	}

	/// <summary>Loads the first usable runtime from the backends folder. Must run before MonoGame makes a native call.</summary>
	public static void Select(string[] args, string root = null)
	{
		if (Handle != IntPtr.Zero) return;
		root ??= BackendsRoot;
		if (!Directory.Exists(root)) return;

		var forced = Native(Requested(args, Environment.GetEnvironmentVariable(EnvironmentVariable)));
		var preferred = forced ?? Native(Preference) ?? Native(ProjectDefault());
		var crashed = TakeCrashed(root);
		foreach (var api in Order(preferred))
		{
			var path = Path.Combine(root, Folder(api), LibraryName);
			var reason = !File.Exists(path) ? "not shipped with this build"
				: api != forced && crashed.Contains(api) ? $"crashed on an earlier start; pass {Argument} {api} to retry"
				: Probe(api);
			if (reason == null)
			{
				if (NativeLibrary.TryLoad(path, out var handle))
				{
					Handle = handle;
					Selected = api;
					_root = root;
					TryWrite(Path.Combine(root, "pending.txt"), api.ToString());
					NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, (library, _, _) => library == "mgruntime" ? Handle : IntPtr.Zero);
					return;
				}
				reason = "its runtime library failed to load";
			}
			_skipped.Add($"{api}: {reason}");
		}
		throw new PlatformNotSupportedException("No graphics backend could be loaded. " + string.Join("; ", _skipped));
	}

	/// <summary>Call once the graphics device exists: clears the crash marker and reports the choice.</summary>
	public static void Confirm()
	{
		if (_root == null) return;
		try { File.Delete(Path.Combine(_root, "pending.txt")); }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		Debug.Log($"[Graphics] Using {Selected}" + (_skipped.Count > 0 ? "; skipped " + string.Join("; ", _skipped) : ""));
		_root = null;
	}

	/// <summary>The preferred backend first when there is one, then the automatic order.</summary>
	public static GraphicsApi[] Order(GraphicsApi? preferred) =>
		(preferred == null ? AutoOrder : new[] { preferred.Value }.Concat(AutoOrder)).Distinct().ToArray();

	/// <summary>Reads --graphics-backend X, --graphics-backend=X or the environment value; null when neither names a choice.</summary>
	public static GraphicsApi? Requested(string[] args, string environment)
	{
		string value = environment;
		for (int i = 0; i < (args?.Length ?? 0); i++)
		{
			if (args[i] == Argument && i + 1 < args.Length) value = args[i + 1];
			else if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal)) value = args[i].Substring(Argument.Length + 1);
		}
		return Parse(value);
	}

	/// <summary>The choice a name spells, ignoring case and spaces ("directx 12"), or null. Numbers are not names.</summary>
	public static GraphicsApi? Parse(string value)
	{
		var name = value?.Replace(" ", "").Trim();
		return !string.IsNullOrEmpty(name) && char.IsLetter(name[0]) && Enum.TryParse<GraphicsApi>(name, true, out var api) && Enum.IsDefined(api) ? api : null;
	}

	static GraphicsApi? Native(GraphicsApi? api) => api is { } value && IsNative(value) ? value : null;

	/// <summary>Choosing a backend that crashed before gives it one more try; a second crash marks it again.</summary>
	static void ForgetFailure(GraphicsApi api)
	{
		try
		{
			var failed = Path.Combine(BackendsRoot, "failed.txt");
			if (File.Exists(failed)) File.WriteAllLines(failed, File.ReadAllLines(failed).Where(line => Parse(line) != api));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	/// <summary>Rendering.GraphicsBackend from the ProjectSettings.json a built game ships beside its executable.</summary>
	static GraphicsApi? ProjectDefault()
	{
		try
		{
			var path = Path.Combine(AppContext.BaseDirectory, "ProjectSettings.json");
			if (!File.Exists(path)) return null;
			using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			return json.RootElement.TryGetProperty("Rendering", out var rendering) && rendering.TryGetProperty("GraphicsBackend", out var backend)
				&& backend.ValueKind == JsonValueKind.String ? Parse(backend.GetString()) : null;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
	}

	/// <summary>A backend still marked pending never reached Confirm, so its last start died in device creation.</summary>
	static HashSet<GraphicsApi> TakeCrashed(string root)
	{
		string pending = Path.Combine(root, "pending.txt"), failed = Path.Combine(root, "failed.txt");
		var crashed = new HashSet<GraphicsApi>();
		try
		{
			if (File.Exists(failed))
				foreach (var line in File.ReadAllLines(failed))
					if (Parse(line) is { } api) crashed.Add(api);
			if (File.Exists(pending) && Parse(File.ReadAllText(pending)) is { } last && crashed.Add(last))
				File.WriteAllLines(failed, crashed.Select(api => api.ToString()));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		return crashed;
	}

	// ponytail: without write access to the backends folder there is no crash memory; move the marker to user data if installs become read-only.
	static void TryWrite(string path, string text)
	{
		try { File.WriteAllText(path, text); }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	static string Probe(GraphicsApi api)
	{
		if (api == GraphicsApi.DirectX12)
			return !OperatingSystem.IsWindows() ? "DirectX 12 needs Windows." : Direct3D12Supported() ? null : "No graphics card or driver with DirectX 12 support was found.";
		if (api == GraphicsApi.Vulkan && !OperatingSystem.IsMacOS())
			return NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1", typeof(GraphicsBackendSelector).Assembly,
				DllImportSearchPath.System32, out _) ? null : "No Vulkan driver is installed.";
		return null;
	}

	[DllImport("d3d12.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	static extern int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, in Guid interfaceId, IntPtr device);

	/// <summary>With a null output pointer the call only reports whether a device could be created.</summary>
	static bool Direct3D12Supported()
	{
		try { return D3D12CreateDevice(IntPtr.Zero, 0xb000, new Guid("189819f1-1db6-4b57-be54-1821339b85f7"), IntPtr.Zero) >= 0; }
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
	}
}
