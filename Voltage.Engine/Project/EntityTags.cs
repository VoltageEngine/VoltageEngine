
namespace Voltage.Project
{
	public class EntityTags
	{
		/// <summary>
		/// Gets the entity tag value by its name.
		/// Returns 0 if not found.
		/// </summary>
		public static int GetEntityTag(string tagName)
		{
			if (ProjectSettings.Instance.Entities.EntityTags.TryGetValue(tagName, out var tagValue))
				return tagValue;

			Debug.Error($"[ProjectSettings] Entity tag '{tagName}' not found. Did you add it in Project Settings?");
			return 0;
		}

		/// <summary>
		/// Tries to get the entity tag value by its name.
		/// </summary>
		public static bool TryGetEntityTag(string tagName, out int tagValue)
		{
			return ProjectSettings.Instance.Entities.EntityTags.TryGetValue(tagName, out tagValue);
		}
	}
}
