namespace Voltage.Project
{
	public class RenderLayer
	{

		#region Render Layer API

		/// <summary>
		/// Returns the render layer value by its name (e.g. "Background" → 0, "Entities" → 1).
		/// Returns 0 if not found.
		/// </summary>
		public static int GetRenderLayer(string layerName)
		{
			if (ProjectSettings.Instance.Rendering.RenderingLayers.TryGetValue(layerName, out var layerValue))
				return layerValue;

			Debug.Error($"[ProjectSettings] Render layer '{layerName}' not found. Did you add it in Project Settings?");
			return 0;
		}

		/// <summary>
		/// Returns the name of a render layer by its int value. Useful for display and debugging.
		/// Returns null if no layer with that value exists.
		/// </summary>
		public static string GetRenderLayerName(int layerValue)
		{
			foreach (var kvp in ProjectSettings.Instance.Rendering.RenderingLayers)
			{
				if (kvp.Value == layerValue)
					return kvp.Key;
			}

			Debug.Error($"[ProjectSettings] No render layer with value '{layerValue}' found. Did you add it in Project Settings?");
			return null;
		}

		/// <summary>
		/// Tries to get the render layer value by its name.
		/// </summary>
		public static bool TryGetRenderLayer(string layerName, out int layerValue)
		{
			return ProjectSettings.Instance.Rendering.RenderingLayers.TryGetValue(layerName, out layerValue);
		}

		#endregion
	}
}
