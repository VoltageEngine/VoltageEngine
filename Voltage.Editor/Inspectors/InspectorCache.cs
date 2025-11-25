using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Nez.Utils;


namespace Nez.ImGuiTools
{
	public static class InspectorCache
	{
		static Type[] _entitySubclasses;
		static Type[] _componentSubclasses;
		static Type[] _effectSubclasses;
		static Type[] _postProcessorSubclasses;

		/// <summary>
		/// gets all the Entity subclasses that have a parameterless constructor
		/// </summary>
		/// <returns></returns>
		public static Type[] GetAllEntitySubclassTypes()
		{
			if (_entitySubclasses == null)
			{
				var subclasses = ReflectionUtils.GetAllSubclasses(typeof(Entity), true);

				// filter out any Entities that have generic parameters
				for (var i = subclasses.Count - 1; i >= 0; i--)
				{
					if (subclasses[i].ContainsGenericParameters)
						subclasses.RemoveAt(i);
				}

				// sort so the Nez entities are after user entities, then by name
				subclasses.Sort((t, u) =>
				{
					var tIsNez = t.Namespace != null && t.Namespace.StartsWith("Nez");
					var uIsNez = u.Namespace != null && u.Namespace.StartsWith("Nez");

					if (tIsNez && uIsNez || !tIsNez && !uIsNez)
						return t.Name.CompareTo(u.Name);

					if (tIsNez && !uIsNez)
						return 1;

					return -1;
				});

				_entitySubclasses = subclasses.ToArray();
			}

			return _entitySubclasses;
		}

		/// <summary>
		/// gets all the Component subclasses that have a parameterless constructor
		/// </summary>
		/// <returns></returns>
		public static Type[] GetAllComponentSubclassTypes()
		{
			if (_componentSubclasses == null)
			{
				var subclasses = ReflectionUtils.GetAllSubclasses(typeof(Component), true);

				// filter out any Components that have generic parameters
				for (var i = subclasses.Count - 1; i >= 0; i--)
				{
					if (subclasses[i].ContainsGenericParameters)
						subclasses.RemoveAt(i);
				}

				// sort so the Colliders are on the bottom
				subclasses.Sort((t, u) =>
				{
					var tIsCollider = typeof(Collider).IsAssignableFrom(t);
					var uIsCollider = typeof(Collider).IsAssignableFrom(u);

					if (tIsCollider && uIsCollider)
						return t.Name.CompareTo(u.Name);

					if (!tIsCollider && !uIsCollider)
					{
						var tIsNez = t.Namespace.StartsWith("Nez");
						var uIsNez = u.Namespace.StartsWith("Nez");

						if (tIsNez && uIsNez || !tIsNez && !uIsNez)
							return t.Name.CompareTo(u.Name);

						if (tIsNez && !uIsNez)
							return 1;

						return -1;
					}

					if (tIsCollider && !uIsCollider)
						return 1;

					return -1;
				});
				_componentSubclasses = subclasses.ToArray();
			}

			return _componentSubclasses;
		}

		/// <summary>
		/// gets all the Effect subclasses that have a parameterless constructor
		/// </summary>
		/// <returns></returns>
		public static Type[] GetAllEffectSubclassTypes()
		{
			if (_effectSubclasses == null)
			{
				var subclasses = ReflectionUtils.GetAllSubclasses(typeof(Effect), true);
				subclasses.Sort((t, u) => { return t.Name.CompareTo(u.Name); });
				_effectSubclasses = subclasses.ToArray();
			}

			return _effectSubclasses;
		}

		/// <summary>
		/// gets all the Effect subclasses that have a parameterless constructor
		/// </summary>
		/// <returns></returns>
		public static Type[] GetAllPostProcessorSubclassTypes()
		{
			if (_postProcessorSubclasses == null)
			{
				var subclasses = ReflectionUtils.GetAllSubclasses(typeof(PostProcessor));

				// filter out all except those with a single parameter constructor
				var constructorParams = new Type[] {typeof(int)};
				for (var i = subclasses.Count - 1; i >= 0; i--)
				{
					if (subclasses[i].GetConstructor(constructorParams) == null)
						subclasses.RemoveAt(i);
				}

				subclasses.Sort((t, u) => { return t.Name.CompareTo(u.Name); });
				_postProcessorSubclasses = subclasses.ToArray();
			}

			return _postProcessorSubclasses;
		}

		public static bool HasNonParameterlessChildEntity(Entity entity)
		{
			if (entity == null)
				return false;

			for (int i = 0; i < entity.Transform.ChildCount; i++)
			{
				var child = entity.Transform.GetChild(i)?.Entity;
				if (child == null)
					continue;

				// Check for parameterless constructor
				var ctor = child.GetType().GetConstructor(Type.EmptyTypes);
				if (ctor == null)
					return true;

				// Recursively check grandchildren
				if (HasNonParameterlessChildEntity(child))
					return true;
			}
			return false;
		}
	}
}