using System.IO;
using Voltage.Persistence;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Load/save for <c>.vtimeline</c> assets. Uses <see cref="TypeNameHandling.Auto"/> so the polymorphic
	/// <see cref="TimelineAsset.ParameterTracks"/> list round-trips its concrete track types (e.g.
	/// <see cref="TimelineTransformTrack"/>). Engine track types live in <c>Voltage.dll</c>, which is fully
	/// preserved by TrimmerRoots, so this resolves correctly in NativeAOT game builds too.
	/// </summary>
	public static class TimelineAssetIO
	{
		public const string FileExtension = ".vtimeline";

		private static JsonSettings Settings() => new()
		{
			PrettyPrint = true,
			TypeNameHandling = TypeNameHandling.Auto,
			PreserveReferencesHandling = false,
		};

		/// <summary>A new, ready-to-edit timeline with a sensible default duration and one starter role.</summary>
		public static TimelineAsset CreateDefault()
		{
			var asset = new TimelineAsset { Duration = 5f };
			asset.Roles.Add(new TimelineRole { Name = "Actor" });
			return asset;
		}

		/// <summary>Writes a fresh default timeline to <paramref name="path"/> and returns it.</summary>
		public static TimelineAsset CreateAndSave(string path)
		{
			var asset = CreateDefault();
			Save(asset, path);
			return asset;
		}

		public static string ToJson(TimelineAsset asset) => Json.ToJson(asset, Settings());

		public static TimelineAsset FromJson(string json)
		{
			var asset = Json.FromJson<TimelineAsset>(json, Settings());
			asset?.InvalidateEventOrder();   // rebuild the cached event ordering after load
			return asset;
		}

		public static void Save(TimelineAsset asset, string path)
			=> File.WriteAllText(path, ToJson(asset));

		public static TimelineAsset Load(string path)
			=> File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
	}
}
