using System;
using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// AOT-safe method dispatch for timeline event clips, mirroring
	/// <see cref="Voltage.Serialization.Registries.ComponentAotFactory"/>. The source generator emits one
	/// <see cref="Register"/> call per <c>[TimelineEvent]</c> method (from a <c>[ModuleInitializer]</c>),
	/// binding <c>(componentId, methodName)</c> to a delegate that casts the component and unpacks its
	/// arguments. Because dispatch is a dictionary lookup + delegate call, it uses zero runtime reflection
	/// and survives NativeAOT + trimming.
	/// </summary>
	public static class TimelineDispatch
	{
		private static readonly Dictionary<(string ComponentId, string Method), Action<Component, TimelineArg[]>> _invokers
			= new();
		private static readonly Dictionary<(string ComponentId, Type ComponentType, string Method), Action<Component, TimelineArg[]>> _typedInvokers = new();
		private static readonly HashSet<(string ComponentId, string Method)> _legacy = new();

		/// <summary>Registers an invoker for a component method. Called by generated module initializers.</summary>
		public static void Register(string componentId, string method, Action<Component, TimelineArg[]> invoker)
			=> Register(componentId, method, invoker, null);

		public static void Register(string componentId, string method, Action<Component, TimelineArg[]> invoker, Type componentType)
		{
			if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(method) || invoker == null)
				return;
			_invokers[(componentId, method)] = invoker;
			if (componentType == null) _legacy.Add((componentId, method));
			else
			{
				_legacy.Remove((componentId, method));
				_typedInvokers[(componentId, componentType, method)] = invoker;
			}
		}

		/// <summary>True when a <c>[TimelineEvent]</c> method with this id+name has been registered.</summary>
		public static bool IsRegistered(string componentId, string method)
			=> _invokers.ContainsKey((componentId, method));

		/// <summary>
		/// Invokes the registered method on <paramref name="target"/> with <paramref name="args"/>.
		/// Returns false (no-op) when the target is null or no such method was registered.
		/// </summary>
		public static bool TryInvoke(string componentId, string method, Component target, TimelineArg[] args)
		{
			if (target == null || string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(method))
				return false;

			var key = (componentId, method);
			var found = _legacy.Contains(key)
				? _invokers.TryGetValue(key, out var invoker)
				: _typedInvokers.TryGetValue((componentId, target.GetType(), method), out invoker);
			if (!found)
				foreach (var registered in _typedInvokers)
					if (registered.Key.ComponentId == componentId && registered.Key.Method == method &&
						registered.Key.ComponentType.IsInstanceOfType(target))
					{
						invoker = registered.Value;
						found = true;
						break;
					}
			if (found)
			{
				invoker(target, args ?? Array.Empty<TimelineArg>());
				return true;
			}

			return false;
		}

		/// <summary>All registered (componentId, method) pairs — used by the editor to build method dropdowns.</summary>
		public static IEnumerable<(string ComponentId, string Method)> RegisteredMethods()
		{
			foreach (var key in _invokers.Keys)
				yield return key;
		}
	}
}
