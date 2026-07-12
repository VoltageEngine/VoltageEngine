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

		/// <summary>Registers an invoker for a component method. Called by generated module initializers.</summary>
		public static void Register(string componentId, string method, Action<Component, TimelineArg[]> invoker)
		{
			if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(method) || invoker == null)
				return;
			_invokers[(componentId, method)] = invoker;
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

			if (_invokers.TryGetValue((componentId, method), out var invoker))
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
