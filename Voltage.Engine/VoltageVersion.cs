namespace Voltage
{
	/// <summary>
	/// Engine version constants. Used by the plugin system to validate a plugin's declared
	/// EngineVersion range against the engine it is being loaded into.
	/// </summary>
	public static class VoltageVersion
	{
		/// <summary>
		/// Current engine version (semver). Bump on releases that change public engine API.
		///
		/// <para>This is the single source of truth for every version check in the engine: whether a
		/// plugin's EngineVersion range is satisfied, and whether a project was written by a newer editor
		/// than the one opening it. All of those are inert if this is not bumped, so the release workflow
		/// refuses to build a tag that disagrees with it.</para>
		/// </summary>
		public const string Engine = "0.2.1";
	}
}
