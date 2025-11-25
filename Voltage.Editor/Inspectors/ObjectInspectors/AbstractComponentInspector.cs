using System.Collections.Generic;
using Nez;
using Voltage.Editor.Inspectors.TypeInspectors;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Inspectors.ObjectInspectors
{
	public abstract class AbstractComponentInspector : IComponentInspector
	{
		public abstract Entity Entity { get; }
		public abstract Component Component { get; }

		protected List<AbstractTypeInspector> _inspectors;
		protected int _scopeId = VoltageEditorUtils.GetScopeId();

		public abstract void Draw();
	}
}