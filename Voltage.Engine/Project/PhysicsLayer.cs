namespace Voltage.Project
{
	public class PhysicsLayer
	{
		/// <summary>
		/// Returns the bitmask bit for a physics layer by name (e.g. "Ground" → 1 &lt;&lt; 1).
		/// Use this when setting <c>Collider.PhysicsLayer</c> or building a <c>CollidesWithLayers</c> mask.
		/// Returns 0 if not found.
		/// </summary>
		public static int GetPhysicsLayerBit(string layerName)
		{
			if (ProjectSettings.Instance.Physics.PhysicsLayers.TryGetValue(layerName, out var index))
				return 1 << index;

			Debug.Error($"[ProjectSettings] Physics layer '{layerName}' not found. Did you add it in Project Settings?");
			return 0;
		}

		/// <summary>
		/// Returns the bitmask bit for a physics layer by its index (e.g. index 1 → 1 &lt;&lt; 1 = 2).
		/// Use this when setting <c>Collider.PhysicsLayer</c> or building a <c>CollidesWithLayers</c> mask.
		/// </summary>
		public static int GetPhysicsLayerBit(int layerIndex) => 1 << layerIndex;

		/// <summary>
		/// Returns a combined bitmask for multiple physics layer names.
		/// Useful for setting <c>Collider.CollidesWithLayers</c> in scripts.
		/// <example>
		/// <code>
		/// collider.CollidesWithLayers = ProjectSettings.Instance.GetPhysicsLayerMask("Default", "Ground");
		/// </code>
		/// </example>
		/// </summary>
		public static int GetPhysicsLayerMask(params string[] layerNames)
		{
			var mask = 0;
			foreach (var name in layerNames)
				mask |= GetPhysicsLayerBit(name);

			return mask;
		}

		/// <summary>
		/// Tries to get the physics layer index by its name.
		/// </summary>
		public static bool TryGetPhysicsLayer(string layerName, out int layerValue)
		{
			return ProjectSettings.Instance.Physics.PhysicsLayers.TryGetValue(layerName, out layerValue);
		}
	}
}
