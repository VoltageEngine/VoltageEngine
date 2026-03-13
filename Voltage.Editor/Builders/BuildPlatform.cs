using System;
using System.Collections.Generic;
using System.Linq;

namespace Voltage.Editor.Builders;

/// <summary>
/// Defines a target platform for game builds.
/// This is the single source of truth for available platforms — both the .csproj template
/// and the GameBuildWindow read from this list, ensuring they are always in sync.
/// </summary>
public class BuildPlatform
{
	/// <summary>Display name shown in the Build Game popup.</summary>
	public string DisplayName { get; init; }

	/// <summary>.NET Runtime Identifier passed to dotnet publish via -r.</summary>
	public string RuntimeIdentifier { get; init; }

	/// <summary>Short suffix appended to the Build output folder (e.g. "Build/win-x64").</summary>
	public string FolderSuffix { get; init; }

	/// <summary>If false, the platform is shown but greyed out and cannot be selected.</summary>
	public bool IsAvailable { get; init; }

	/// <summary>Tooltip explaining why the platform is unavailable, if applicable.</summary>
	public string UnavailableReason { get; init; }

	/// <summary>
	/// The canonical list of all supported build platforms.
	/// DesktopGL (MonoGame) supports Windows, Linux, and macOS out of the box.
	/// Console platforms are listed but marked unavailable until native toolchains are integrated.
	/// </summary>
	public static readonly IReadOnlyList<BuildPlatform> All = new List<BuildPlatform>
	{
		new()
		{
			DisplayName = "Windows (x64)",
			RuntimeIdentifier = "win-x64",
			FolderSuffix = "win-x64",
			IsAvailable = true,
			UnavailableReason = null
		},
		new()
		{
			DisplayName = "Linux (x64)",
			RuntimeIdentifier = "linux-x64",
			FolderSuffix = "linux-x64",
			IsAvailable = true,
			UnavailableReason = null
		},
		new()
		{
			DisplayName = "macOS (x64)",
			RuntimeIdentifier = "osx-x64",
			FolderSuffix = "osx-x64",
			IsAvailable = true,
			UnavailableReason = null
		},
		new()
		{
			DisplayName = "macOS (ARM64 / Apple Silicon)",
			RuntimeIdentifier = "osx-arm64",
			FolderSuffix = "osx-arm64",
			IsAvailable = true,
			UnavailableReason = null
		},
		new()
		{
			DisplayName = "Nintendo Switch",
			RuntimeIdentifier = "switch",
			FolderSuffix = "switch",
			IsAvailable = false,
			UnavailableReason = "Requires NintendoSDK and platform-specific MonoGame backend. Not yet supported."
		},
		new()
		{
			DisplayName = "PlayStation 4/5",
			RuntimeIdentifier = "ps",
			FolderSuffix = "ps",
			IsAvailable = false,
			UnavailableReason = "Requires PlayStation Partners SDK and platform-specific MonoGame backend. Not yet supported."
		},
		new()
		{
			DisplayName = "Xbox (GDK)",
			RuntimeIdentifier = "xbox",
			FolderSuffix = "xbox",
			IsAvailable = false,
			UnavailableReason = "Requires Microsoft GDK and platform-specific MonoGame backend. Not yet supported."
		}
	};

	/// <summary>
	/// Returns only the platforms that are currently available for building.
	/// </summary>
	public static IEnumerable<BuildPlatform> Available => All.Where(p => p.IsAvailable);

	/// <summary>
	/// Returns the default platform (Windows x64).
	/// </summary>
	public static BuildPlatform Default => All[0];
}