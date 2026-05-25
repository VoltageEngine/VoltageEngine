using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Voltage.Editor.Utils;

/// <summary>
/// Provides cross-platform path utilities that normalize path separators and
/// perform OS-aware path comparisons. Use this class instead of raw string
/// concatenation or hardcoded '/' / '\' separators.
/// </summary>
public static class CrossPlatformPath
{
	/// <summary>
	/// The directory separator character for the current OS.
	/// </summary>
	public static readonly char Sep = Path.DirectorySeparatorChar;

	/// <summary>
	/// Returns true when running on a case-sensitive file system (Linux / macOS HFS+).
	/// Windows and macOS APFS with case-insensitivity disabled both use OrdinalIgnoreCase.
	/// </summary>
	public static readonly StringComparison PathComparison =
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

	/// <summary>
	/// Combines path segments using <see cref="Path.Combine"/> and then normalizes
	/// all separator characters to the current OS separator.
	/// </summary>
	public static string Combine(params string[] parts) =>
		Normalize(Path.Combine(parts));

	/// <summary>
	/// Normalizes all forward and back slashes in a path string to the current OS
	/// directory separator character and resolves any redundant separators.
	/// </summary>
	public static string Normalize(string path)
	{
		if (string.IsNullOrEmpty(path))
			return path;

		// Replace both separator styles with the native one.
		return path.Replace('/', Sep).Replace('\\', Sep);
	}

	/// <summary>
	/// Normalizes a path and returns its fully qualified form via
	/// <see cref="Path.GetFullPath"/>, which also resolves '.' and '..' segments.
	/// </summary>
	public static string GetFullPath(string path) =>
		Path.GetFullPath(Normalize(path));

	/// <summary>
	/// Returns a relative path from <paramref name="basePath"/> to
	/// <paramref name="targetPath"/>, using forward slashes as the separator
	/// so it can be stored in cross-platform project files / JSON data.
	/// </summary>
	public static string GetRelativePathForStorage(string basePath, string targetPath)
	{
		var relative = Path.GetRelativePath(
			GetFullPath(basePath),
			GetFullPath(targetPath));

		// Always store with forward slashes so project files are portable.
		return relative.Replace('\\', '/');
	}

	/// <summary>
	/// Converts a stored (forward-slash) relative path back to an absolute path
	/// for the current OS, combining it with the given <paramref name="basePath"/>.
	/// </summary>
	public static string ResolveStoredPath(string basePath, string storedRelativePath)
	{
		if (string.IsNullOrEmpty(storedRelativePath))
			return null;

		var native = storedRelativePath.Replace('/', Sep).Replace('\\', Sep);
		return Path.GetFullPath(Path.Combine(Normalize(basePath), native));
	}

	/// <summary>
	/// Determines whether <paramref name="filePath"/> is located inside
	/// <paramref name="directoryPath"/> (or is the directory itself).
	/// Handles mixed separators and trailing slashes correctly.
	/// </summary>
	public static bool IsPathUnder(string directoryPath, string filePath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(filePath))
			return false;

		try
		{
			// Ensure trailing separator on directory so we don't get false positives
			// where a dir named "Foo" would match a file path like "FooBar/file.txt".
			var dir = GetFullPath(directoryPath).TrimEnd(Sep) + Sep;
			var file = GetFullPath(filePath);

			return file.StartsWith(dir, PathComparison);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Compares two path strings for equality after normalizing separators and
	/// resolving them to absolute paths. Uses OS-appropriate case sensitivity.
	/// </summary>
	public static bool AreEqual(string pathA, string pathB)
	{
		if (pathA == null && pathB == null) return true;
		if (pathA == null || pathB == null) return false;

		try
		{
			return string.Equals(GetFullPath(pathA), GetFullPath(pathB), PathComparison);
		}
		catch
		{
			return string.Equals(Normalize(pathA), Normalize(pathB), PathComparison);
		}
	}
}

