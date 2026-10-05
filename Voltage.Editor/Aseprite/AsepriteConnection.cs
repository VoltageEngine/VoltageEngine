using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Voltage.Editor.Aseprite;

/// <summary>Authenticated loopback WebSocket host for the bundled Aseprite extension.</summary>
public sealed class AsepriteConnection : IDisposable
{
	private const int MaxMessageBytes = 3 * 1024 * 1024;
	private readonly string _token;
	private readonly CancellationTokenSource _stop = new();
	private readonly SemaphoreSlim _send = new(1, 1);
	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly TcpListener _listener;
	private WebSocket _client;
	private int _disposed;
	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
	public bool IsConnected => _client?.State == WebSocketState.Open;
	public event Action<bool> ConnectionChanged;

	public AsepriteConnection(string token, int port = 0)
	{
		if (string.IsNullOrEmpty(token) || token.Length < 32)
			throw new ArgumentException("Aseprite bridge token must have at least 32 characters", nameof(token));
		_token = token;
		_listener = new TcpListener(IPAddress.Loopback, port);
		_listener.Start();
		_ = AcceptLoop();
	}

	private async Task AcceptLoop()
	{
		try
		{
			while (!_stop.IsCancellationRequested)
			{
				var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
				_ = Serve(tcp);
			}
		}
		catch (Exception) when (_stop.IsCancellationRequested) { }
	}

	private async Task Serve(TcpClient tcp)
	{
		WebSocket socket = null;
		try
		{
			using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
			handshake.CancelAfter(TimeSpan.FromSeconds(5));
			var stream = tcp.GetStream();
			var header = new MemoryStream();
			var single = new byte[1];
			while (header.Length < 8192)
			{
				if (await stream.ReadAsync(single.AsMemory(), handshake.Token) == 0) return;
				header.WriteByte(single[0]);
				var bytes = header.GetBuffer();
				var n = (int)header.Length;
				if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) break;
			}
			var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
			var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var line in lines)
			{
				var colon = line.IndexOf(':');
				if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
			}
			var nativeOrigin = $"ws://127.0.0.1:{Port}";
			if (lines[0] != $"GET /voltage/{_token} HTTP/1.1" ||
			    headers.TryGetValue("Origin", out var origin) && origin != nativeOrigin ||
			    !headers.TryGetValue("Upgrade", out var upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) ||
			    !headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13" ||
			    !headers.TryGetValue("Sec-WebSocket-Key", out var key) || Convert.FromBase64String(key).Length != 16)
			{
				await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"), handshake.Token);
				return;
			}
			var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
			await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), handshake.Token);
			socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(10));
			await _send.WaitAsync(_stop.Token);
			try
			{
				if (_client != null)
				{
					socket.Abort();
					return;
				}
				_client = socket;
			}
			finally { _send.Release(); }
			ConnectionChanged?.Invoke(true);
			var heartbeat = Heartbeat(socket);
			try { await Receive(socket); }
			finally { socket.Abort(); await heartbeat; }
		}
		catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException or FormatException or JsonException or InvalidOperationException) { }
		finally
		{
			if (ReferenceEquals(_client, socket) && socket != null)
			{
				_client = null;
				FailPending("Aseprite disconnected; an in-flight edit may have completed. Inspect state before retrying.");
				ConnectionChanged?.Invoke(false);
			}
			socket?.Dispose();
			tcp.Dispose();
		}
	}

	private async Task Receive(WebSocket socket)
	{
		var buffer = new byte[16384];
		while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
		{
			using var message = new MemoryStream();
			WebSocketReceiveResult fragment;
			do
			{
				fragment = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
				if (fragment.MessageType != WebSocketMessageType.Text || message.Length + fragment.Count > MaxMessageBytes) return;
				message.Write(buffer, 0, fragment.Count);
			} while (!fragment.EndOfMessage);
			using var doc = JsonDocument.Parse(message.ToArray());
			var root = doc.RootElement;
			if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && _pending.TryRemove(id.GetString(), out var completion))
			{
				if (root.TryGetProperty("error", out var error))
					completion.TrySetException(new InvalidOperationException(error.TryGetProperty("message", out var text) ? text.GetString() : error.GetRawText()));
				else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
				else completion.TrySetException(new InvalidOperationException("Aseprite returned no result"));
			}
		}
	}

	private async Task Heartbeat(WebSocket socket)
	{
		try
		{
			while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
			{
				await Send(socket, "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"params\":{}}", _stop.Token);
				await Task.Delay(5000, _stop.Token);
			}
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
	}

	private async Task Send(WebSocket socket, string message, CancellationToken cancellation)
	{
		await _send.WaitAsync(cancellation);
		try { await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, cancellation); }
		finally { _send.Release(); }
	}

	public async Task<JsonElement> Call(string method, JsonElement parameters, TimeSpan? timeout = null)
	{
		var socket = _client;
		if (socket?.State != WebSocketState.Open) throw new InvalidOperationException("Aseprite is not connected. Install the Voltage extension and open Aseprite.");
		if (_pending.Count >= 32) throw new InvalidOperationException("Too many pending Aseprite requests");
		var id = Guid.NewGuid().ToString("N");
		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
		deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
		try
		{
			var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
			if (Encoding.UTF8.GetByteCount(json) > MaxMessageBytes) throw new InvalidOperationException("Aseprite request exceeds 3 MiB; split the operation");
			await Send(socket, json, deadline.Token);
			return await completion.Task.WaitAsync(deadline.Token);
		}
		catch (OperationCanceledException) { throw new TimeoutException("Aseprite request interrupted or timed out; inspect state before retrying an edit."); }
		finally { _pending.TryRemove(id, out _); }
	}

	private void FailPending(string message)
	{
		foreach (var pair in _pending)
			if (_pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(new InvalidOperationException(message));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_stop.Cancel();
		_listener.Stop();
		_client?.Abort();
		FailPending("Voltage Aseprite bridge stopped");
	}
}
