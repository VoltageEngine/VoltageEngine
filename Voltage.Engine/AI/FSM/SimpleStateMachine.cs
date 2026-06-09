using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Voltage.Utils;


namespace Voltage.AI.FSM
{
	/// <summary>
	/// Simple state machine with an enum constraint. There are some rules you must follow when using this:
	/// - before update is called initialState must be set (use the constructor or onAddedToEntity)
	/// - must override <see cref="RegisterStates"/> and call <see cref="RegisterState"/> before setting the initial state
	/// - if you implement update in your subclass you must call base.update()
	/// 
	/// Note: if you use an enum as the contraint you can avoid allocations/boxing in Mono by doing what the Core
	/// Emitter does for its enum: pass in a IEqualityComparer to the constructor.
	/// <para></para>
	/// AOT/NativeAOT: 
	/// </summary>
	public abstract class SimpleStateMachine<TEnum> : Component, IUpdatable
		where TEnum : struct, IComparable, IFormattable
	{
		class StateMethodCache
		{
			public Action EnterState;
			public Action Tick;
			public Action ExitState;
		}

		protected float elapsedTimeInState = 0f;
		protected TEnum previousState;
		Dictionary<TEnum, StateMethodCache> _stateCache;
		StateMethodCache _stateMethods;

		TEnum _currentState;

		public TEnum CurrentState
		{
			get => _currentState;
			set
			{
				// dont change to the current state
				if (_stateCache.Comparer.Equals(_currentState, value))
					return;

				// swap previous/current
				previousState = _currentState;
				_currentState = value;

				// exit the state, fetch the next cached state methods then enter that state
				if (_stateMethods?.ExitState != null)
					_stateMethods.ExitState();

				elapsedTimeInState = 0f;
				_stateMethods = _stateCache[_currentState];

				if (_stateMethods.EnterState != null)
					_stateMethods.EnterState();
			}
		}

		protected TEnum InitialState
		{
			set
			{
				_currentState = value;
				_stateMethods = _stateCache[_currentState];

				if (_stateMethods.EnterState != null)
					_stateMethods.EnterState();
			}
		}

		public SimpleStateMachine(IEqualityComparer<TEnum> customComparer = null)
		{
			_stateCache = new Dictionary<TEnum, StateMethodCache>(customComparer);
		}

		/// <summary>
		/// Called during <see cref="OnStart"/> after the state cache is prepared.
		/// Override this in subclasses to explicitly register state delegates via
		/// <see cref="RegisterState"/> — required for NativeAOT / trimming compatibility.
		/// If not overridden, falls back to the reflection-based auto-discovery path.
		/// </summary>
		protected virtual void RegisterStates() { }

		/// <summary>
		/// Explicitly registers delegates for a state. Use this instead of the reflection-based
		/// auto-discovery path when targeting NativeAOT or when trimming is enabled.
		/// Any of the three delegates may be null.
		/// </summary>
		protected void RegisterState(TEnum state, Action enter = null, Action tick = null, Action exit = null)
		{
			_stateCache[state] = new StateMethodCache
			{
				EnterState = enter,
				Tick = tick,
				ExitState = exit,
			};
		}

		public override void OnStart()
		{
			RegisterStates();
		}

		public virtual void Update()
		{
			elapsedTimeInState += Time.DeltaTime;

			if (_stateMethods == null)
			{
				Debug.Error(
					$"[{GetType().Name}] InitialState was never set. " +
					$"Assign 'InitialState = <YourFirstState>' in OnStart() before the first Update() runs.");
				return;
			}

			if (_stateMethods.Tick == null)
				return;

			try
			{
				_stateMethods.Tick();
			}
			catch (Exception ex)
			{
				Debug.Error($"[{GetType().Name}] Exception in state '{_currentState}' Tick() — {ex.Message}, from {ex}");
			}
		}
	}
}
