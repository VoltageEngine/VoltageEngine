using System.Collections.Generic;
using Voltage.Utils.Collections;


namespace Voltage
{
	/// <summary>
	/// Helper used by Mover to dispatch trigger events to ITriggerListeners.
	/// Tracks per-pair overlap state across frames and fires OnTriggerEnter / OnTriggerStay / OnTriggerExit
	/// as the set transitions. Call Update AFTER the entity has been moved this frame.
	/// </summary>
	public class ColliderTriggerHelper
	{
		enum TriggerEventType { Enter, Stay, Exit }

		Entity _entity;

		HashSet<Pair<Collider>> _activeTriggerIntersections = new HashSet<Pair<Collider>>();
		HashSet<Pair<Collider>> _previousTriggerIntersections = new HashSet<Pair<Collider>>();
		List<ITriggerListener> _tempTriggerList = new List<ITriggerListener>();


		public ColliderTriggerHelper(Entity entity)
		{
			_entity = entity;
		}


		public void Update()
		{
			var colliders = _entity.GetComponents<Collider>();
			for (var i = 0; i < colliders.Count; i++)
			{
				var collider = colliders[i];
				if (!collider.Enabled)
					continue;

				var neighbors = Physics.BoxcastBroadphase(collider.Bounds, collider.CollidesWithLayers);
				foreach (var neighbor in neighbors)
				{
					if (!collider.IsTrigger && !neighbor.IsTrigger)
						continue;

					if (!collider.Overlaps(neighbor))
						continue;

					var pair = new Pair<Collider>(collider, neighbor);
					// HashSet.Add == false → already counted this pair this frame (duplicate broadphase hit)
					if (!_activeTriggerIntersections.Add(pair))
						continue;

					NotifyTriggerListeners(pair,
						_previousTriggerIntersections.Contains(pair) ? TriggerEventType.Stay : TriggerEventType.Enter);
				}
			}
			ListPool<Collider>.Free(colliders);

			// pairs that were active last frame but not this frame → exit
			foreach (var pair in _previousTriggerIntersections)
			{
				if (!_activeTriggerIntersections.Contains(pair))
					NotifyTriggerListeners(pair, TriggerEventType.Exit);
			}

			// swap the two sets so we keep allocations to zero per frame
			var swap = _previousTriggerIntersections;
			_previousTriggerIntersections = _activeTriggerIntersections;
			_activeTriggerIntersections = swap;
			_activeTriggerIntersections.Clear();
		}


		void NotifyTriggerListeners(Pair<Collider> collisionPair, TriggerEventType eventType)
		{
			collisionPair.First.Entity.GetComponents(_tempTriggerList);
			for (var i = 0; i < _tempTriggerList.Count; i++)
				Dispatch(_tempTriggerList[i], eventType, collisionPair.Second, collisionPair.First);
			_tempTriggerList.Clear();

			if (collisionPair.Second.Entity != null)
			{
				collisionPair.Second.Entity.GetComponents(_tempTriggerList);
				for (var i = 0; i < _tempTriggerList.Count; i++)
					Dispatch(_tempTriggerList[i], eventType, collisionPair.First, collisionPair.Second);
				_tempTriggerList.Clear();
			}
		}


		static void Dispatch(ITriggerListener listener, TriggerEventType eventType, Collider other, Collider local)
		{
			switch (eventType)
			{
				case TriggerEventType.Enter: listener.OnTriggerEnter(other, local); break;
				case TriggerEventType.Stay:  listener.OnTriggerStay(other, local);  break;
				case TriggerEventType.Exit:  listener.OnTriggerExit(other, local);  break;
			}
		}
	}
}
