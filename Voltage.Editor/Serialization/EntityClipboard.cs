using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Utils;
using Voltage.Persistence;

namespace Voltage.Editor.Serialization
{
	/// <summary>
	/// Copy/paste for entities, held as scene-format DATA rather than live <see cref="Entity"/> references.
	/// </summary>
	/// <remarks>
	/// Data is what makes this work across scenes: a scene swap destroys every entity in it, so a clipboard of
	/// live references would hold torn-down objects. It is mirrored to the OS clipboard as JSON, so a copy can
	/// also be pasted into a second editor instance. The in-process copy is authoritative when both are present -
	/// it survives the user copying unrelated text in another app.
	/// </remarks>
	public static class EntityClipboard
	{
		private const string FormatMarker = "VoltageEntityClipboard";

		/// <summary>Bump when the payload shape changes; older payloads are then ignored rather than mis-pasted.</summary>
		private const int CurrentVersion = 1;

		public sealed class Payload
		{
			public string Format = FormatMarker;
			public int Version = CurrentVersion;
			public List<SceneData.SceneEntityData> Entities = new();
		}

		// Kept as JSON, not as objects: every paste deserializes a fresh copy, so pasting twice cannot alias the
		// same data (InstantiateEntityData rewrites ids in place).
		private static string _inProcess;

		public static bool HasContent => TryReadPayloadJson(out _);

		/// <summary>Captures entities and their descendants. Returns how many entries were stored.</summary>
		public static int Copy(IEnumerable<Entity> entities)
		{
			var scene = Core.Scene;
			if (scene == null || entities == null)
				return 0;

			try
			{
				var payload = new Payload { Entities = scene.CaptureEntityData(entities) };
				if (payload.Entities.Count == 0)
					return 0;

				_inProcess = Json.ToJson(payload, Settings());
				SdlClipboard.TrySetText(_inProcess);

				return payload.Entities.Count;
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"EntityClipboard: could not copy: {ex.Message}", "Entity");
				return 0;
			}
		}

		/// <summary>
		/// Pastes into the CURRENT scene - which is what makes cross-scene paste work: copy, switch scene, paste.
		/// Returns the new root entities.
		/// </summary>
		public static List<Entity> Paste(Vector2 offset = default)
		{
			var scene = Core.Scene;
			if (scene == null || !TryReadPayloadJson(out var json))
				return new List<Entity>();

			try
			{
				var payload = Json.FromJson<Payload>(json, Settings());
				if (payload?.Entities == null || payload.Entities.Count == 0)
					return new List<Entity>();

				return scene.InstantiateEntityData(payload.Entities, offset);
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"EntityClipboard: could not paste: {ex.Message}", "Entity");
				return new List<Entity>();
			}
		}

		public static void Clear() => _inProcess = null;

		/// <summary>
		/// Prefers this process's copy, then the OS clipboard - so text copied in another app never shadows a
		/// copy made here, but a copy made in another editor instance is still available.
		/// </summary>
		private static bool TryReadPayloadJson(out string json)
		{
			json = null;

			if (!string.IsNullOrEmpty(_inProcess))
			{
				json = _inProcess;
				return true;
			}

			if (!SdlClipboard.TryGetText(out var text) || string.IsNullOrEmpty(text))
				return false;

			// Cheap reject before parsing: the clipboard usually holds unrelated text.
			if (text.IndexOf(FormatMarker, StringComparison.Ordinal) < 0)
				return false;

			json = text;
			return true;
		}

		// Matches SceneData.Clone so SceneEntityData round-trips exactly as it does in a .vscene.
		private static JsonSettings Settings() => new()
		{
			PrettyPrint = false,
			TypeNameHandling = TypeNameHandling.Auto,
		};
	}
}
