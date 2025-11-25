using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage;
using System;
using System.Collections.Generic;
using System.Linq;
using Voltage.Editor.UndoActions;

namespace Voltage.Editor.Gizmos
{
	/// <summary>
	/// Handles gizmo rendering and interaction for entity rotation using a circle
	/// </summary>
	public class EntityRotateGizmoHandler
	{
		private bool _draggingRotate = false;
		private float _dragStartAngle;
		private float _dragStartEntityRotation;

		public bool IsDragging => _draggingRotate;
		public bool IsMouseOverGizmo { get; private set; }

		/// <summary>
		/// Draws entity rotation circle gizmo and handles interaction
		/// </summary>
		public void Draw(List<Entity> selectedEntities, Vector2 worldMouse, Camera camera)
		{
			IsMouseOverGizmo = false;

			var validEntities = GizmoEntityFilter.GetValidEntities(selectedEntities);

			if (validEntities.Count == 0)
				return;

			Vector2 center = Vector2.Zero;
			foreach (var e in validEntities)
				center += e.Transform.Position;
			center /= validEntities.Count;

			var screenCenter = camera.WorldToScreenPoint(center);

			float baseRadius = 30f;
			float minRadius = 28f;
			float maxRadius = 33f;
			float radius = baseRadius / MathF.Max(camera.RawZoom, 0.01f);
			radius = Math.Clamp(radius, minRadius, maxRadius);

			Color circleColor = Color.CornflowerBlue;

			var mousePos = Input.ScaledMousePosition;
			float distToCenter = Vector2.Distance(mousePos, screenCenter);

			// Only allow rotation if cursor is inside the circle
			bool hoveredCircle = distToCenter <= radius;

			if (hoveredCircle)
			{
				circleColor = Color.Orange;
				IsMouseOverGizmo = true;
			}
			if (_draggingRotate)
				circleColor = Color.Yellow;

			Debug.DrawCircle(center, radius / camera.RawZoom, circleColor);

			// Draw up (Y) and right (X) axes for visual reference only
			DrawRotateGizmoAxesUpRight(center, radius, camera, validEntities[0].Transform.Rotation);

			// Start rotation only if mouse is inside the circle
			if (!_draggingRotate && hoveredCircle && Input.LeftMouseButtonPressed)
			{
				_draggingRotate = true;
				_dragStartAngle = MathF.Atan2(mousePos.Y - screenCenter.Y, mousePos.X - screenCenter.X);
				_dragStartEntityRotation = validEntities[0].Transform.Rotation;
			}

			// Apply rotation as long as we're dragging inside the circle
			if (_draggingRotate && Input.LeftMouseButtonDown)
			{
				var currentAngle = MathF.Atan2(mousePos.Y - screenCenter.Y, mousePos.X - screenCenter.X);
				float deltaAngle = currentAngle - _dragStartAngle;

				if (deltaAngle > MathF.PI) deltaAngle -= MathF.PI * 2;
				if (deltaAngle < -MathF.PI) deltaAngle += MathF.PI * 2;

				foreach (var entity in validEntities)
					entity.Transform.Rotation = _dragStartEntityRotation + deltaAngle;

				ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
			}

			if (_draggingRotate && Input.LeftMouseButtonReleased)
			{
				_draggingRotate = false;

				var startRotations = new Dictionary<Entity, float>();
				var endRotations = new Dictionary<Entity, float>();
				foreach (var entity in validEntities)
				{
					startRotations[entity] = _dragStartEntityRotation;
					endRotations[entity] = entity.Transform.Rotation;
				}

				bool anyRotated = validEntities.Any(e =>
					startRotations.TryGetValue(e, out var startRot) &&
					endRotations.TryGetValue(e, out var endRot) &&
					startRot != endRot
				);

				if (anyRotated)
				{
					EditorChangeTracker.PushUndo(
						new MultiEntityRotationUndoAction(
							validEntities.ToList(),
							startRotations,
							endRotations,
							$"Rotated {string.Join(", ", validEntities.Select(e => e.Name))}"
						),
						validEntities.First(),
						$"Rotated {string.Join(", ", validEntities.Select(e => e.Name))}"
					);
				}
			}
		}

		private void DrawRotateGizmoAxesUpRight(Vector2 center, float radius, Camera camera, float rotation)
		{
			float axisLength = radius * 0.7f / camera.RawZoom;

			// Right (X) axis
			Vector2 axisXDir = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
			Vector2 axisXStart = center;
			Vector2 axisXEnd = center + axisXDir * axisLength;

			// Up (Y) axis (90 degrees offset)
			float axisYAngle = rotation - MathF.PI / 2f;
			Vector2 axisYDir = new Vector2(MathF.Cos(axisYAngle), MathF.Sin(axisYAngle));
			Vector2 axisYStart = center;
			Vector2 axisYEnd = center + axisYDir * axisLength;

			Debug.DrawLine(axisXStart, axisXEnd, Color.Red);
			Debug.DrawLine(axisYStart, axisYEnd, Color.LimeGreen);
		}

		/// <summary>
		/// Resets the dragging state
		/// </summary>
		public void Reset()
		{
			_draggingRotate = false;
			_dragStartAngle = 0f;
			_dragStartEntityRotation = 0f;
		}
	}
}