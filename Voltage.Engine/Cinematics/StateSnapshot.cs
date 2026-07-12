using System;
using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Records how to restore the pre-cutscene state of the entities a timeline touches, so
	/// <see cref="TimelineDirector.Cancel"/> can put them back (e.g. the player died mid-cutscene). Each
	/// entry is a closure that captured a value at capture time and re-applies it — no reflection, AOT-safe.
	///
	/// This restores <b>visual/positional</b> state (transforms, alpha, etc.); it deliberately does not try
	/// to undo consequential gameplay events (an item already granted stays granted — that's the game's
	/// concern, not the director's).
	/// </summary>
	public sealed class StateSnapshot
	{
		private readonly List<Action> _restores = new();

		/// <summary>Adds a restore closure (which should have captured the current value already).</summary>
		public void Add(Action restore)
		{
			if (restore != null)
				_restores.Add(restore);
		}

		/// <summary>Captures <paramref name="current"/> now and restores it via <paramref name="apply"/> later.</summary>
		public void Capture<T>(T current, Action<T> apply)
		{
			if (apply != null)
				_restores.Add(() => apply(current));
		}

		public void Restore()
		{
			for (var i = 0; i < _restores.Count; i++)
				_restores[i]();
		}

		public void Clear() => _restores.Clear();
	}
}
