using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Voltage.Editor.Aseprite;
using Voltage.Gateway;
using Voltage.Cli;

static void Check(bool success, string description)
{
    if (!success) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}
var resources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Voltage.Editor/Aseprite/Resources"));
using var catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(resources, "tools.json")));
var tools = catalogue.RootElement.EnumerateArray().ToArray();
Check(tools.Length == 123 && tools.Select(t => t.GetProperty("name").GetString()).Distinct().Count() == 123, "123 unique Aseprite schemas");
var drawing = tools.Single(t => t.GetProperty("name").GetString() == "put_pixels").GetProperty("schema");
var valid = JsonSerializer.SerializeToElement(new { pixels = new[] { new { x = 1, y = 2, color = "#AABBCC" } } });
AsepriteSchema.Validate(drawing, valid);
Check(true, "nested pixel arrays validated");
foreach (var invalid in new[] { "{}", "{\"pixels\":[{\"x\":1,\"y\":2}]}", "{\"pixels\":[],\"bogus\":true}" })
{
    try { AsepriteSchema.Validate(drawing, JsonSerializer.Deserialize<JsonElement>(invalid)); throw new Exception("invalid parameters accepted"); }
    catch (GatewayException) { }
}
Check(true, "missing nested fields and unknown parameters rejected");
var record = JsonSerializer.Serialize(new[] { new { name = "aseprite.put_pixels", help = "Draw", inputSchema = drawing, readOnly = false, destructive = false, @unsafe = true } });
var mcp = McpSchema.ToolDefinition(McpTool.Parse(record).Single());
Check(mcp["inputSchema"]["properties"]["pixels"]["items"]["properties"]["color"] != null, "MCP preserves nested schemas");
var token = new string('a', 64);
using var host = new AsepriteConnection(token);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
using (var unauthorized = new ClientWebSocket())
{
    unauthorized.Options.SetRequestHeader("Origin", $"ws://127.0.0.1:{host.Port}");
    try { await unauthorized.ConnectAsync(new Uri($"ws://127.0.0.1:{host.Port}/voltage/wrong"), cancellation.Token); throw new Exception("unauthenticated connection accepted"); }
    catch (WebSocketException) { }
}
Check(!host.IsConnected, "unauthenticated peer rejected");
foreach (var origin in new[] { "http://untrusted.invalid", $"http://127.0.0.1:{host.Port}", "null", "ws://127.0.0.1:1", $"ws://localhost:{host.Port}" })
{
    using var browser = new ClientWebSocket();
    browser.Options.SetRequestHeader("Origin", origin);
    try { await browser.ConnectAsync(new Uri($"ws://127.0.0.1:{host.Port}/voltage/{token}"), cancellation.Token); throw new Exception("browser origin accepted"); }
    catch (WebSocketException) { }
}
Check(true, "browser-origin connection rejected");
using var client = new ClientWebSocket();
client.Options.SetRequestHeader("Origin", $"ws://127.0.0.1:{host.Port}");
await client.ConnectAsync(new Uri($"ws://127.0.0.1:{host.Port}/voltage/{token}"), cancellation.Token);
while (!host.IsConnected) await Task.Delay(10, cancellation.Token);
Check(host.IsConnected, "authenticated Aseprite native-origin connection");
async Task<JsonElement> ReceiveRequest()
{
    var buffer = new byte[16384];
    while (true)
    {
        using var bytes = new MemoryStream();
        WebSocketReceiveResult part;
        do { part = await client.ReceiveAsync(buffer, cancellation.Token); bytes.Write(buffer, 0, part.Count); } while (!part.EndOfMessage);
        var root = JsonSerializer.Deserialize<JsonElement>(bytes.ToArray());
        if (root.TryGetProperty("id", out _)) return root;
    }
}
async Task Reply(JsonElement request, string tail)
{
    var text = "{\"id\":\"" + request.GetProperty("id").GetString() + "\"," + tail + "}";
    await client.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellation.Token);
}
var one = host.Call("one", JsonSerializer.SerializeToElement(new { }));
var two = host.Call("two", JsonSerializer.SerializeToElement(new { }));
var first = await ReceiveRequest();
var second = await ReceiveRequest();
await Reply(second, "\"result\":{\"answer\":2}");
await Reply(first, "\"result\":{\"answer\":1}");
Check((await one).GetProperty("answer").GetInt32() == 1 && (await two).GetProperty("answer").GetInt32() == 2, "out-of-order responses correlated correctly");
var failure = host.Call("failure", JsonSerializer.SerializeToElement(new { }));
await Reply(await ReceiveRequest(), "\"error\":{\"message\":\"test error\"}");
try { await failure; throw new Exception("remote error swallowed"); } catch (InvalidOperationException ex) { Check(ex.Message == "test error", "remote errors propagated"); }
try { await host.Call("timeout", JsonSerializer.SerializeToElement(new { }), TimeSpan.FromMilliseconds(100)); throw new Exception("timeout ignored"); }
catch (TimeoutException) { Check(true, "timeout reports uncertain edit outcome"); }
host.Dispose();
Console.WriteLine("All bridge checks passed.");
