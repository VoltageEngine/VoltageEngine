using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage.Cinematics;
using Voltage.Editor.Assets;
using Voltage.Editor.FilePickers;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;
using Voltage.Utils.Tweens.Easing;
using Num = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace Voltage.Editor.Windows
{
	/// <summary>
	/// Authoring UI for cinematic timelines. Follows the "edit through a director" model: it edits the
	/// <see cref="TimelineAsset"/> of the currently-selected entity's <see cref="TimelineDirector"/>, so the
	/// director's per-scene role bindings drive a live preview (scrub/play evaluate against real entities),
	/// while the data saves to the reusable <c>.timeline</c> asset.
	/// </summary>
	public class TimelineWindow
	{
		public bool IsOpen;

		private ImGuiManager _imgui;
		private TimelineDirector _director;      // the selected entity's director we're editing through
		private TimelineAsset _asset;          // working copy (also handed to the director for preview)
		private float _scrub;
		private bool _previewPlaying;
		private string _status;

		// Graphical timeline canvas.
		private float _pixelsPerSecond = 80f;

		// Record mode: while on, moving a bound entity in the scene auto-keyframes it at the playhead.
		private bool _recording;
		private readonly Dictionary<string, RecordSample> _recordBaseline = new();

		private struct RecordSample
		{
			public Xna.Vector2 Pos;
			public float Rot;
			public Xna.Vector2 Scale;
			public float Zoom;
			public bool HasCamera;
		}

		private enum LaneKind { Transform, Camera, Event, Spawn }

		private struct Lane
		{
			public string Name;
			public LaneKind Kind;
			public List<float> Marks;              // instant keyframes / events
			public List<(float Start, float Len)> Ranges;  // ranged events
			public float Start, Length;            // spawn bar
		}

		private static readonly Num.Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);
		private static readonly Num.Vector4 Ok = new(0.3f, 1f, 0.4f, 1f);
		private static readonly Num.Vector4 Warn = new(1f, 0.8f, 0.2f, 1f);

		private static readonly string[] EaseNames = Enum.GetNames(typeof(EaseType));
		private static readonly string[] SkipNames = Enum.GetNames(typeof(SkipBehavior));

		public void Draw()
		{
			if (!IsOpen)
				return;

			_imgui ??= Core.GetGlobalManager<ImGuiManager>();

			ImGui.SetNextWindowSize(new Num.Vector2(820, 560), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin("Timeline ###TimelineWindow", ref IsOpen))
			{
				ImGui.End();
				return;
			}

			SyncToSelectedDirector();

			if (_director == null)
			{
				ImGui.TextColored(Muted, "Select an entity with a Timeline Director to edit its timeline.");
				ImGui.Spacing();
				ImGui.TextWrapped("Drag a .timeline asset into the scene (or add a Timeline Director component) to get started.");
				ImGui.End();
				return;
			}

			if (_asset == null)
			{
				DrawNoAssetState();
				ImGui.End();
				return;
			}

			// Keep the director previewing against our working copy.
			_director.SetAsset(_asset);

			DrawTransport();

			if (_recording)
				HandleRecording();

			DrawTimelineCanvas();

			if (!string.IsNullOrEmpty(_status))
			{
				ImGui.TextColored(Ok, _status);
			}
			ImGui.Separator();

			if (ImGui.BeginTabBar("TimelineTabs"))
			{
				if (ImGui.BeginTabItem("Roles"))      { DrawRoles(); ImGui.EndTabItem(); }
				if (ImGui.BeginTabItem("Events"))     { DrawEvents(); ImGui.EndTabItem(); }
				if (ImGui.BeginTabItem("Transform"))  { DrawTransformTracks(); ImGui.EndTabItem(); }
				if (ImGui.BeginTabItem("Spawns"))     { DrawSpawns(); ImGui.EndTabItem(); }
				ImGui.EndTabBar();
			}

			ImGui.End();
		}

		#region Director / asset sync

		private void SyncToSelectedDirector()
		{
			var selected = _imgui?.SceneGraphWindow?.EntityPane?.SelectedEntities?.FirstOrDefault();
			var director = selected?.GetComponent<TimelineDirector>();

			if (ReferenceEquals(director, _director))
				return;

			// Selection changed — stop any preview on the old director and load the new one's asset.
			StopPreview();
			_director = director;
			_asset = _director != null ? LoadOrNull(_director) : null;
			_scrub = 0f;
		}

		private static TimelineAsset LoadOrNull(TimelineDirector director)
		{
			if (director.Asset != null)
				return director.Asset;

			var path = director.Timeline.ResolvePath();
			if (!string.IsNullOrEmpty(path) && File.Exists(path))
			{
				try { return TimelineAssetIO.Load(path); }
				catch (Exception ex) { Voltage.Debug.Warn($"[Timeline] load failed: {ex.Message}"); }
			}
			return null;
		}

		private void DrawNoAssetState()
		{
			ImGui.TextColored(Warn, "This director has no timeline asset loaded.");
			ImGui.Spacing();

			var path = _director.Timeline.ResolvePath();
			if (!string.IsNullOrEmpty(path))
				ImGui.TextColored(Muted, $"Expected: {path}");

			ImGui.Spacing();
			DrawFileButtons();
		}

		#endregion

		#region Transport (play / scrub / save)

		private void DrawTransport()
		{
			// Manually pump the director while previewing (in edit mode its Update() isn't called).
			if (_previewPlaying)
			{
				_director.Update();
				_scrub = _director.PlayheadTime;
				if (_director.State == DirectorState.Stopped)
					_previewPlaying = false;
			}

			if (ImGui.Button(_previewPlaying ? "Pause" : "Play"))
			{
				if (_previewPlaying) { _director.Pause(); _previewPlaying = false; }
				else { SetRecording(false); if (_director.State != DirectorState.Paused) _director.Play(); else _director.Resume(); _previewPlaying = true; }
			}
			ImGui.SameLine();
			if (ImGui.Button("Stop"))
				StopPreview();

			// Record toggle — red when armed.
			ImGui.SameLine();
			if (_recording)
			{
				ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.75f, 0.15f, 0.15f, 1f));
				ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.9f, 0.25f, 0.25f, 1f));
			}
			if (ImGui.Button(_recording ? "● Recording" : "● Record"))
				SetRecording(!_recording);
			if (_recording)
				ImGui.PopStyleColor(2);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Record: move a bound entity in the scene (or zoom its camera) and a keyframe is\nwritten at the playhead. Scrub to a time first, then move the entity.");

			ImGui.SameLine();
			ImGui.TextColored(Muted, $"| {_scrub:0.00}s");

			// Zoom controls.
			ImGui.SameLine();
			if (ImGui.SmallButton("-"))
				_pixelsPerSecond = Math.Max(12f, _pixelsPerSecond / 1.25f);
			ImGui.SameLine();
			if (ImGui.SmallButton("+"))
				_pixelsPerSecond = Math.Min(600f, _pixelsPerSecond * 1.25f);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Zoom (or scroll the wheel over the timeline).");

			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			var dur = _asset.Duration;
			if (ImGui.InputFloat("Length", ref dur, 0f, 0f, "%.2f"))
				_asset.Duration = Math.Max(0.1f, dur);

			ImGui.SameLine();
			if (ImGui.Button("Save"))
				SaveIfPathKnown();
			ImGui.SameLine();
			if (ImGui.Button("Load..."))
				LoadTimeline();
		}

		/// <summary>New / Load buttons — create a fresh .timeline file or open an existing one for this director.</summary>
		private void DrawFileButtons()
		{
			if (ImGui.Button("New Timeline..."))
				NewTimeline();
			ImGui.SameLine();
			if (ImGui.Button("Load Timeline..."))
				LoadTimeline();

			if (!NativeFileDialogs.IsAvailable)
			{
				ImGui.SameLine();
				ImGui.TextColored(Muted, "(native file dialogs unavailable on this platform)");
			}
		}

		private void NewTimeline()
		{
			var start = DefaultTimelineFolder();
			if (!NativeFileDialogs.TrySaveFile("New Timeline", Path.Combine(start, "NewTimeline.timeline"),
				    new[] { "*" + TimelineAssetIO.FileExtension }, "Voltage Timeline", out var path) || string.IsNullOrEmpty(path))
				return;

			if (!path.EndsWith(TimelineAssetIO.FileExtension, StringComparison.OrdinalIgnoreCase))
				path += TimelineAssetIO.FileExtension;

			try
			{
				_asset = TimelineAssetIO.CreateAndSave(path);
				AssignTimelineToDirector(path);
				_scrub = 0f;
				_status = $"Created {Path.GetFileName(path)}.";
			}
			catch (Exception ex)
			{
				_status = $"Create failed: {ex.Message}";
			}
		}

		private void LoadTimeline()
		{
			var start = DefaultTimelineFolder();
			if (!NativeFileDialogs.TryOpenFile("Load Timeline", start,
				    new[] { "*" + TimelineAssetIO.FileExtension }, "Voltage Timeline", out var path) || string.IsNullOrEmpty(path))
				return;

			try
			{
				var loaded = TimelineAssetIO.Load(path);
				if (loaded == null)
				{
					_status = "Could not load that timeline.";
					return;
				}
				_asset = loaded;
				AssignTimelineToDirector(path);
				_scrub = 0f;
				_status = $"Loaded {Path.GetFileName(path)}.";
			}
			catch (Exception ex)
			{
				_status = $"Load failed: {ex.Message}";
			}
		}

		/// <summary>Points the selected director's Timeline reference at <paramref name="path"/> (GUID + path) and previews it.</summary>
		private void AssignTimelineToDirector(string path)
		{
			var guid = AssetDatabase.Instance?.GetOrCreateGuid(path) ?? Guid.Empty;
			_director.Timeline = new Voltage.Serialization.AssetReference
			{
				AssetGuid = guid,
				AssetPath = path,
				AssetName = Path.GetFileNameWithoutExtension(path),
			};
			_director.SetAsset(_asset);
			AssetDatabase.Instance?.Refresh();   // catalog the new file so it appears in the browser
		}

		private static string DefaultTimelineFolder()
		{
			if (ProjectManager.Instance.HasActiveProject)
			{
				var dir = Path.Combine(ProjectManager.Instance.CurrentProject.DataFolder, "Timelines");
				try { Directory.CreateDirectory(dir); } catch { /* fall through to base dir */ }
				if (Directory.Exists(dir))
					return dir;
			}
			return AppContext.BaseDirectory;
		}

		private void StopPreview()
		{
			if (_director != null && _director.State != DirectorState.Stopped)
				_director.Cancel();   // abort preview and restore pre-preview entity state
			_previewPlaying = false;
			_scrub = 0f;
		}

		private void SaveIfPathKnown()
		{
			var path = _director.Timeline.ResolvePath();
			if (string.IsNullOrEmpty(path))
			{
				_status = "No .timeline file path — assign the director's Timeline asset first.";
				return;
			}
			try
			{
				_asset.InvalidateEventOrder();
				TimelineAssetIO.Save(_asset, path);
				_status = $"Saved to {Path.GetFileName(path)}.";
			}
			catch (Exception ex)
			{
				_status = $"Save failed: {ex.Message}";
			}
		}

		#endregion

		#region Graphical canvas

		/// <summary>
		/// A Unity-style timeline box: a per-second vertical grid you can zoom into, track lanes showing
		/// keyframes / events / spawn bars, and a draggable playhead. Click or drag in the time area to scrub.
		/// </summary>
		private void DrawTimelineCanvas()
		{
			var duration = Math.Max(0.1f, _asset.Duration);
			const float labelW = 150f;
			const float rulerH = 22f;
			const float trackH = 24f;

			var lanes = BuildLanes();
			var pps = _pixelsPerSecond;
			var contentW = labelW + duration * pps + 60f;
			var contentH = rulerH + Math.Max(1, lanes.Count) * trackH + 6f;
			var boxH = Math.Min(300f, contentH + 6f);

			var io = ImGui.GetIO();

			ImGui.BeginChild("##tlcanvas", new Num.Vector2(0, boxH), true, ImGuiWindowFlags.HorizontalScrollbar);

			var dl = ImGui.GetWindowDrawList();
			var origin = ImGui.GetCursorScreenPos();
			ImGui.InvisibleButton("##tlcontent", new Num.Vector2(contentW, contentH));
			var hovered = ImGui.IsItemHovered();
			var mouse = ImGui.GetMousePos();

			var timeX = origin.X + labelW;
			var bottomY = origin.Y + contentH;

			var colGridSec = ImGui.GetColorU32(new Num.Vector4(1, 1, 1, 0.12f));
			var colGridMin = ImGui.GetColorU32(new Num.Vector4(1, 1, 1, 0.05f));
			var colText = ImGui.GetColorU32(new Num.Vector4(0.8f, 0.8f, 0.8f, 1f));
			var colRuler = ImGui.GetColorU32(new Num.Vector4(0.08f, 0.08f, 0.08f, 1f));
			var colGutter = ImGui.GetColorU32(new Num.Vector4(0.13f, 0.13f, 0.14f, 1f));
			var colLaneAlt = ImGui.GetColorU32(new Num.Vector4(1, 1, 1, 0.035f));
			var colPlay = ImGui.GetColorU32(new Num.Vector4(1f, 0.25f, 0.25f, 1f));
			var colKey = ImGui.GetColorU32(new Num.Vector4(0.4f, 0.8f, 1f, 1f));
			var colCam = ImGui.GetColorU32(new Num.Vector4(0.8f, 0.6f, 1f, 1f));
			var colEvent = ImGui.GetColorU32(new Num.Vector4(1f, 0.8f, 0.2f, 1f));
			var colSpawn = ImGui.GetColorU32(new Num.Vector4(0.5f, 1f, 0.5f, 0.45f));

			// Ruler background + per-second grid (with minor lines that appear as you zoom in).
			dl.AddRectFilled(new Num.Vector2(origin.X, origin.Y), new Num.Vector2(origin.X + contentW, origin.Y + rulerH), colRuler);
			var minorStep = pps >= 220f ? 0.1f : pps >= 90f ? 0.5f : 1f;
			for (var t = 0f; t <= duration + 0.0001f; t += minorStep)
			{
				var x = timeX + t * pps;
				var isSecond = Math.Abs(t - MathF.Round(t)) < 0.001f;
				dl.AddLine(new Num.Vector2(x, origin.Y + (isSecond ? 0f : rulerH)), new Num.Vector2(x, bottomY),
					isSecond ? colGridSec : colGridMin);
				if (isSecond)
					dl.AddText(new Num.Vector2(x + 3f, origin.Y + 4f), colText, ((int)MathF.Round(t)) + "s");
			}

			// Track lanes + name gutter.
			for (var i = 0; i < lanes.Count; i++)
			{
				var lane = lanes[i];
				var y = origin.Y + rulerH + i * trackH;
				var cy = y + trackH * 0.5f;

				if (i % 2 == 0)
					dl.AddRectFilled(new Num.Vector2(timeX, y), new Num.Vector2(origin.X + contentW, y + trackH), colLaneAlt);

				if (lane.Kind == LaneKind.Spawn)
					dl.AddRectFilled(new Num.Vector2(timeX + lane.Start * pps, y + 4f),
						new Num.Vector2(timeX + (lane.Start + lane.Length) * pps, y + trackH - 4f), colSpawn, 3f);

				if (lane.Ranges != null)
					foreach (var r in lane.Ranges)
						dl.AddRectFilled(new Num.Vector2(timeX + r.Start * pps, cy - 3f),
							new Num.Vector2(timeX + (r.Start + r.Len) * pps, cy + 3f), colEvent, 2f);

				var markColor = lane.Kind switch
				{
					LaneKind.Event => colEvent,
					LaneKind.Camera => colCam,
					_ => colKey,
				};
				if (lane.Marks != null)
					foreach (var m in lane.Marks)
					{
						var x = timeX + m * pps;
						dl.AddQuadFilled(new Num.Vector2(x, cy - 5f), new Num.Vector2(x + 5f, cy),
							new Num.Vector2(x, cy + 5f), new Num.Vector2(x - 5f, cy), markColor);
					}

				dl.AddRectFilled(new Num.Vector2(origin.X, y), new Num.Vector2(timeX, y + trackH), colGutter);
				dl.AddText(new Num.Vector2(origin.X + 6f, y + 5f), colText, lane.Name);
			}

			// Gutter corner over the ruler.
			dl.AddRectFilled(new Num.Vector2(origin.X, origin.Y), new Num.Vector2(timeX, origin.Y + rulerH), colGutter);
			if (_recording)
				dl.AddText(new Num.Vector2(origin.X + 6f, origin.Y + 4f), colPlay, "REC");

			// Playhead.
			var px = timeX + _scrub * pps;
			dl.AddLine(new Num.Vector2(px, origin.Y), new Num.Vector2(px, bottomY), colPlay, 2f);
			dl.AddTriangleFilled(new Num.Vector2(px - 5f, origin.Y), new Num.Vector2(px + 5f, origin.Y),
				new Num.Vector2(px, origin.Y + 8f), colPlay);

			// Scrub by clicking/dragging in the time area.
			if (hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left) && mouse.X >= timeX)
			{
				_previewPlaying = false;
				_scrub = Math.Clamp((mouse.X - timeX) / pps, 0f, duration);
				_director.Evaluate(_scrub);
				RefreshRecordBaseline();
			}

			// Zoom with the mouse wheel over the canvas.
			if (hovered && io.MouseWheel != 0f)
				_pixelsPerSecond = Math.Clamp(_pixelsPerSecond * (1f + io.MouseWheel * 0.12f), 12f, 600f);

			ImGui.EndChild();
		}

		private List<Lane> BuildLanes()
		{
			var lanes = new List<Lane>();

			foreach (var track in _asset.ParameterTracks)
			{
				switch (track)
				{
					case TimelineTransformTrack tt:
					{
						var marks = new List<float>();
						foreach (var k in tt.Position) marks.Add(k.Time);
						foreach (var k in tt.Rotation) marks.Add(k.Time);
						foreach (var k in tt.Scale) marks.Add(k.Time);
						lanes.Add(new Lane { Name = $"{tt.TargetRole} - transform", Kind = LaneKind.Transform, Marks = marks });
						break;
					}
					case TimelineCameraTrack ct:
						lanes.Add(new Lane { Name = $"{ct.TargetRole} - zoom", Kind = LaneKind.Camera, Marks = ct.Zoom.Select(k => k.Time).ToList() });
						break;
				}
			}

			if (_asset.Events.Count > 0)
			{
				lanes.Add(new Lane
				{
					Name = "events",
					Kind = LaneKind.Event,
					Marks = _asset.Events.Where(e => e.Duration <= 0f).Select(e => e.Time).ToList(),
					Ranges = _asset.Events.Where(e => e.Duration > 0f).Select(e => (e.Time, e.Duration)).ToList(),
				});
			}

			foreach (var s in _asset.SpawnClips)
				lanes.Add(new Lane { Name = $"spawn - {s.SpawnRole}", Kind = LaneKind.Spawn, Start = s.Time, Length = s.Duration });

			return lanes;
		}

		#endregion

		#region Recording

		private void SetRecording(bool on)
		{
			if (on == _recording)
				return;
			_recording = on;
			if (on)
			{
				_previewPlaying = false;
				_director.Evaluate(_scrub);   // pose entities at the current time, then capture as the baseline
				RefreshRecordBaseline();
				_status = "Recording - move a bound entity in the scene to keyframe it at the playhead.";
			}
			else
			{
				_recordBaseline.Clear();
			}
		}

		/// <summary>Captures the current transform (and camera zoom) of every bound role as the record baseline.</summary>
		private void RefreshRecordBaseline()
		{
			if (!_recording)
				return;

			_recordBaseline.Clear();
			foreach (var role in _asset.Roles)
			{
				var e = RoleEntity(role.Name);
				if (e == null)
					continue;
				var cam = e.GetComponent<Camera>();
				_recordBaseline[role.Name] = new RecordSample
				{
					Pos = e.Position, Rot = e.Rotation, Scale = e.Scale,
					Zoom = cam?.RawZoom ?? 0f, HasCamera = cam != null,
				};
			}
		}

		/// <summary>
		/// Each frame while recording: if a bound entity moved (or its camera zoomed) since the baseline,
		/// write/update a keyframe at the playhead. This is the "move it in the scene -> it's recorded" flow.
		/// </summary>
		private void HandleRecording()
		{
			var dirty = false;

			foreach (var role in _asset.Roles)
			{
				var e = RoleEntity(role.Name);
				if (e == null)
					continue;

				if (!_recordBaseline.TryGetValue(role.Name, out var b))
				{
					RefreshRecordBaseline();
					continue;
				}

				var cam = e.GetComponent<Camera>();
				var moved = !NearV(e.Position, b.Pos) || !Near(e.Rotation, b.Rot) || !NearV(e.Scale, b.Scale);
				var zoomed = b.HasCamera && cam != null && !Near(cam.RawZoom, b.Zoom);

				if (moved)
					UpsertTransformKey(role.Name, _scrub, e.Position, e.Rotation, e.Scale);
				if (zoomed)
					UpsertCameraKey(role.Name, _scrub, cam.RawZoom);

				if (moved || zoomed)
				{
					_recordBaseline[role.Name] = new RecordSample
					{
						Pos = e.Position, Rot = e.Rotation, Scale = e.Scale,
						Zoom = cam?.RawZoom ?? 0f, HasCamera = cam != null,
					};
					dirty = true;
				}
			}

			if (dirty)
				_asset.InvalidateEventOrder();
		}

		private Entity RoleEntity(string role)
		{
			if (string.IsNullOrEmpty(role))
				return null;
			var binding = _director.Bindings.FirstOrDefault(b => b.Role == role);
			if (binding == null || !binding.Entity.IsValid)
				return null;

			var scene = Core.Scene;
			if (scene == null)
				return null;

			var id = binding.Entity.GetPersistentId();
			var e = id != Guid.Empty ? scene.FindEntityByPersistentId(id) : null;
			if (e == null && !string.IsNullOrEmpty(binding.Entity.EntityName))
				e = scene.FindEntity(binding.Entity.EntityName);
			return e;
		}

		private void UpsertTransformKey(string role, float time, Xna.Vector2 pos, float rot, Xna.Vector2 scale)
		{
			var track = _asset.ParameterTracks.OfType<TimelineTransformTrack>().FirstOrDefault(t => t.TargetRole == role);
			if (track == null)
			{
				track = new TimelineTransformTrack { TargetRole = role };
				_asset.ParameterTracks.Add(track);
			}
			UpsertVector2(track.Position, time, pos);
			UpsertFloat(track.Rotation, time, rot);
			UpsertVector2(track.Scale, time, scale);
		}

		private void UpsertCameraKey(string role, float time, float zoom)
		{
			var track = _asset.ParameterTracks.OfType<TimelineCameraTrack>().FirstOrDefault(t => t.TargetRole == role);
			if (track == null)
			{
				track = new TimelineCameraTrack { TargetRole = role };
				_asset.ParameterTracks.Add(track);
			}
			UpsertFloat(track.Zoom, time, zoom);
		}

		private static void UpsertVector2(List<Vector2Keyframe> keys, float time, Xna.Vector2 value)
		{
			var existing = keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.001f);
			if (existing != null) { existing.Value = value; return; }
			keys.Add(new Vector2Keyframe { Time = time, Value = value });
			keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		}

		private static void UpsertFloat(List<FloatKeyframe> keys, float time, float value)
		{
			var existing = keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.001f);
			if (existing != null) { existing.Value = value; return; }
			keys.Add(new FloatKeyframe { Time = time, Value = value });
			keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		}

		private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
		private static bool NearV(Xna.Vector2 a, Xna.Vector2 b) => Near(a.X, b.X) && Near(a.Y, b.Y);

		#endregion

		#region Roles

		private void DrawRoles()
		{
			ImGui.TextColored(Muted, "Abstract actor slots this timeline drives. Bind each to a scene entity on the director.");
			ImGui.Spacing();

			for (var i = 0; i < _asset.Roles.Count; i++)
			{
				var role = _asset.Roles[i];
				ImGui.PushID(i);

				var name = role.Name ?? "";
				ImGui.SetNextItemWidth(200);
				if (ImGui.InputText("Role", ref name, 64))
					role.Name = name;

				ImGui.SameLine();
				DrawRoleBindingCombo(role.Name);

				ImGui.SameLine();
				if (ImGui.SmallButton("X"))
				{
					_asset.Roles.RemoveAt(i);
					ImGui.PopID();
					break;
				}
				ImGui.PopID();
			}

			if (ImGui.Button("Add Role"))
				_asset.Roles.Add(new TimelineRole { Name = "Role" + _asset.Roles.Count });
		}

		private void DrawRoleBindingCombo(string role)
		{
			if (string.IsNullOrEmpty(role))
				return;

			var binding = _director.Bindings.FirstOrDefault(b => b.Role == role);
			var current = binding?.Entity.EntityName ?? "(unbound)";

			ImGui.SetNextItemWidth(220);
			if (ImGui.BeginCombo($"##bind_{role}", current))
			{
				var scene = Core.Scene;
				if (scene != null)
				{
					foreach (var e in scene.Entities.OrderBy(e => e.Name))
					{
						if (ImGui.Selectable(e.Name, current == e.Name))
							BindRole(role, e);
					}
				}
				ImGui.EndCombo();
			}
		}

		private void BindRole(string role, Entity entity)
		{
			var binding = _director.Bindings.FirstOrDefault(b => b.Role == role);
			if (binding == null)
			{
				binding = new RoleBinding { Role = role };
				_director.Bindings.Add(binding);
			}
			binding.Entity = Voltage.Serialization.EntityReference.From(entity);
		}

		#endregion

		#region Events

		private void DrawEvents()
		{
			ImGui.TextColored(Muted, "Fire component methods (AOT-safe) or broadcast signals at a time. Ranged clips fire a begin + end method.");
			ImGui.Spacing();

			var componentIds = TimelineDispatch.RegisteredMethods().Select(m => m.ComponentId).Distinct().OrderBy(x => x).ToArray();

			for (var i = 0; i < _asset.Events.Count; i++)
			{
				var e = _asset.Events[i];
				ImGui.PushID("evt" + i);
				ImGui.Separator();

				var name = e.Name ?? "";
				ImGui.SetNextItemWidth(160);
				if (ImGui.InputText("Name", ref name, 64)) e.Name = name;

				ImGui.SameLine();
				ImGui.SetNextItemWidth(80);
				if (ImGui.InputFloat("Time", ref e.Time, 0, 0, "%.2f")) _asset.InvalidateEventOrder();
				ImGui.SameLine();
				ImGui.SetNextItemWidth(80);
				ImGui.InputFloat("Dur", ref e.Duration, 0, 0, "%.2f");

				// Target role
				ImGui.SetNextItemWidth(140);
				DrawRoleSelect("Role##evt", ref e.TargetRole);

				ImGui.SameLine();
				ImGui.SetNextItemWidth(150);
				var comp = e.TargetComponentId ?? "";
				if (ImGui.BeginCombo("Component", comp))
				{
					foreach (var id in componentIds)
						if (ImGui.Selectable(id, comp == id)) e.TargetComponentId = id;
					ImGui.EndCombo();
				}

				// Method dropdowns (begin/end) filtered by chosen component
				var methods = TimelineDispatch.RegisteredMethods()
					.Where(m => m.ComponentId == e.TargetComponentId).Select(m => m.Method).OrderBy(x => x).ToArray();
				ImGui.SameLine();
				ImGui.SetNextItemWidth(140);
				DrawMethodCombo("Begin", ref e.BeginMethod, methods);
				if (e.Duration > 0f)
				{
					ImGui.SameLine();
					ImGui.SetNextItemWidth(140);
					DrawMethodCombo("End", ref e.EndMethod, methods);
				}

				var bc = e.BroadcastMessage ?? "";
				ImGui.SetNextItemWidth(160);
				if (ImGui.InputText("Broadcast", ref bc, 64)) e.BroadcastMessage = string.IsNullOrWhiteSpace(bc) ? null : bc;

				ImGui.SameLine();
				var skip = (int)e.OnSkip;
				ImGui.SetNextItemWidth(150);
				if (ImGui.Combo("On Skip", ref skip, SkipNames, SkipNames.Length)) e.OnSkip = (SkipBehavior)skip;
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Skip = dropped when the player skips.\nFireImmediately = still runs (give item, spawn boss).");

				ImGui.SameLine();
				if (ImGui.SmallButton("Remove")) { _asset.Events.RemoveAt(i); _asset.InvalidateEventOrder(); ImGui.PopID(); break; }

				ImGui.PopID();
			}

			ImGui.Separator();
			if (ImGui.Button("Add Event"))
			{
				_asset.Events.Add(new TimelineEventClip { Name = "event", Time = _scrub });
				_asset.InvalidateEventOrder();
			}
		}

		private void DrawMethodCombo(string label, ref string value, string[] methods)
		{
			var cur = value ?? "(none)";
			if (ImGui.BeginCombo(label, cur))
			{
				if (ImGui.Selectable("(none)", value == null)) value = null;
				foreach (var m in methods)
					if (ImGui.Selectable(m, value == m)) value = m;
				ImGui.EndCombo();
			}
		}

		private void DrawRoleSelect(string label, ref string role)
		{
			var cur = role ?? "(none)";
			if (ImGui.BeginCombo(label, cur))
			{
				if (ImGui.Selectable("(none)", role == null)) role = null;
				foreach (var r in _asset.Roles)
					if (ImGui.Selectable(r.Name, role == r.Name)) role = r.Name;
				ImGui.EndCombo();
			}
		}

		#endregion

		#region Transform tracks

		private void DrawTransformTracks()
		{
			ImGui.TextColored(Muted, "Keyframe an entity's transform over time. Scrub to preview.");
			ImGui.Spacing();

			var transformTracks = _asset.ParameterTracks.OfType<TimelineTransformTrack>().ToList();
			for (var i = 0; i < transformTracks.Count; i++)
			{
				var track = transformTracks[i];
				ImGui.PushID("tt" + i);
				ImGui.Separator();

				ImGui.SetNextItemWidth(160);
				DrawRoleSelect("Target Role", ref track.TargetRole);
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove Track")) { _asset.ParameterTracks.Remove(track); ImGui.PopID(); break; }

				DrawVector2Keys("Position", track.Position);
				DrawFloatKeys("Rotation (rad)", track.Rotation);
				DrawVector2Keys("Scale", track.Scale);

				ImGui.PopID();
			}

			ImGui.Separator();
			if (ImGui.Button("Add Transform Track"))
				_asset.ParameterTracks.Add(new TimelineTransformTrack { TargetRole = _asset.Roles.FirstOrDefault()?.Name });
		}

		private void DrawVector2Keys(string label, List<Vector2Keyframe> keys)
		{
			if (!ImGui.TreeNode($"{label} ({keys.Count} keys)"))
				return;

			for (var i = 0; i < keys.Count; i++)
			{
				var k = keys[i];
				ImGui.PushID(label + i);
				ImGui.SetNextItemWidth(70);
				ImGui.InputFloat("t", ref k.Time, 0, 0, "%.2f");
				ImGui.SameLine();
				var v = new Num.Vector2(k.Value.X, k.Value.Y);
				ImGui.SetNextItemWidth(140);
				if (ImGui.InputFloat2("val", ref v)) k.Value = new Microsoft.Xna.Framework.Vector2(v.X, v.Y);
				ImGui.SameLine();
				var ease = (int)k.Ease;
				ImGui.SetNextItemWidth(130);
				if (ImGui.Combo("ease", ref ease, EaseNames, EaseNames.Length)) k.Ease = (EaseType)ease;
				ImGui.SameLine();
				if (ImGui.SmallButton("x")) { keys.RemoveAt(i); ImGui.PopID(); break; }
				ImGui.PopID();
			}
			if (ImGui.SmallButton($"+ key##{label}"))
			{
				var last = keys.Count > 0 ? keys[keys.Count - 1].Value : default;
				keys.Add(new Vector2Keyframe { Time = _scrub, Value = last });
				keys.Sort((a, b) => a.Time.CompareTo(b.Time));
			}
			ImGui.TreePop();
		}

		private void DrawFloatKeys(string label, List<FloatKeyframe> keys)
		{
			if (!ImGui.TreeNode($"{label} ({keys.Count} keys)"))
				return;

			for (var i = 0; i < keys.Count; i++)
			{
				var k = keys[i];
				ImGui.PushID(label + i);
				ImGui.SetNextItemWidth(70);
				ImGui.InputFloat("t", ref k.Time, 0, 0, "%.2f");
				ImGui.SameLine();
				ImGui.SetNextItemWidth(90);
				ImGui.InputFloat("val", ref k.Value, 0, 0, "%.3f");
				ImGui.SameLine();
				var ease = (int)k.Ease;
				ImGui.SetNextItemWidth(130);
				if (ImGui.Combo("ease", ref ease, EaseNames, EaseNames.Length)) k.Ease = (EaseType)ease;
				ImGui.SameLine();
				if (ImGui.SmallButton("x")) { keys.RemoveAt(i); ImGui.PopID(); break; }
				ImGui.PopID();
			}
			if (ImGui.SmallButton($"+ key##{label}"))
			{
				var last = keys.Count > 0 ? keys[keys.Count - 1].Value : 0f;
				keys.Add(new FloatKeyframe { Time = _scrub, Value = last });
				keys.Sort((a, b) => a.Time.CompareTo(b.Time));
			}
			ImGui.TreePop();
		}

		#endregion

		#region Spawns

		private void DrawSpawns()
		{
			ImGui.TextColored(Muted, "Spawn a prefab for a time range, bound to a role other tracks can target.");
			ImGui.Spacing();

			for (var i = 0; i < _asset.SpawnClips.Count; i++)
			{
				var s = _asset.SpawnClips[i];
				ImGui.PushID("sp" + i);
				ImGui.Separator();

				var role = s.SpawnRole ?? "";
				ImGui.SetNextItemWidth(140);
				if (ImGui.InputText("Spawn Role", ref role, 64)) s.SpawnRole = role;

				ImGui.SameLine();
				ImGui.SetNextItemWidth(80);
				ImGui.InputFloat("Time", ref s.Time, 0, 0, "%.2f");
				ImGui.SameLine();
				ImGui.SetNextItemWidth(80);
				ImGui.InputFloat("Dur", ref s.Duration, 0, 0, "%.2f");

				ImGui.SameLine();
				ImGui.Checkbox("Keep after", ref s.KeepAfterTimeline);

				ImGui.TextColored(Muted, $"Prefab: {(s.Prefab.IsValid ? s.Prefab.PrefabName : "(assign in inspector — drag a prefab)")}");

				ImGui.SameLine();
				if (ImGui.SmallButton("Remove")) { _asset.SpawnClips.RemoveAt(i); ImGui.PopID(); break; }
				ImGui.PopID();
			}

			ImGui.Separator();
			if (ImGui.Button("Add Spawn"))
				_asset.SpawnClips.Add(new TimelineSpawnClip { SpawnRole = "Spawn" + _asset.SpawnClips.Count, Time = _scrub, Duration = 1f });
		}

		#endregion
	}
}
