using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voltage.Serialization
{
	#region Entity Component Attributes
	/// <summary>
	/// When applied to a Component subclass, ensures all listed component types
	/// are present on the Entity before this component is added.
	/// Missing dependencies are added automatically, mirroring Unity's behaviour.
	/// Multiple attributes may be stacked on a single class.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
	public sealed class RequireComponentAttribute : Attribute
	{
		public Type ComponentType { get; }

		public RequireComponentAttribute(Type componentType)
		{
			if (componentType == null)
				throw new ArgumentNullException(nameof(componentType));

			if (!typeof(Component).IsAssignableFrom(componentType))
				throw new ArgumentException(
					$"RequireComponent: '{componentType.Name}' must derive from Component.", nameof(componentType));

			ComponentType = componentType;
		}
	}


	/// <summary>
	/// When applied to a Component subclass, ensures at least one child entity
	/// of the owning entity has the specified component type at the time this
	/// component is added. If no such child exists, a new child entity is
	/// created automatically and the required component is added to it.
	/// Multiple attributes may be stacked on a single class.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
	public sealed class RequireComponentInChildrenAttribute : Attribute
	{
		public Type ComponentType { get; }

		/// <summary>
		/// Optional name for the auto-created child entity.
		/// If null, defaults to the component type name.
		/// </summary>
		public string ChildEntityName { get; set; }

		public RequireComponentInChildrenAttribute(Type componentType)
		{
			if (componentType == null)
				throw new ArgumentNullException(nameof(componentType));

			if (!typeof(Component).IsAssignableFrom(componentType))
				throw new ArgumentException(
					$"RequireComponentInChildren: '{componentType.Name}' must derive from Component.",
					nameof(componentType));

			ComponentType = componentType;
		}
	}

	#endregion
}
