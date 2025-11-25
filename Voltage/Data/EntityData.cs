using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Voltage.Data
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class EntityData
    {
        [HideAttributeInInspector]
        public List<ComponentDataEntry> ComponentDataList;

        [Inspectable]
        public int NumberOfSerializedComponents => ComponentDataList?.Count ?? 0;

		public EntityData()
		{
			ComponentDataList = new List<ComponentDataEntry>();
		}

		public EntityData(Entity entity)
        {
            ComponentDataList = new List<ComponentDataEntry>();
        }

        public EntityData(string entityType)
        {
            ComponentDataList = new List<ComponentDataEntry>();
        }

        /// <summary>
        /// Creates a deep copy of this EntityData
        /// </summary>
        public EntityData Clone()
        {
            var clone = new EntityData
            {
                ComponentDataList = new List<ComponentDataEntry>()
            };

            // Deep clone the component data list
            if (ComponentDataList != null)
            {
                foreach (var entry in ComponentDataList)
                {
                    clone.ComponentDataList.Add(new ComponentDataEntry
                    {
                        ComponentTypeName = entry.ComponentTypeName,
                        ComponentName = entry.ComponentName,
                        DataTypeName = entry.DataTypeName,
                        Json = entry.Json // JSON is immutable string, safe to copy reference
                    });
                }
            }

            return clone;
        }
    }
}
