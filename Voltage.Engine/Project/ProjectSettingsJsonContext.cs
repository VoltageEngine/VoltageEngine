using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voltage.Project
{
	/// <summary>
	/// Source-generated JSON serialization context for ProjectSettings.
	/// Produces AOT-safe, trim-safe serialization code at compile time 
	/// no reflection, no Activator.CreateInstance, no property discovery.
	/// </summary>
	[JsonSourceGenerationOptions(
		WriteIndented = true,
		PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	)]
	[JsonSerializable(typeof(ProjectSettings))]
	[JsonSerializable(typeof(ProjectSettings.DisplaySettings))]
	[JsonSerializable(typeof(ProjectSettings.AudioSettings))]
	[JsonSerializable(typeof(ProjectSettings.DesignResolutionSettings))]
	[JsonSerializable(typeof(ProjectSettings.PhysicsSettings))]
	[JsonSerializable(typeof(ProjectSettings.RenderingSettings))]
	[JsonSerializable(typeof(ProjectSettings.EntitySettings))]
	public partial class ProjectSettingsJsonContext : JsonSerializerContext
	{
	}
}