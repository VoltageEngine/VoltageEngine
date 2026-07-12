namespace Voltage
{
	/// <summary>
	/// Engine version constants. Used by the plugin system to validate a plugin's declared
	/// EngineVersion range against the engine it is being loaded into.
	/// </summary>
	public static class VoltageVersion
	{
		/// <summary>Current engine version (semver). Bump on releases that change public engine API.</summary>
		public const string Engine = "0.1.0";
	}
}
