using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Voltage.Editor.Undo;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Undo.EntityActions;

namespace Voltage.Editor.Gizmos
{
	/// <summary>
	/// Handles gizmo rendering and interaction for entity position/translation using arrows
	/// </summary>
	public class EntityTransformGizmoHandler
	{
		public bool IsDragging => _draggingX || _draggingY;
		public bool IsMouseOverGizmo { get; private set; }

		private bool _draggingX = false;
		private bool _draggingY = false;
		private Vector2 _dragStartWorldMouse;
		private Dictionary<Entity, Vector2> _dragStartEntityPositions = new();
		private Dictionary<Entity, Vector2> _dragEndEntityPositions = new();
		private float _desiredScreenLength = 90f;
		private float _minLength = 60f;
		private float _maxLength = 600f;

		/// <summary>
		/// Draws entity transform arrows and handles interaction
		/// </summary>
		public void Draw(List<Entity> entities, Vector2 worldMouse, Camera camera)
		{
			IsMouseOverGizmo = false;

			var selectedEntities = GizmoEntityFilter.GetValidEntities(entities);

			if (selectedEntities.Count == 0)
				return;

			// Calculate center using valid entities only
			Vector2 center = Vector2.Zero;
			foreach (var e in selectedEntities)
			{
				center += e.Transform.Position;
			}
			center /= selectedEntities.Count;

			float axisLength = _desiredScreenLength / MathF.Max(camera.RawZoom, 0.01f);
			axisLength = Math.Clamp(axisLength, _minLength, _maxLength);

			float baseWidth = 4f;
			float maxWidth = 12f;
			float scaledWidth = baseWidth;

			// Scale arrow width inversely with zoom so it stays constant on screen
			scaledWidth = baseWidth / MathF.Max(camera.RawZoom, 0.01f);
			scaledWidth = Math.Clamp(scaledWidth, 2f, maxWidth);

			// World-space endpoints for the arrows
			var worldEndX = center + new Vector2(axisLength, 0);
			var worldEndY = center + new Vector2(0, -axisLength);

			// Screen-space positions for hit-testing
			var screenPos = camera.WorldToScreenPoint(center);
			var screenEndX = camera.WorldToScreenPoint(worldEndX);
			var screenEndY = camera.WorldToScreenPoint(worldEndY);

			Color xColor = Color.Red;
			Color yColor = Color.LimeGreen;

			var mousePos = Input.ScaledMousePosition;

			bool xHovered = IsMouseNearLine(mousePos, screenPos, screenEndX);
			bool yHovered = IsMouseNearLine(mousePos, screenPos, screenEndY);

			IsMouseOverGizmo = xHovered || yHovered;

			if (_draggingX)
				xColor = Color.Yellow;
			else if (xHovered)
				xColor = Color.Orange;

			if (_draggingY)
				yColor = Color.Yellow;
			else if (yHovered)
				yColor = Color.Orange;

			Debug.DrawArrow(center, worldEndX, scaledWidth, scaledWidth, xColor);
			Debug.DrawArrow(center, worldEndY, scaledWidth, scaledWidth, yColor);

			// Convert mouse to world space for dragging
			var worldMousePos = camera.ScreenToWorldPoint(mousePos);
			HandleDragging(selectedEntities, worldMousePos, camera, center, xHovered, yHovered);
		}

		private void HandleDragging(List<Entity> selectedEntities, Vector2 worldMouse, Camera camera, Vector2 center,
			bool xHovered, bool yHovered)
		{
			if (selectedEntities.Count == 0)
				return;

			var mousePos = Input.ScaledMousePosition;

			// Start dragging
			if (!_draggingX && !_draggingY)
			{
				if ((xHovered && yHovered && Input.LeftMouseButtonPressed) ||
				    (xHovered && Input.LeftMouseButtonPressed) ||
				    (yHovered && Input.LeftMouseButtonPressed))
				{
					if (xHovered && yHovered)
					{
						_draggingX = true;
						_draggingY = true;
					}
					else if (xHovered)
					{
						_draggingX = true;
					}
					else if (yHovered)
					{
						_draggingY = true;
					}

					_dragStartEntityPositions.Clear();
					foreach (var entity in selectedEntities)
					{
						_dragStartEntityPositions[entity] = entity.Transform.Position;
					}

					_dragStartWorldMouse = camera.ScreenToWorldPoint(mousePos);
				}
			}

			// Dragging
			if ((_draggingX || _draggingY) && Input.LeftMouseButtonDown)
			{
				var delta = worldMouse - _dragStartWorldMouse;
				foreach (var entity in selectedEntities)
				{
					var startPos = _dragStartEntityPositions.TryGetValue(entity, out var pos)
						? pos
						: entity.Transform.Position;

					if (_draggingX && _draggingY)
					{
						ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
						entity.Transform.Position = startPos + delta;
					}
					else if (_draggingX)
					{
						ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
						entity.Transform.Position = new Vector2(startPos.X + delta.X, startPos.Y);
					}
					else if (_draggingY)
					{
						ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
						entity.Transform.Position = new Vector2(startPos.X, startPos.Y + delta.Y);
					}
				}
			}

			// End drag
			if ((_draggingX || _draggingY) && Input.LeftMouseButtonReleased)
			{
				_draggingX = false;
				_draggingY = false;

				_dragEndEntityPositions = new Dictionary<Entity, Vector2>();
				foreach (var entity in selectedEntities)
				{
					_dragEndEntityPositions[entity] = entity.Transform.Position;
				}

				// Only push undo if any entity moved
				bool anyMoved = selectedEntities.Any(e =>
					_dragStartEntityPositions.TryGetValue(e, out var startPos) &&
					_dragEndEntityPositions.TryGetValue(e, out var endPos) &&
					startPos != endPos
				);

				if (anyMoved)
				{
					EditorChangeTracker.PushUndo(
						new MultiEntityTransformUndoAction(
							selectedEntities.ToList(),
							_dragStartEntityPositions,
							_dragEndEntityPositions,
							$"Moved {string.Join(", ", selectedEntities.Select(e => e.Name))}"
						),
						selectedEntities.First(),
						$"Moved {string.Join(", ", selectedEntities.Select(e => e.Name))}"
					);
				}
			}
		}

		private bool IsMouseNearLine(Vector2 mouse, Vector2 a, Vector2 b, float threshold = 10f)
		{
			var ap = mouse - a;
			var ab = b - a;
			float abLen = ab.Length();
			float t = Math.Clamp(Vector2.Dot(ap, ab) / (abLen * abLen), 0, 1);
			var closest = a + ab * t;
			return (mouse - closest).Length() < threshold;
		}

		/// <summary>
		/// Returns the world-space center point that the gizmo arrows are anchored to for the given entities.
		/// Uses the same filtering and averaging logic as Draw() so the camera target matches the arrow origin exactly.
		/// </summary>
		public Vector2 GetArrowGizmoCenter(IReadOnlyList<Entity> entities)
		{
			var validEntities = GizmoEntityFilter.GetValidEntities(entities);

			if (validEntities.Count == 0)
				return Vector2.Zero;

			var center = Vector2.Zero;
			foreach (var e in validEntities)
				center += e.Transform.Position;

			return center / validEntities.Count;
		}

		/// <summary>
		/// Resets the dragging state
		/// </summary>
		public void Reset()
		{
			_draggingX = false;
			_draggingY = false;
			_dragStartEntityPositions.Clear();
			_dragEndEntityPositions.Clear();

		}
	}
}