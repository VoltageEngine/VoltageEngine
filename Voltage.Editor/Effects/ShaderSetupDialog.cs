using System;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Effects;

/// <summary>Shared, cancellable shader setup for effect compilation and game builds.</summary>
public static class ShaderSetupDialog
{
	private const string Popup = "Shader Setup";
	private static readonly SemaphoreSlim Gate = new(1, 1);
	private static readonly object Sync = new();
	private static Request _request;
	public static bool IsBusy { get { lock (Sync) return _request != null; } }

	private sealed class Request
	{
		public string Version;
		public string Message = "Shader tools need setup.";
		public string Progress;
		public string Loader = "";
		public string Prefix = "";
		public bool Choose;
		public bool Open;
		public CancellationTokenSource Cancel = new();
		public Task Work;
		public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	public static async Task EnsureReadyAsync(string version, CancellationToken cancel)
	{
		if (OperatingSystem.IsWindows()) return;
		await Gate.WaitAsync(cancel);
		try
		{
			string problem;
			try
			{
				await Task.Run(() => ShaderToolchainSetup.Default.Verify(version, Log, cancel), cancel);
				return;
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) { problem = ex.Message; }
			if (EditorRunMode.NoPrompts)
			{
				Gateway.EditorGatewayDispatcher.SuppressPrompt(Popup, problem);
				throw new InvalidOperationException("Shader setup required. Run effects.setup to install the tools, then retry. " + problem);
			}
			Log(problem);
			var info = ShaderToolchainSetup.Default.EnvironmentInfo();
			var request = new Request { Version = version, Message = ShortMessage(problem),
				Loader = ShaderToolchainSetup.Loader(info) ?? "", Prefix = info.Environment["MGFXC_WINE_PATH"] ?? "" };
			lock (Sync) _request = request;
			using var registration = cancel.Register(() => { request.Cancel.Cancel(); request.Done.TrySetCanceled(cancel); });
			try { await request.Done.Task; }
			finally
			{
				request.Cancel.Cancel();
				if (request.Work != null) await request.Work;
				lock (Sync) { if (_request == request) _request = null; }
				request.Cancel.Dispose();
			}
		}
		finally { Gate.Release(); }
	}

	public static async Task InstallAsync(string version, string loader = null, string prefix = null)
	{
		await Gate.WaitAsync();
		try
		{
			await Task.Run(() =>
			{
				if (loader != null || prefix != null) ShaderToolchainSetup.Default.Verify(version, Log, CancellationToken.None, loader, prefix);
				else ShaderToolchainSetup.Default.Install(version, Log, CancellationToken.None);
			});
		}
		finally { Gate.Release(); }
	}

	private static string ShortMessage(string problem)
	{
		if (problem.Contains("Homebrew", StringComparison.OrdinalIgnoreCase)) return "Install Homebrew or choose an existing Wine installation.";
		if (problem.Contains("development container", StringComparison.OrdinalIgnoreCase)) return "Choose portable Wine or use your development container.";
		if (problem.Contains("download failed verification", StringComparison.OrdinalIgnoreCase)) return "The download could not be verified. Retry later.";
		if (problem.Contains("Installation did not finish", StringComparison.OrdinalIgnoreCase)) return "Installation did not finish. Retry or choose an installation.";
		if (problem.Contains("requires Wine", StringComparison.OrdinalIgnoreCase)) return "Wine is needed to compile shaders.";
		if (problem.Contains("prefix", StringComparison.OrdinalIgnoreCase) || problem.Contains("missing", StringComparison.OrdinalIgnoreCase)) return "Shader tools are missing or incomplete.";
		return "Shader tools need repair. Details are in the Debug Log.";
	}

	private static void Log(string message)
	{
		if (!string.IsNullOrWhiteSpace(message)) Debug.Log(message, "ShaderSetup");
	}

	private static void Start(Request request, bool install)
	{
		request.Progress = install ? "Installing shader tools" : "Checking installation";
		var loader = request.Loader; var prefix = request.Prefix;
		request.Work = Task.Run(() =>
		{
			try
			{
				if (install) ShaderToolchainSetup.Default.Install(request.Version, message => { request.Progress = message; Log(message); }, request.Cancel.Token);
				else ShaderToolchainSetup.Default.Verify(request.Version, Log, request.Cancel.Token, loader, prefix);
				request.Done.TrySetResult(true);
			}
			catch (OperationCanceledException) { request.Done.TrySetCanceled(); }
			catch (Exception ex) { Log(ex.Message); request.Message = ShortMessage(ex.Message); request.Progress = null; }
		});
	}

	public static void Draw()
	{
		Request request;
		lock (Sync) request = _request;
		if (request == null) return;
		if (!request.Open) { ImGui.OpenPopup(Popup); request.Open = true; }
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Num.Vector2(0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(480, 0), ImGuiCond.Appearing);
		var open = true;
		if (Gui.BeginPopupModal(Popup, ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			if (request.Done.Task.IsCompleted) ImGui.CloseCurrentPopup();
			else
			{
				var working = request.Work != null && !request.Work.IsCompleted;
				ImGuiSafe.TextWrappedSafe(working ? request.Progress ?? "Checking shader tools" : request.Message);
				if (!working) ImGuiSafe.TextWrappedSafe("Set up once, then compilation continues automatically.");
				ImGui.Spacing();
				if (request.Choose && !working)
				{
					Gui.InputText("Wine executable", ref request.Loader, 4096);
					Gui.InputText("Wine prefix", ref request.Prefix, 4096);
					if (Gui.Button("Check and Continue")) Start(request, false);
				}
				else if (!working)
				{
					if (Gui.Button("Install and Continue")) Start(request, true);
					ImGui.SameLine();
					if (Gui.Button("Choose Installation")) request.Choose = true;
				}
				ImGui.SameLine();
				if (Gui.Button("Cancel")) { request.Cancel.Cancel(); request.Done.TrySetCanceled(); }
			}
			Gui.EndPopup();
		}
		if (!open) { request.Cancel.Cancel(); request.Done.TrySetCanceled(); }
	}
}
