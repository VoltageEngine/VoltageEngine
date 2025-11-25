using System;
using System.Reflection;
using Nez;

namespace Voltage.Editor.Inspectors.ObjectInspectors
{
    public static class ComponentInspectorFactory
    {
        public static IComponentInspector CreateInspector(Component component)
        {
            // Check if the component type has a CustomInspector attribute
            var componentType = component.GetType();
            var customInspectorAttribute = componentType.GetCustomAttribute<CustomInspectorAttribute>();
            
            if (customInspectorAttribute != null)
            {
                // Create the custom inspector
                var inspectorType = customInspectorAttribute.InspectorType;
                if (typeof(IComponentInspector).IsAssignableFrom(inspectorType))
                {
                    try
                    {
                        return (IComponentInspector)Activator.CreateInstance(inspectorType, component);
                    }
                    catch (Exception e)
                    {
                        Debug.Error($"Failed to create custom inspector {inspectorType.Name} for {componentType.Name}: {e.Message}");
                    }
                }
            }
            
            // Fall back to default ComponentInspector
            return new ComponentInspector(component);
        }
    }
}