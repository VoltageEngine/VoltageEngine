using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Voltage.Cli;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture]
public class AsepriteGatewayTests
{
	[Test]
	public void All_tools_have_nested_schemas_and_safety_annotations()
	{
		var commands = Call("commands").EnumerateArray().Where(c => c.GetProperty("name").GetString().StartsWith("aseprite.")).ToArray();
		Assert.That(commands.Length, Is.EqualTo(129));
		var pixels = commands.Single(c => c.GetProperty("name").GetString() == "aseprite.put_pixels");
		Assert.That(pixels.GetProperty("inputSchema").GetProperty("properties").GetProperty("pixels").GetProperty("items").GetProperty("properties").TryGetProperty("color", out _), Is.True);
		Assert.That(pixels.GetProperty("unsafe").GetBoolean(), Is.True);
		Assert.That(commands.Single(c => c.GetProperty("name").GetString() == "aseprite.get_sprite_info").GetProperty("readOnly").GetBoolean(), Is.True);
	}

	[Test]
	public async Task Extension_install_and_gateway_round_trip()
	{
		var install = Call("aseprite.install");
		var directory = install.GetProperty("directory").GetString();
		Assert.That(File.Exists(Path.Combine(directory, "LICENSE")), Is.True);
		var script = File.ReadAllText(Path.Combine(directory, "plugin.lua"));
		Assert.That(script, Does.Not.Contain("__VOLTAGE_"));
		using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, "Data", "AsepriteBridge.json")));
		var port = config.RootElement.GetProperty("port").GetInt32();
		var token = config.RootElement.GetProperty("token").GetString();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		using var client = new ClientWebSocket();
		await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/voltage/{token}"), cancellation.Token);
		var buffer = new byte[8192];
		using var connection = Connect();
		var query = Task.Run(() => connection.Call("aseprite.get_sprite_info", JsonSerializer.SerializeToElement(new { voltage_sprite_id = 12 })));
		while (true)
		{
			var message = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
			using var request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, message.Count));
			if (!request.RootElement.TryGetProperty("id", out var id)) continue;
			Assert.That(request.RootElement.GetProperty("method").GetString(), Is.EqualTo("get_sprite_info"));
			Assert.That(request.RootElement.GetProperty("params").GetProperty("voltage_sprite_id").GetInt32(), Is.EqualTo(12));
			var response = JsonSerializer.Serialize(new { id = id.GetString(), result = new { width = 8, height = 8 } });
			await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(response)), WebSocketMessageType.Text, true, cancellation.Token);
			break;
		}
		Assert.That((await query).GetProperty("width").GetInt32(), Is.EqualTo(8));
		Assert.Throws<CliException>(() => Call("aseprite.put_pixels", new { pixels = new[] { new { x = 1 } } }));
		var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
		foreach (var argument in new[] { typeof(McpServer).Assembly.Location, "--config", InfoPath, "mcp" }) start.ArgumentList.Add(argument);
		using var mcp = Process.Start(start);
		try
		{
			await mcp.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18" } }));
			await ReadResponse(mcp, 1, cancellation.Token);
			await mcp.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "aseprite_get_sprite_screenshot", arguments = new { frame = 1 } } }));
			const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aC1sAAAAASUVORK5CYII=";
			while (true)
			{
				var message = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
				using var request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, message.Count));
				if (!request.RootElement.TryGetProperty("id", out var id)) continue;
				Assert.That(request.RootElement.GetProperty("method").GetString(), Is.EqualTo("get_sprite_screenshot"));
				var response = JsonSerializer.Serialize(new { id = id.GetString(), result = new { image = png, width = 1 } });
				await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(response)), WebSocketMessageType.Text, true, cancellation.Token);
				break;
			}
			var blocks = (await ReadResponse(mcp, 2, cancellation.Token)).GetProperty("result").GetProperty("content").EnumerateArray().ToArray();
			Assert.That(blocks[1].GetProperty("type").GetString(), Is.EqualTo("image"));
			Assert.That(blocks[1].GetProperty("data").GetString(), Is.EqualTo(png));
			Assert.That(blocks[0].GetProperty("text").GetString(), Does.Not.Contain(png));
			mcp.StandardInput.Close();
			await mcp.WaitForExitAsync(cancellation.Token);
			Assert.That(mcp.ExitCode, Is.Zero);
		}
		finally { if (!mcp.HasExited) mcp.Kill(entireProcessTree: true); }
		Call("aseprite.disconnect");
	}

	private static async Task<JsonElement> ReadResponse(Process process, int id, CancellationToken cancellation)
	{
		while (true)
		{
			var line = await process.StandardOutput.ReadLineAsync(cancellation) ?? throw new InvalidOperationException("MCP exited before replying");
			using var message = JsonDocument.Parse(line);
			if (message.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id) return message.RootElement.Clone();
		}
	}
}
