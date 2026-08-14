namespace Voltage
{
	/// <summary>Engine version constants, used to validate a plugin's declared EngineVersion range.</summary>
	public static class VoltageVersion
	{
		/// <summary>Current engine version. The single source of truth for every version check, so the release workflow refuses a tag that disagrees with it.</summary>
		public const string Engine = "0.2.1";
	}
}
