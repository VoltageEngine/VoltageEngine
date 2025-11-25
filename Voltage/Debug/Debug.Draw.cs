using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.PhysicsShapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Voltage.BitmapFonts;
using Voltage.Utils.Fonts;

namespace Voltage
{
	public static partial class Debug
	{
		public static bool DrawTextFromBottom;

		static List<DebugDrawItem> _debugDrawItems = new List<DebugDrawItem>();
		static List<DebugDrawItem> _screenSpaceDebugDrawItems = new List<DebugDrawItem>();

		[Conditional("DEBUG")]
		internal static void Render()
		{
			if (_debugDrawItems.Count > 0)
			{
				if (Core.Scene != null && Core.Scene.Camera != null)
					Graphics.Instance.Batcher.Begin(Core.Scene.Camera.TransformMatrix);
				else
					Graphics.Instance.Batcher.Begin();

				for (var i = _debugDrawItems.Count - 1; i >= 0; i--)
				{
					var item = _debugDrawItems[i];
					if (item.Draw(Graphics.Instance.Batcher))
						_debugDrawItems.RemoveAt(i);
				}

				Graphics.Instance.Batcher.End();
			}

			if (_screenSpaceDebugDrawItems.Count > 0)
			{
				var pos = DrawTextFromBottom ? new Vector2(0, Core.Scene.SceneRenderTargetSize.Y) : Vector2.Zero;
				Graphics.Instance.Batcher.Begin();

				for (var i = _screenSpaceDebugDrawItems.Count - 1; i >= 0; i--)
				{
					var item = _screenSpaceDebugDrawItems[i];
					var itemHeight = item.GetHeight();

					if (DrawTextFromBottom)
						item.Position = pos - new Vector2(0, itemHeight);
					else
						item.Position = pos;

					if (item.Draw(Graphics.Instance.Batcher))
						_screenSpaceDebugDrawItems.RemoveAt(i);

					if (DrawTextFromBottom)
						pos.Y -= itemHeight;
					else
						pos.Y += itemHeight;
				}

				Graphics.Instance.Batcher.End();
			}
		}

		[Conditional("DEBUG")]
		public static void DrawLine(Vector2 start, Vector2 end, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(start, end, color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawPixel(float x, float y, int size, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(x, y, size, color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawPixel(Vector2 position, int size, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(position.X, position.Y, size, color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawHollowRect(Rectangle rectangle, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(rectangle, color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawHollowBox(Vector2 center, int size, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			var halfSize = size * 0.5f;
			_debugDrawItems.Add(new DebugDrawItem(
				new Rectangle((int)(center.X - halfSize), (int)(center.Y - halfSize), size, size), color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawHollowBox(Vector2 center, int sizeX, int sizeY, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			var halfXSize = sizeX * 0.5f;
			var halfYSize = sizeY * 0.5f;
			_debugDrawItems.Add(new DebugDrawItem(
				new Rectangle((int)(center.X - sizeX), (int)(center.Y - sizeY), sizeX, sizeY), color, duration));
		}

		[Conditional("DEBUG")]
		public static void DrawText(BitmapFont font, string text, Vector2 position, Color color, float duration = 0f,
									float scale = 1f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(font, text, position, color, duration, scale));
		}

		[Conditional("DEBUG")]
		public static void DrawPolygon(Vector2 position, Vector2[] points, Color color, bool closePoly = true, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled || points == null || points.Length < 2)
				return;

			for (int i = 1; i < points.Length; i++)
				DrawLine(position + points[i - 1], position + points[i], color, duration);

			if (closePoly)
				DrawLine(position + points[points.Length - 1], position + points[0], color, duration);
		}

		[Conditional("DEBUG")]
		public static void DrawCircle(Vector2 center, float radius, Color color, float duration = 0f, int resolution = 32)
		{
			if (!Core.DebugRenderEnabled)
				return;

			float increment = MathHelper.TwoPi / resolution;
			Vector2 prev = center + new Vector2(radius, 0);

			for (int i = 1; i <= resolution; i++)
			{
				float angle = i * increment;
				Vector2 next = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
				DrawLine(prev, next, color, duration);
				prev = next;
			}
		}

		public static void DrawColliderDelayed(Collider collider, Color color, float duration)
		{
			if (collider == null)
				return;
		
			// BoxCollider
			if (collider is BoxCollider)
			{
				DrawHollowRect(collider.Bounds, color, duration);
			}
			// CircleCollider
			else if (collider is CircleCollider circle)
			{
				var center = circle.Shape.Position;
				var radius = circle.Radius;
				DrawCircle(center, radius, color, duration);
			}
			// PolygonCollider
			else if (collider is PolygonCollider polygon)
			{
				var poly = polygon.Shape as Polygon;
				if (poly != null)
				{
					DrawPolygon(polygon.Shape.Position, poly.Points, color, true, duration);
				}
			}
			// Fallback: draw bounds as rectangle
			else
			{
				DrawHollowRect(collider.Bounds, color, duration);
			}
		}

		[Conditional("DEBUG")]
		public static void DrawText(VoltageSpriteFont font, string text, Vector2 position, Color color, float duration = 0f,
									float scale = 1f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_debugDrawItems.Add(new DebugDrawItem(font, text, position, color, duration, scale));
		}

		[Conditional("DEBUG")]
		public static void DrawText(string text, float duration = 0)
		{
			DrawText(text, Colors.DebugText, duration);
		}

		[Conditional("DEBUG")]
		public static void DrawText(string format, params object[] args)
		{
			var text = string.Format(format, args);
			DrawText(text, Colors.DebugText);
		}

		[Conditional("DEBUG")]
		public static void DrawText(string text, Color color, float duration = 1f, float scale = 1f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			_screenSpaceDebugDrawItems.Add(new DebugDrawItem(text, color, duration, scale));
		}

		[Conditional("DEBUG")]
		public static void DrawRect(Rectangle rectangle, Color color, float duration = 0f)
		{
			if (!Core.DebugRenderEnabled)
				return;

			// This will draw a filled rectangle using the pixel texture
			_debugDrawItems.Add(new DebugDrawItem(rectangle, color, duration) { drawType = DebugDrawItem.DebugDrawType.FilledRectangle });
		}

		/// <summary>
		/// Draws an arrow from start to end, with customizable arrowhead.
		/// </summary>
		public static void DrawArrow(Vector2 start, Vector2 end, float headLength = 12f, float headWidth = 3f, Color color = default, float duration = 0f)
		{
			DrawLine(start, end, color, duration);

			// Calculate direction
			var direction = end - start;
			if (direction.LengthSquared() < 0.01f)
				return;

			direction.Normalize();

			// Arrowhead base
			var arrowBase = end - direction * headLength;

			// Perpendicular vector
			var perp = new Vector2(-direction.Y, direction.X);

			// Arrowhead points
			var left = arrowBase + perp * headWidth;
			var right = arrowBase - perp * headWidth;

			// Draw arrowhead (as lines)
			DrawLine(end, left, color, duration);
			DrawLine(end, right, color, duration);
		}
	}
}
