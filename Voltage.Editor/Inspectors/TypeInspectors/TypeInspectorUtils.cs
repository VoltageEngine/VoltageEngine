using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Voltage;
using Voltage.Utils;
using Voltage.Utils.Extensions;


namespace Voltage.Editor.Inspectors.TypeInspectors
{
	using TypeInspectors_BlendStateInspector = TypeInspectors.BlendStateInspector;
	using TypeInspectors_EntityFieldInspector = TypeInspectors.EntityFieldInspector;
	using TypeInspectors_EnumInspector = TypeInspectors.EnumInspector;
	using TypeInspectors_ListInspector = TypeInspectors.ListInspector;
	using TypeInspectors_SimpleTypeInspector = TypeInspectors.SimpleTypeInspector;
	using TypeInspectors_StructInspector = TypeInspectors.StructInspector;

	public static class TypeInspectorUtils
	{
		// Type cache seeing as how typeof isnt free and this will be hit a lot
		static readonly Type notInspectableAttrType = typeof(HideInInspectorAttribute);
		static readonly Type inspectableAttrType = typeof(SerializeAttribute);
		static readonly Type componentType = typeof(Component);
		static readonly Type transformType = typeof(Transform);
		static readonly Type materialType = typeof(Material);
		static readonly Type effectType = typeof(Effect);
		static readonly Type iListType = typeof(IList);
		static readonly Type abstractTypeInspectorType = typeof(AbstractTypeInspector);
		static readonly Type objectType = typeof(object);
		static readonly Type serializationAttrType = typeof(SerializableAttribute);

		/// <summary>
		/// fetches all the relevant AbstractTypeInspectors for target including fields, properties and methods.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		// Update the GetInspectableProperties method to check for public setters:

		public static List<AbstractTypeInspector> GetInspectableProperties(object obj)
		{
			var objType = obj.GetType();
			var inspectors = new List<AbstractTypeInspector>();

			var fields = ReflectionUtils.GetFields(objType);
			foreach (var field in fields)
			{
				// Check for [HideAttributeInInspector] first
				if (field.IsDefined(notInspectableAttrType))
					continue;

				// Skip if not public and doesn't have [Inspectable] attribute
				if (!field.IsPublic && !field.IsDefined(inspectableAttrType))
					continue;

				// Skip const fields
				if (field.IsLiteral)
					continue;

				var inspector = TypeInspectorUtils.GetInspectorForType(field.FieldType, obj, field);
				if (inspector != null)
				{
					inspector.SetTarget(obj, field);
					inspector.Initialize();
					inspectors.Add(inspector);
				}
			}

			var properties = ReflectionUtils.GetProperties(objType);
			foreach (var prop in properties)
			{
				// Check for [HideAttributeInInspector] first
				if (prop.IsDefined(notInspectableAttrType))
					continue;

				// Skip properties that can't be read
				if (!prop.CanRead)
					continue;

				// Skip indexed properties (properties with parameters)
				var indexParams = prop.GetIndexParameters();
				if (indexParams != null && indexParams.Length > 0)
					continue;

				// Check getter and setter accessibility
				bool hasPublicGetter = prop.GetMethod?.IsPublic ?? false;
				bool hasInspectableAttribute = prop.IsDefined(inspectableAttrType);

				// Rules for showing properties:
				// 1. If property has [Inspectable] attribute, always show it (regardless of accessibility)
				// 2. If property has public getter, show it (will be read-only if setter is non-public)
				// 3. If both getter and setter are non-public, hide it (unless it has [Inspectable])
				
				if (!hasInspectableAttribute)
				{
					// Hide if getter is not public (both getter and setter are non-public)
					if (!hasPublicGetter)
						continue;
				}

				var inspector = TypeInspectorUtils.GetInspectorForType(prop.PropertyType, obj, prop);
				if (inspector != null)
				{
					inspector.SetTarget(obj, prop);
					inspector.Initialize();
					inspectors.Add(inspector);
				}
			}

			var methods = ReflectionUtils.GetMethods(objType);
			foreach (var method in methods)
			{
				if (!method.IsDefined(typeof(InspectorCallableAttribute)))
					continue;

				if (!MethodInspector.AreParametersValid(method.GetParameters()))
					continue;

				var inspector = new MethodInspector();
				inspector.SetTarget(obj, method);
				inspector.Initialize();
				inspectors.Add(inspector);
			}

			return inspectors;
		}

		public static IEnumerable<MethodInfo> GetAllMethodsWithAttribute<T>(Type type) where T : Attribute
		{
			var methods = ReflectionUtils.GetMethods(type);
			foreach (var method in methods)
			{
				var attr = method.GetAttribute<T>();
				if (attr == null)
					continue;

				yield return method;
			}
		}

		/// <summary>
		/// gets an Inspector subclass that can handle valueType. If no default Inspector is available the memberInfo custom attributes
		/// will be checked for the CustomInspectorAttribute.
		/// </summary>
		/// <returns>The inspector for type.</returns>
		/// <param name="valueType">Value type.</param>
		/// <param name="memberInfo">Member info.</param>
		public static AbstractTypeInspector GetInspectorForType(Type valueType, object target, MemberInfo memberInfo)
		{
			// Layer-aware int inspectors  must be checked before SimpleTypeInspector
			if (valueType == typeof(int) && memberInfo.IsDefined(typeof(PhysicsLayerAttribute)))
				return new PhysicsLayerTypeInspector();
			if (valueType == typeof(int) && memberInfo.IsDefined(typeof(PhysicsLayerMaskAttribute)))
				return new PhysicsLayerMaskTypeInspector();
			if (valueType == typeof(int) && memberInfo.IsDefined(typeof(RenderLayerAttribute)))
				return new RenderLayerTypeInspector();

			// built-in types
			if (SimpleTypeInspector.KSupportedTypes.Contains(valueType))
				return new TypeInspectors_SimpleTypeInspector();
			if (target is Entity)
				return new TypeInspectors_EntityFieldInspector();
			if (target is BlendState)
				return new TypeInspectors_BlendStateInspector();
			if (valueType.GetTypeInfo().IsEnum)
				return new TypeInspectors_EnumInspector();

			// must be checked before IsValueType
			if (typeof(IComponentGroup).IsAssignableFrom(valueType) && !valueType.IsValueType)
				return new TypeInspectors_ComponentGroupInspector();
			if (valueType.GetTypeInfo().IsValueType)
				return new TypeInspectors_StructInspector();
			if (target is IList && ListInspector.KSupportedTypes.Contains(valueType.GetElementType()))
				return new TypeInspectors_ListInspector();
			if (valueType.IsArray && valueType.GetArrayRank() == 1 &&
			    ListInspector.KSupportedTypes.Contains(valueType.GetElementType()))
				return new TypeInspectors_ListInspector();
			if (valueType.IsGenericType && iListType.IsAssignableFrom(valueType) &&
			    valueType.GetInterface(nameof(IList)) != null &&
			    ListInspector.KSupportedTypes.Contains(valueType.GetGenericArguments()[0]))
				return new TypeInspectors_ListInspector();

			// check for custom inspectors before checking Voltage types in case a subclass implemented one
			var customInspectorType = valueType.GetTypeInfo().GetAttribute<CustomInspectorAttribute>();
			if (customInspectorType != null)
			{
				if (customInspectorType.InspectorType.GetTypeInfo().IsSubclassOf(abstractTypeInspectorType))
					return (AbstractTypeInspector) Activator.CreateInstance(customInspectorType.InspectorType);
			}

			// Voltage types
			if (componentType.IsAssignableFrom(valueType) && valueType != objectType)
				return new ComponentReferenceTypeInspector();
			if (valueType == typeof(Entity))
				return new EntityReferenceTypeInspector();
			if (valueType == typeof(Transform))
				return new EntityReferenceTypeInspector();
			if (valueType == materialType || valueType.IsSubclassOf(materialType))
				return GetMaterialInspector(target, memberInfo);
			if (valueType == effectType || valueType.IsSubclassOf(effectType))
				return GetEffectInspector(target, memberInfo);
			

			// last ditch effort. If the class is serializeable we use a generic ObjectInspector
			if (valueType != objectType && valueType.IsDefined(serializationAttrType))
				return new ObjectInspectors.ObjectInspector();

			return null;
		}

		/// <summary>
		/// null checks the Material and ony returns an Inspector if we have data since Material will almost always
		/// be null
		/// </summary>
		/// <returns>The material inspector.</returns>
		/// <param name="target">Target.</param>
		/// <param name="memberInfo">Member info.</param>
		static AbstractTypeInspector GetMaterialInspector(object target, MemberInfo memberInfo)
		{
			Material material = null;
			var fieldInfo = memberInfo as FieldInfo;
			if (fieldInfo != null)
				material = fieldInfo.GetValue(target) as Material;

			var propInfo = memberInfo as PropertyInfo;
			if (propInfo != null)
			{
				var getter = ReflectionUtils.GetPropertyGetter(propInfo);
				material = getter.Invoke(target, new object[] { }) as Material;
			}

			return new MaterialInspector();
		}

		/// <summary>
		/// null checks the Effect and creates an Inspector only if it is not null
		/// </summary>
		/// <returns>The effect inspector.</returns>
		/// <param name="target">Target.</param>
		/// <param name="memberInfo">Member info.</param>
		static AbstractTypeInspector GetEffectInspector(object target, MemberInfo memberInfo)
		{
			// we only want subclasses of Effect. Effect itself is not interesting so we have to fetch the data
			Effect effect = null;
			var fieldInfo = memberInfo as FieldInfo;
			if (fieldInfo != null)
				effect = fieldInfo.GetValue(target) as Effect;

			var propInfo = memberInfo as PropertyInfo;
			if (propInfo != null)
			{
				var getter = ReflectionUtils.GetPropertyGetter(propInfo);
				effect = getter.Invoke(target, new object[] { }) as Effect;
			}

			if (effect != null && effect.GetType() != effectType)
				return new EffectInspector();

			return null;
		}
	}
}