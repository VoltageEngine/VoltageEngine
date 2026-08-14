using System;

namespace Voltage.Editor.Plugins
{
	/// <summary>Minimal semver range matcher: "*", &gt;=, &gt;, &lt;=, &lt;, a bare exact version, and space-separated conjunctions. Pre-release suffixes compare ordinally after the numeric parts.</summary>
	public static class SemVerRange
	{
		/// <summary>True when version satisfies range. Empty or "*" matches everything; malformed input is false.</summary>
		public static bool Satisfies(string version, string range)
		{
			if (string.IsNullOrWhiteSpace(range) || range.Trim() == "*")
				return true;

			if (!TryParseVersion(version, out var v))
				return false;

			foreach (var clause in range.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!ClauseSatisfied(v, clause))
					return false;
			}

			return true;
		}

		/// <summary>Splits a version, tolerating a leading "v" and a "-prerelease" suffix. Missing parts read as 0.</summary>
		public static bool TryParse(string version, out int major, out int minor, out int patch, out string prerelease)
		{
			major = minor = patch = 0;
			prerelease = null;

			if (!TryParseVersion(version, out var parsed))
				return false;

			major = parsed.major;
			minor = parsed.minor;
			patch = parsed.patch;
			prerelease = string.IsNullOrEmpty(parsed.pre) ? null : parsed.pre;
			return true;
		}

		/// <summary>Orders two versions. False when either side is not a version, since a ref like "main" has no ordering.</summary>
		public static bool TryCompare(string a, string b, out int comparison)
		{
			comparison = 0;
			if (!TryParseVersion(a, out var va) || !TryParseVersion(b, out var vb))
				return false;

			comparison = Compare(va, vb);
			return true;
		}

		/// <summary>True when candidate is strictly newer. Unorderable input is never newer.</summary>
		public static bool IsNewer(string candidate, string current) =>
			TryCompare(candidate, current, out var comparison) && comparison > 0;

		private static bool ClauseSatisfied((int major, int minor, int patch, string pre) v, string clause)
		{
			string op;
			string versionPart;

			if (clause.StartsWith(">=", StringComparison.Ordinal)) { op = ">="; versionPart = clause.Substring(2); }
			else if (clause.StartsWith("<=", StringComparison.Ordinal)) { op = "<="; versionPart = clause.Substring(2); }
			else if (clause.StartsWith(">", StringComparison.Ordinal)) { op = ">"; versionPart = clause.Substring(1); }
			else if (clause.StartsWith("<", StringComparison.Ordinal)) { op = "<"; versionPart = clause.Substring(1); }
			else if (clause.StartsWith("=", StringComparison.Ordinal)) { op = "="; versionPart = clause.Substring(1); }
			else { op = "="; versionPart = clause; }

			if (!TryParseVersion(versionPart, out var bound))
				return false;

			var cmp = Compare(v, bound);
			return op switch
			{
				">=" => cmp >= 0,
				"<=" => cmp <= 0,
				">" => cmp > 0,
				"<" => cmp < 0,
				_ => cmp == 0,
			};
		}

		private static int Compare((int major, int minor, int patch, string pre) a, (int major, int minor, int patch, string pre) b)
		{
			if (a.major != b.major) return a.major.CompareTo(b.major);
			if (a.minor != b.minor) return a.minor.CompareTo(b.minor);
			if (a.patch != b.patch) return a.patch.CompareTo(b.patch);

			// Per semver, a release (no pre-release tag) is higher than any pre-release of the same triple.
			var aHasPre = !string.IsNullOrEmpty(a.pre);
			var bHasPre = !string.IsNullOrEmpty(b.pre);
			if (aHasPre != bHasPre) return aHasPre ? -1 : 1;
			return string.CompareOrdinal(a.pre ?? "", b.pre ?? "");
		}

		private static bool TryParseVersion(string input, out (int major, int minor, int patch, string pre) result)
		{
			result = default;
			if (string.IsNullOrWhiteSpace(input))
				return false;

			var text = input.Trim();
			if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
				text = text.Substring(1);

			var pre = "";
			var dashIdx = text.IndexOf('-');
			if (dashIdx >= 0)
			{
				pre = text.Substring(dashIdx + 1);
				text = text.Substring(0, dashIdx);
			}

			var parts = text.Split('.');
			if (parts.Length < 1 || parts.Length > 3)
				return false;

			if (!int.TryParse(parts[0], out var major))
				return false;

			var minor = 0;
			var patch = 0;
			if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
				return false;
			if (parts.Length > 2 && !int.TryParse(parts[2], out patch))
				return false;

			result = (major, minor, patch, pre);
			return true;
		}
	}
}
