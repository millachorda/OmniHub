using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>OBS Studio vía obs-websocket v5 (integrado en OBS 28+; Herramientas → Ajustes de WebSocket).</summary>
public sealed class ObsService
{
    ClientWebSocket? _ws;
    CancellationTokenSource? _cts;
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    public bool Connected => _ws?.State == WebSocketState.Open && _identified;
    bool _identified;
    public bool Recording { get; private set; }
    public bool Streaming { get; private set; }
    public string Status { get; private set; } = "Desconectado";
    public List<string> Scenes { get; private set; } = new();
    public string CurrentScene { get; private set; } = "";
    public event Action? Changed;

    public async Task<bool> ConnectAsync()
    {
        Disconnect();
        var s = Settings.Current;
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        try
        {
            using var to = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            to.CancelAfter(4000);
            await _ws.ConnectAsync(new Uri($"ws://{s.ObsHost}:{s.ObsPort}"), to.Token);
            var hello = await ReceiveAsync(to.Token);
            var d = hello?["d"];
            var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 64 }; // Outputs
            if (d?["authentication"] is JsonNode auth)
            {
                var secret = B64Sha(s.ObsPassword + auth["salt"]!.GetValue<string>());
                identify["authentication"] = B64Sha(secret + auth["challenge"]!.GetValue<string>());
            }
            await SendAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, to.Token);
            var ident = await ReceiveAsync(to.Token);
            if (ident?["op"]?.GetValue<int>() != 2) { Status = "OBS rechazó la conexión (¿contraseña?)"; return false; }
            _identified = true;
            Status = "Conectado a OBS";
            _ = Task.Run(() => ReadLoop(_cts.Token));
            var rec = await Request("GetRecordStatus");
            Recording = rec?["outputActive"]?.GetValue<bool>() == true;
            var str = await Request("GetStreamStatus");
            Streaming = str?["outputActive"]?.GetValue<bool>() == true;
            await LoadScenes();
            Changed?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Status = "No se pudo conectar con OBS: " + (e is OperationCanceledException ? "tiempo agotado" : e.Message);
            return false;
        }
    }

    public async Task LoadScenes()
    {
        var r = await Request("GetSceneList");
        Scenes = r?["scenes"]?.AsArray().Select(x => x?["sceneName"]?.GetValue<string>() ?? "").Where(x => x != "").Reverse().ToList() ?? new();
        CurrentScene = r?["currentProgramSceneName"]?.GetValue<string>() ?? "";
    }

    public Task SetScene(string name) => Request("SetCurrentProgramScene", new JsonObject { ["sceneName"] = name });

    public async Task<JsonNode?> Request(string type, JsonObject? data = null)
    {
        if (!Connected) { Hub.Toast("OBS no está conectado"); return null; }
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data != null) d["requestData"] = data;
        try { await SendAsync(new JsonObject { ["op"] = 6, ["d"] = d }, _cts!.Token); }
        catch { _pending.TryRemove(id, out _); return null; }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(4000));
        _pending.TryRemove(id, out _);
        return done == tcs.Task ? tcs.Task.Result : null;
    }

    async Task ReadLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                var msg = await ReceiveAsync(ct);
                if (msg == null) break;
                var op = msg["op"]?.GetValue<int>();
                var d = msg["d"];
                if (op == 7 && d?["requestId"]?.GetValue<string>() is string id && _pending.TryGetValue(id, out var tcs))
                    tcs.TrySetResult(d["responseData"]);
                else if (op == 5)
                {
                    var ev = d?["eventType"]?.GetValue<string>();
                    var active = d?["eventData"]?["outputActive"]?.GetValue<bool>();
                    if (ev == "RecordStateChanged" && active != null) { Recording = active.Value; Changed?.Invoke(); }
                    if (ev == "StreamStateChanged" && active != null) { Streaming = active.Value; Changed?.Invoke(); }
                }
            }
        }
        catch { }
        _identified = false;
        Status = "Desconectado de OBS";
        Recording = Streaming = false;
        Changed?.Invoke();
    }

    async Task SendAsync(JsonNode n, CancellationToken ct) =>
        await _ws!.SendAsync(Encoding.UTF8.GetBytes(n.ToJsonString()), WebSocketMessageType.Text, true, ct);

    async Task<JsonNode?> ReceiveAsync(CancellationToken ct)
    {
        var buf = new byte[16384];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await _ws!.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray());
    }

    static string B64Sha(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public void Disconnect()
    {
        try { _cts?.Cancel(); _ws?.Abort(); _ws?.Dispose(); } catch { }
        _identified = false;
        _ws = null;
    }
}
