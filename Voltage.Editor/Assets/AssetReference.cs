using System;

namespace Voltage.Editor.Assets
{
    /// <summary>
    /// A move/rename-safe reference to a project asset.
    ///
    /// Resolution priority (see <see cref="AssetDatabase.Resolve"/>):
    ///   1. GUID lookup in the in-memory <see cref="AssetDatabase"/> map (most reliable).
    ///   2. <see cref="HintPath"/> fallback (project-relative forward-slash path) when the
    ///      GUID is not found — this self-heals if the file is still on disk at the hinted
    ///      location.
    ///
    /// <c>HintPath</c> is intentionally never empty so that old data (pre-Phase-3 scenes that
    /// lack a GUID) can still resolve via the path-only fallback.
    ///
    /// Phase-3 seam for type/namespace-rename resilience: when <c>[FormerlyKnownAs]</c> and
    /// a <c>TypeRenameRegistry</c> are added, the registry lookup lives in
    /// <see cref="AssetDatabase.Resolve"/> — no change needed here.
    /// </summary>
    public readonly struct AssetReference : IEquatable<AssetReference>
    {
        /// <summary>Stable, per-file GUID written into the companion <c>.meta</c> sidecar.</summary>
        public Guid Guid { get; }

        /// <summary>
        /// Project-relative path using forward slashes (e.g. <c>Content/Sprites/Player.png</c>).
        /// Acts as a human-readable fallback when the GUID cannot be matched.
        /// </summary>
        public string HintPath { get; }

        /// <summary>True when this instance carries neither a GUID nor a hint path.</summary>
        public bool IsEmpty => Guid == Guid.Empty && string.IsNullOrEmpty(HintPath);

        public AssetReference(Guid guid, string hintPath)
        {
            Guid     = guid;
            HintPath = hintPath ?? string.Empty;
        }

        /// <summary>Creates an <see cref="AssetReference"/> with only a hint path (no GUID yet).</summary>
        public static AssetReference FromHintPath(string hintPath) =>
            new AssetReference(Guid.Empty, hintPath);

        /// <summary>An unset / invalid reference.</summary>
        public static AssetReference Empty => new AssetReference(Guid.Empty, string.Empty);

        public bool Equals(AssetReference other) =>
            Guid == other.Guid && string.Equals(HintPath, other.HintPath, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) =>
            obj is AssetReference other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Guid, HintPath?.ToLowerInvariant());

        public static bool operator ==(AssetReference left, AssetReference right) => left.Equals(right);
        public static bool operator !=(AssetReference left, AssetReference right) => !left.Equals(right);

        public override string ToString() =>
            Guid != Guid.Empty
                ? $"AssetReference({Guid}, hint='{HintPath}')"
                : $"AssetReference(hint='{HintPath}')";
    }
}
