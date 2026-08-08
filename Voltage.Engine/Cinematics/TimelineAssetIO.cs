using Voltage.Assets;
using Voltage.Persistence;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Load/save for <c>.vtimeline</c> assets. The behaviour lives in <see cref="Format"/>; the members
	/// here are thin forwarders kept for existing call sites.
	/// </summary>
	public static class TimelineAssetIO
	{
		public const string FileExtension = ".vtimeline";

		/// <summary>
		/// Needs <see cref="TypeNameHandling.Auto"/> so the polymorphic <see cref="TimelineAsset.ParameterTracks"/> list round-trips its concrete track types — but the hint written is a <b>stable id</b> from <see cref="TimelineTrackRegistry"/>, not a CLR name, so renaming or moving a track class does not break existing timelines.
		/// </summary>
		public static readonly JsonAssetFile<TimelineAsset> Format = new(
			FileExtension,
			"Timeline",
			createDefault: _ =>
			{
				var asset = new TimelineAsset { Duration = 5f };
				asset.Roles.Add(new TimelineRole { Name = "Actor" });
				return asset;
			},
			settings: new JsonSettings
			{
				PrettyPrint = true,
				TypeNameHandling = TypeNameHandling.Auto,
				PreserveReferencesHandling = false,
				TypeNameWriter = TimelineTrackRegistry.RequireId,
				TypeNameReader = TimelineTrackRegistry.RequireType,
			},
			afterLoad: asset => asset.InvalidateEventOrder());

		public static TimelineAsset CreateDefault() => Format.CreateDefault();

		public static TimelineAsset CreateAndSave(string path) => Format.CreateAndSave(path);

		public static string ToJson(TimelineAsset asset) => Format.ToJson(asset);

		public static TimelineAsset FromJson(string json) => Format.FromJson(json);

		public static void Save(TimelineAsset asset, string path) => Format.Save(asset, path);

		public static TimelineAsset Load(string path) => Format.Load(path);
	}
}
