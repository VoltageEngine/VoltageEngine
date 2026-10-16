using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Voltage.Cinematics;

namespace Voltage.Editor.Windows;

public enum TimelineSwitchChoice { Save, Discard, Cancel }

/// <summary>A detached timeline working copy with guarded transitions.</summary>
public sealed class TimelineDocument
{
    private string _savedJson;
    private Action _pending;
    public TimelineAsset Asset { get; private set; }
    public string Path { get; private set; }
    public string Error { get; private set; }
    public bool HasPendingSwitch => _pending != null;
    public bool IsDirty => Asset != null && TimelineAssetIO.ToJson(Asset) != _savedJson;

    public bool RequestOpen(string path, string projectRoot, Action activated = null)
    {
        try
        {
            path = ValidatePath(path, projectRoot);
            var asset = TimelineAssetIO.Load(path) ?? throw new InvalidDataException("The timeline is empty.");
            return Request(asset, path, activated);
        }
        catch (Exception ex) { Error = "Load failed: " + ex.Message; return false; }
    }

    public bool Request(TimelineAsset asset, string path, Action activated = null)
    {
        if (HasPendingSwitch) return false;
        if (asset != null) ValidateAsset(asset);
        var reuseSavedCopy = asset != null && Asset != null && IsDirty && !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(Path) &&
            string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(Path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var json = asset == null ? null : TimelineAssetIO.ToJson(asset);
        return RequestTransition(() =>
        {
            var nextJson = reuseSavedCopy ? _savedJson : json;
            Asset = nextJson == null ? null : TimelineAssetIO.FromJson(nextJson);
            Path = path;
            _savedJson = nextJson;
            Error = null;
            activated?.Invoke();
        });
    }

    public bool RequestTransition(Action action)
    {
        if (HasPendingSwitch) return false;
        Error = null;
        if (IsDirty) { _pending = action; return false; }
        action();
        return true;
    }

    public bool Resolve(TimelineSwitchChoice choice)
    {
        if (_pending == null) return false;
        if (choice == TimelineSwitchChoice.Cancel) { _pending = null; return false; }
        if (choice == TimelineSwitchChoice.Save && !TrySave()) return false;
        var transition = _pending;
        _pending = null;
        if (choice == TimelineSwitchChoice.Discard && Asset != null) Asset = TimelineAssetIO.FromJson(_savedJson);
        transition();
        return true;
    }

    public bool TrySave()
    {
        if (Asset == null || string.IsNullOrEmpty(Path)) { Error = "Save failed: no timeline file is assigned."; return false; }
        string temporary = null;
        try
        {
            ValidateAsset(Asset);
            var json = TimelineAssetIO.ToJson(Asset);
            temporary = Path + ".saving-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, json);
            File.Move(temporary, Path, true);
            _savedJson = json;
            Error = null;
            TimelineNestedTrack.ClearCache();
            return true;
        }
        catch (Exception ex) { Error = "Save failed: " + ex.Message; return false; }
        finally { if (temporary != null && File.Exists(temporary)) { try { File.Delete(temporary); } catch { } } }
    }

    private static void ValidateAsset(TimelineAsset asset)
    {
        if (!float.IsFinite(asset.Duration) || asset.Duration <= 0) throw new InvalidDataException("Timeline duration must be finite and positive.");
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object value)
        {
            if (value == null) return;
            if (value is float number) { if (!float.IsFinite(number)) throw new InvalidDataException("Timeline values must be finite."); return; }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is Guid) return;
            if (!type.IsValueType && !seen.Add(value)) return;
            if (value is IList list)
            {
                foreach (var item in list) { if (item == null) throw new InvalidDataException("Timeline lists cannot contain empty items."); Visit(item); }
                return;
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var member = field.GetValue(value);
                if (typeof(IList).IsAssignableFrom(field.FieldType) && member == null) throw new InvalidDataException("Timeline list '" + field.Name + "' is missing.");
                Visit(member);
            }
        }
        Visit(asset);
    }

    private static string ValidatePath(string path, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(projectRoot)) throw new InvalidDataException("Open a project and choose a timeline file.");
        var root = System.IO.Path.GetFullPath(projectRoot);
        var full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path));
        var relative = System.IO.Path.GetRelativePath(root, full);
        if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            throw new InvalidDataException("The timeline must be inside the current project.");
        if (!full.EndsWith(TimelineAssetIO.FileExtension, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidDataException("Choose an existing .vtimeline file.");
        return full;
    }
}
