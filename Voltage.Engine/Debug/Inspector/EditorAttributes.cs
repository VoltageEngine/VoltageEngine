using System;

namespace Voltage
{
	/// <summary>
	/// Attribute that is used to indicate that the field/property should be serialized and shown in the Inspector
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class SerializeAttribute : Attribute
	{
	}

	/// <summary>
	/// Attribute that is used to indicate that the field/property should not be present in the Inspector
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class HideInInspectorAttribute : Attribute
	{
	}

	/// <summary>
	/// Adding this to a method will expose it to the Inspector if it has 0 params or 1 param of a supported type: int, float, string, bool
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class InspectorCallableAttribute : SerializeAttribute
	{
	}

	/// <summary>
	/// Displays a tooltip when hovering over the label of any inspectable elements
	/// </summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
	public class TooltipAttribute : SerializeAttribute
	{
		public string Tooltip;

		public TooltipAttribute(string tooltip)
		{
			Tooltip = tooltip;
		}
	}

	/// <summary>
	/// Range attribute. Tells the Inspector you want a slider to be displayed for a float/int
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class RangeAttribute : SerializeAttribute
	{
		public float MinValue;
		public float MaxValue;
		public float StepSize = 1;
		public bool UseDragVersion;


		public RangeAttribute(float minValue)
		{
			MinValue = minValue;

			// magic number! This is the highest number ImGui functions properly with for some reason.
			MaxValue = int.MaxValue - 100;
			UseDragVersion = true;
		}

		public RangeAttribute(float minValue, float maxValue, float stepSize)
		{
			MinValue = minValue;
			MaxValue = maxValue;
			StepSize = stepSize;
		}

		public RangeAttribute(float minValue, float maxValue, bool useDragFloat)
		{
			MinValue = minValue;
			MaxValue = maxValue;
			UseDragVersion = useDragFloat;
		}

		public RangeAttribute(float minValue, float maxValue) : this(minValue, maxValue, 0.1f)
		{
		}
	}

	/// <summary>
	/// Putting this attribute on a class and specifying a subclass of Inspector lets you create custom Inspectors for any type. When
	/// the Inspector finds a field/property of the type with the attribute on it the InspectorType will be instantiated and used.
	/// Inspectors are only active in DEBUG builds so make sure to wrap your custom Inspector subclass in #if EDITOR /#endif.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
	public class CustomInspectorAttribute : Attribute
	{
		public Type InspectorType;

		public CustomInspectorAttribute(Type InspectorType)
		{
			InspectorType = InspectorType;
		}
	}
	/// <summary>
	/// Optional attribute for controlling the display label and default expanded state
	/// of a component group in the inspector.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class ComponentGroupAttribute : Attribute
	{
		public string Label { get; }
		public bool DefaultExpanded { get; }

		public ComponentGroupAttribute(string label = null, bool defaultExpanded = true)
		{
			Label = label;
			DefaultExpanded = defaultExpanded;
		}
	}

	/// <summary>
	/// Marks an int field as a single physics layer selector.
	/// The stored value is 1 shifted left by the layer index (e.g. 1 << 0).
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class PhysicsLayerAttribute : Attribute { }

	/// <summary>
	/// Marks an int field as a physics layer bitmask.
	/// Multiple layers can be selected at once (e.g. CollidesWithLayers).
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class PhysicsLayerMaskAttribute : Attribute { }

	/// <summary>
	/// Marks an int field as a render layer selector.
	/// The stored value is the direct int value defined in ProjectSettings.RenderingLayers.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class RenderLayerAttribute : Attribute { }

	/// <summary>
	/// Marks an int field as an entity tag selector.
	/// The stored value is the direct int value defined in ProjectSettings.EntityTags.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class EntityTagAttribute : Attribute { }

	/// <summary>
	/// Apply to a public string field or property to display a file-browser button in the Inspector.
	/// Clicking "Browse" opens a popup rooted at the project's Content folder (or an optional
	/// sub-path). The selected absolute path is converted to a relative path before being stored.
	/// Optionally restrict the picker to specific file extensions, e.g. [FilePath(".png|.jpg")].
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class FilePathAttribute : Attribute
	{
		/// <summary>
		/// Pipe-separated list of allowed file extensions, e.g. ".png|.jpg|.aseprite".
		/// Null or empty means all files are shown.
		/// </summary>
		public string Filter { get; }

		public FilePathAttribute(string filter = null)
		{
			Filter = filter;
		}
	}
}