using System;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Opts a public component method into the cinematic timeline's method-call dropdown and its AOT-safe
	/// dispatch table. Mark a method on a <see cref="Component"/> subclass with this, and the source
	/// generator emits a <see cref="TimelineDispatch"/> registration binding
	/// <c>(componentId, methodName)</c> to a delegate that invokes it — no runtime reflection, so it
	/// survives NativeAOT + trimming.
	///
	/// <example>
	/// <code>
	/// [ComponentId("explosion")]
	/// public partial class ExplosionComponent : Component
	/// {
	///     [TimelineEvent] public void Explode(float radius) { /* … */ }
	/// }
	/// </code>
	/// </example>
	///
	/// Supported parameter types match <see cref="TimelineArgType"/>: <c>float</c>, <c>int</c>,
	/// <c>bool</c>, <c>string</c>, and the serializable references <c>EntityReference</c>,
	/// <c>ComponentReference</c>, <c>AssetReference</c>, and <c>PrefabReference</c>. A parameterless method
	/// is the common case.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
	public sealed class TimelineEventAttribute : Attribute
	{
		/// <summary>Optional friendly label shown in the editor's method dropdown (defaults to the method name).</summary>
		public string DisplayName { get; set; }
	}
}
