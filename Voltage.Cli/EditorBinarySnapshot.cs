using System;
using System.IO;

namespace Voltage.Cli;

/// <summary>A private editor binary copy that leaves the normal build output unlocked.</summary>
public sealed class EditorBinarySnapshot : IDisposable
{
    public const string SourceExeArgument = "--gateway-source-exe";
    private static string RunsDirectory => Path.Combine(Path.GetTempPath(), "voltage-editor-runs");
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string _directory;
    public string Executable { get; }

    private EditorBinarySnapshot(string directory, string executable)
    {
        _directory = directory;
        Executable = executable;
    }

    public static EditorBinarySnapshot Create(string executable, string directory = null)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Editor executable not found", executable);
        var source = Path.GetDirectoryName(executable);
        directory = Path.GetFullPath(directory ?? Path.Combine(RunsDirectory, Guid.NewGuid().ToString("N")));
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(temporary, PathComparison) || Directory.Exists(directory) ||
            directory.StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
            throw new ArgumentException("A snapshot needs a new temporary directory outside the build output", nameof(directory));

        var snapshot = new EditorBinarySnapshot(directory, Path.Combine(directory, Path.GetFileName(executable)));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (var file in Directory.EnumerateFiles(source, "*", options))
            {
                var target = Path.Combine(directory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
                File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
            }
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    /// <summary>Removes only a CLI-owned snapshot after its recorded process has exited.</summary>
    public static void Remove(string executable, string sourceExecutable)
    {
        if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(sourceExecutable)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable));
        if (directory == null || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
            !string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(RunsDirectory), PathComparison)) return;
        new EditorBinarySnapshot(directory, executable).Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory) && (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) == 0)
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(_directory, "*", options))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
