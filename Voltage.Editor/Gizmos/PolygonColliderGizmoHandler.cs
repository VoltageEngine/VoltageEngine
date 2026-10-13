using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.PhysicsShapes;
using System.Collections.Generic;
using Voltage.Editor.Undo;
using Voltage.Editor.Undo.ComponentActions;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Gizmos
{
	/// <summary>
	/// Handles gizmo rendering and interaction for polygon colliders and room lights
	/// </summary>
	public class PolygonColliderGizmoHandler
	{
		private bool _isDragging = false;
		private Component _selectedCollider;
		private int _selectedPointIndex = -1;
		private Dictionary<Component, List<Vector2>> _originalPolygonPoints = new();

		public bool IsDragging => _isDragging;
		public bool IsMouseOverGizmo { get; private set; }

		/// <summary>
		/// Draws polygon collider gizmos and handles interaction
		/// </summary>
		public void Draw(List<Component> polygonColliders, Vector2 worldMouse, Camera camera)
		{
			IsMouseOverGizmo = false;

			if (!_isDragging && Input.LeftMouseButtonPressed)
			{
				TryStartDragging(polygonColliders, worldMouse, camera);
			}

			if (_isDragging && Input.LeftMouseButtonDown)
			{
				UpdateDragging(worldMouse);
			}

			if (_isDragging && Input.LeftMouseButtonReleased)
			{
				EndDragging();
			}

			DrawPolygonPoints(polygonColliders, camera);
		}

		private void TryStartDragging(List<Component> polygonColliders, Vector2 worldMouse, Camera camera)
		{
			float minDist = 10f / camera.RawZoom;
			Component closestCollider = null;
			int closestPointIndex = -1;
			float closestDistance = float.MaxValue;

			foreach (var collider in polygonColliders)
			{
				var points = PolygonBoundsEditor.Points(collider);
				if (points == null)
					continue;

				for (int i = 0; i < points.Count; i++)
				{
					var worldPoint = PolygonBoundsEditor.WorldPoint(collider, i);
					float dist = Vector2.Distance(worldMouse, worldPoint);

					if (dist < minDist && dist < closestDistance)
					{
						closestDistance = dist;
						closestCollider = collider;
						closestPointIndex = i;
					}
				}
			}

			if (closestCollider != null && closestPointIndex >= 0)
			{
				_isDragging = true;
				_selectedCollider = closestCollider;
				_selectedPointIndex = closestPointIndex;

				// Store original points for undo
				_originalPolygonPoints.Clear();
				foreach (var collider in polygonColliders)
				{
					_originalPolygonPoints[collider] = new List<Vector2>(PolygonBoundsEditor.Points(collider));
				}
			}
		}

		private void UpdateDragging(Vector2 worldMouse)
		{
			if (_selectedCollider != null && _selectedPointIndex >= 0)
			{
				PolygonBoundsEditor.MovePoint(_selectedCollider, _selectedPointIndex, worldMouse);

				ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
				IsMouseOverGizmo = true;
			}
		}

		private void EndDragging()
		{
			if (_selectedCollider != null)
			{
				var finalPoints = new Dictionary<Component, List<Vector2>>
				{
					[_selectedCollider] = new List<Vector2>(PolygonBoundsEditor.Points(_selectedCollider))
				};

				EditorChangeTracker.PushUndo(
					new PolygonBoundsPointEditUndoAction(
						_selectedCollider,
						_originalPolygonPoints[_selectedCollider],
						finalPoints[_selectedCollider],
						$"Edited bounds vertex on {_selectedCollider.Entity.Name}"
					),
					_selectedCollider.Entity,
					$"Edited bounds vertex"
				);
			}

			_isDragging = false;
			_selectedCollider = null;
			_selectedPointIndex = -1;
			_originalPolygonPoints.Clear();
		}

		private void DrawPolygonPoints(List<Component> polygonColliders, Camera camera)
		{
			float pointSize = 6f / camera.RawZoom;

			foreach (var collider in polygonColliders)
			{
				var points = PolygonBoundsEditor.Points(collider);
				if (points == null)
					continue;

				for (int i = 0; i < points.Count; i++)
				{
					var worldPoint = PolygonBoundsEditor.WorldPoint(collider, i);

					bool isSelected = _isDragging &&
					                  collider == _selectedCollider &&
					                  i == _selectedPointIndex;

					var pointColor = isSelected ? Color.Yellow : Color.Cyan;

					Debug.DrawHollowRect(new RectangleF(
						worldPoint.X - pointSize / 2f,
						worldPoint.Y - pointSize / 2f,
						pointSize,
						pointSize
					), pointColor);

					// Draw lines between points
					if (i < points.Count - 1)
					{
						var nextWorldPoint = PolygonBoundsEditor.WorldPoint(collider, i + 1);
						Debug.DrawLine(worldPoint, nextWorldPoint, Color.Cyan * 0.5f);
					}
					else
					{
						// Connect last point to first
						var firstWorldPoint = PolygonBoundsEditor.WorldPoint(collider, 0);
						Debug.DrawLine(worldPoint, firstWorldPoint, Color.Cyan * 0.5f);
					}
				}

				var centerPoint = collider.Entity.Position;
				Debug.DrawPixel(centerPoint, 4 * Debug.Size.LineSizeMultiplier, new Color(200, 0, 255));
			}
		}

		/// <summary>
		/// Resets the dragging state
		/// </summary>
		public void Reset()
		{
			_isDragging = false;
			_selectedCollider = null;
			_selectedPointIndex = -1;
			_originalPolygonPoints.Clear();
		}
	}
}