using System.Collections.Generic;
using System.Linq;
using Voltage;

namespace Voltage.Editor.Gizmos
{
    public class GizmoEntityFilter
    {
        /// <summary>
        /// Returns only entities with valid (non-NaN, non-Infinite) positions.
        /// </summary>
        public static List<Entity> GetValidEntities(IEnumerable<Entity> entities)
        {
            return entities
                .Where(entity => entity != null && entity.Transform != null && !Voltage.Utils.MathUtils.IsVectorNaNOrInfinite(entity.Transform.Position))
                .ToList();
        }
    }
}