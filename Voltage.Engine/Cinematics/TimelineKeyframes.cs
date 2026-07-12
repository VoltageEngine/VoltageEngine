using Microsoft.Xna.Framework;
using Voltage.Utils.Tweens.Easing;

namespace Voltage.Cinematics
{
	/// <summary>A keyframe holding a <see cref="Vector2"/> value (position/scale) at a point in time.</summary>
	public class Vector2Keyframe
	{
		public float Time;
		public Vector2 Value;

		/// <summary>Ease used when interpolating INTO this keyframe from the previous one.</summary>
		public EaseType Ease = EaseType.Linear;
	}

	/// <summary>A keyframe holding a scalar value (rotation/alpha/any float property) at a point in time.</summary>
	public class FloatKeyframe
	{
		public float Time;
		public float Value;

		/// <summary>Ease used when interpolating INTO this keyframe from the previous one.</summary>
		public EaseType Ease = EaseType.Linear;
	}
}
