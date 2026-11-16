using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace Voltage;

/// <summary>Picks the MonoGame native runtime for builds made with VoltageGraphics=Native; does nothing in DesktopGL builds.</summary>
public static class GraphicsBackendSelector
{
	public const string EnvironmentVariable = "VOLTAGE_GRAPHICS_BACKEND";
	public const string Argument = "--graphics-backend";
	public const string DirectX12 = "DirectX12", Vulkan = "Vulkan", OpenGL4 = "OpenGL4";

	/// <summary>The loaded backend, or null when the build ships no selectable runtimes.</summary>
	public static string Selected { get; private set; }

	/// <summary>Why each skipped backend was skipped, for the log.</summary>
	public static IReadOnlyList<string> Skipped => _skipped;

	internal static IntPtr Handle { get; private set; }

	static readonly List<string> _skipped = new();
	static string _root;

	static string LibraryName => OperatingSystem.IsWindows() ? "mgruntime.dll" : OperatingSystem.IsMacOS() ? "libmgruntime.dylib" : "libmgruntime.so";

	/// <summary>Preference order when nothing is requested: the newest API with the most mature runtime first, OpenGL 4 last.</summary>
	public static string[] AutoOrder => OperatingSystem.IsWindows() ? new[] { DirectX12, Vulkan, OpenGL4 } : new[] { Vulkan, OpenGL4 };

	/// <summary>Loads the first usable runtime from the backends folder. Must run before MonoGame makes a native call.</summary>
	public static void Select(string[] args, string root = null)
	{
		if (Handle != IntPtr.Zero) return;
		root ??= Path.Combine(AppContext.BaseDirectory, "backends");
		if (!Directory.Exists(root)) return;

		var requested = Requested(args, Environment.GetEnvironmentVariable(EnvironmentVariable));
		var crashed = TakeCrashed(root);
		foreach (var name in Order(requested))
		{
			var path = Path.Combine(root, name.ToLowerInvariant(), LibraryName);
			var reason = !File.Exists(path) ? "not shipped with this build"
				: name != requested && crashed.Contains(name) ? $"crashed on an earlier start; pass {Argument} {name} to retry"
				: Probe(name);
			if (reason == null)
			{
				if (NativeLibrary.TryLoad(path, out var handle))
				{
					Handle = handle;
					Selected = name;
					_root = root;
					TryWrite(Path.Combine(root, "pending.txt"), name);
					NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, (library, _, _) => library == "mgruntime" ? Handle : IntPtr.Zero);
					return;
				}
				reason = "its runtime library failed to load";
			}
			_skipped.Add($"{name}: {reason}");
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

	/// <summary>The requested backend first when it names a known one, then the automatic order.</summary>
	public static string[] Order(string requested) =>
		(requested == null ? AutoOrder : new[] { requested }.Concat(AutoOrder)).Distinct().ToArray();

	/// <summary>Reads --graphics-backend X, --graphics-backend=X or the environment value; unknown names mean automatic.</summary>
	public static string Requested(string[] args, string environment)
	{
		string value = environment;
		for (int i = 0; i < (args?.Length ?? 0); i++)
		{
			if (args[i] == Argument && i + 1 < args.Length) value = args[i + 1];
			else if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal)) value = args[i].Substring(Argument.Length + 1);
		}
		return new[] { DirectX12, Vulkan, OpenGL4 }.FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>A backend still marked pending never reached Confirm, so its last start died in device creation.</summary>
	static HashSet<string> TakeCrashed(string root)
	{
		string pending = Path.Combine(root, "pending.txt"), failed = Path.Combine(root, "failed.txt");
		var crashed = new HashSet<string>(StringComparer.Ordinal);
		try
		{
			if (File.Exists(failed)) crashed.UnionWith(File.ReadAllLines(failed));
			if (File.Exists(pending) && crashed.Add(File.ReadAllText(pending).Trim()))
				File.WriteAllLines(failed, crashed);
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

	static string Probe(string name)
	{
		if (name == DirectX12)
			return !OperatingSystem.IsWindows() ? "Windows only" : Direct3D12Supported() ? null : "no Direct3D 12 device at feature level 11_0";
		if (name == Vulkan && !OperatingSystem.IsMacOS())
			return NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1", typeof(GraphicsBackendSelector).Assembly,
				DllImportSearchPath.System32, out _) ? null : "no Vulkan loader installed";
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
