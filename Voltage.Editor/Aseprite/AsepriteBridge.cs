using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Voltage.Editor.Persistence;
using Voltage.Gateway;

namespace Voltage.Editor.Aseprite;

/// <summary>Owns the editor bridge and installs its MIT-licensed Lua extension.</summary>
public sealed class AsepriteBridge : IDisposable
{
	private AsepriteConnection _connection;
	private string _token;
	private int _port;
	private readonly ConcurrentQueue<(Func<object> action, TaskCompletionSource<object> completion)> _actions = new();
	private readonly Action<string, object> _emit;
	public static string Resources => Path.Combine(AppContext.BaseDirectory, "Aseprite");
	public static string ExtensionDirectory => Environment.GetEnvironmentVariable("VOLTAGE_ASEPRITE_EXTENSION") ?? Path.Combine(
		OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) :
		OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support") :
		Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
		"Aseprite", "extensions", "voltage-aseprite");
	private static string SettingsPath => Path.Combine(EditorStorage.Root, "AsepriteBridge.json");
	public bool IsConnected => _connection?.IsConnected == true;
	public bool IsListening => _connection != null;
	public int Port => _connection?.Port ?? 0;

	public AsepriteBridge(Action<string, object> emit) { _emit = emit; }

	public object Status() => new { connected = IsConnected, listening = IsListening, port = Port, installed = File.Exists(Path.Combine(ExtensionDirectory, "plugin.lua")), extension = ExtensionDirectory, tools = 123 };

	public void Start()
	{
		if (_connection != null) return;
		if (File.Exists(SettingsPath))
		{
			var config = JsonNode.Parse(File.ReadAllText(SettingsPath));
			_token = config["token"].GetValue<string>();
			_port = config["port"].GetValue<int>();
		}
		else
		{
			_token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
			_port = 0;
		}
		var connection = new AsepriteConnection(_token, _port);
		try
		{
			_port = connection.Port;
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
			File.WriteAllText(SettingsPath, new JsonObject { ["token"] = _token, ["port"] = _port }.ToJsonString());
			connection.ConnectionChanged += connected => _emit?.Invoke(connected ? "aseprite.connected" : "aseprite.disconnected", new { port = _port });
			_connection = connection;
		}
		catch { connection.Dispose(); throw; }
	}

	public object Install()
	{
		Start();
		Directory.CreateDirectory(ExtensionDirectory);
		var plugin = File.ReadAllText(Path.Combine(Resources, "plugin.lua"))
			.Replace("__VOLTAGE_PORT__", _port.ToString(System.Globalization.CultureInfo.InvariantCulture))
			.Replace("__VOLTAGE_TOKEN__", _token);
		File.WriteAllText(Path.Combine(ExtensionDirectory, "plugin.lua"), plugin);
		File.Copy(Path.Combine(Resources, "LICENSE"), Path.Combine(ExtensionDirectory, "LICENSE"), true);
		File.Copy(Path.Combine(Resources, "package.json"), Path.Combine(ExtensionDirectory, "package.json"), true);
		return new { installed = true, directory = ExtensionDirectory, port = _port, instruction = "Restart Aseprite or enable Voltage Bridge in its Extensions preferences. Allow its initial WebSocket request." };
	}

	public async Task<object> Call(string method, JsonElement parameters)
	{
		if (_connection == null) throw new GatewayException("Voltage Aseprite bridge is stopped; use aseprite.connect or aseprite.install");
		try { return await _connection.Call(method, parameters); }
		catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Net.WebSockets.WebSocketException or IOException)
		{ throw new GatewayException(ex.Message); }
	}

	public Task<object> OnMainThread(Func<object> action)
	{
		var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_actions)
		{
			if (_connection == null) completion.TrySetException(new GatewayException("Aseprite bridge stopped"));
			else _actions.Enqueue((action, completion));
		}
		return completion.Task;
	}

	public void Tick()
	{
		while (_actions.TryDequeue(out var item))
		{
			try { item.completion.TrySetResult(item.action()); }
			catch (Exception ex) { item.completion.TrySetException(ex); }
		}
	}

	public void Stop()
	{
		lock (_actions)
		{
			_connection?.Dispose(); _connection = null;
			while (_actions.TryDequeue(out var item)) item.completion.TrySetException(new GatewayException("Aseprite bridge stopped"));
		}
	}
	public void Dispose() => Stop();
}
