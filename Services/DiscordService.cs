using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>
/// Discord Rich Presence por IPC local (no necesita token: solo un Application ID de
/// https://discord.com/developers/applications). Silenciar micro se hace con el atajo de Discord.
/// </summary>
public sealed class DiscordService : IDisposable
{
    NamedPipeClientStream? _pipe;
    readonly long _start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Status { get; private set; } = "Desactivado";
    public bool Connected => _pipe?.IsConnected == true;

    public bool Connect()
    {
        Close();
        var id = Settings.Current.DiscordClientId.Trim();
        if (id.Length == 0) { Status = "Falta el Application ID"; return false; }
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(300);
                _pipe = p;
                Write(0, new JsonObject { ["v"] = 1, ["client_id"] = id });
                var resp = Read();
                if (resp == null || resp.ToJsonString().Contains("\"ERROR\"")) { Status = "Discord rechazó el Application ID"; Close(); return false; }
                Status = "Conectado a Discord";
                return true;
            }
            catch { }
        }
        Status = "Discord no está abierto";
        return false;
    }

    public void SetActivity(string details, string state)
    {
        if (!Connected) return;
        try
        {
            var activity = new JsonObject
            {
                ["details"] = details,
                ["state"] = state,
                ["timestamps"] = new JsonObject { ["start"] = _start },
            };
            Write(1, new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity },
                ["nonce"] = Guid.NewGuid().ToString(),
            });
            Read();
        }
        catch { Status = "Conexión con Discord perdida"; Close(); }
    }

    void Write(int op, JsonNode payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var buf = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(buf, 0);
        BitConverter.GetBytes(body.Length).CopyTo(buf, 4);
        body.CopyTo(buf, 8);
        _pipe!.Write(buf);
        _pipe.Flush();
    }

    JsonNode? Read()
    {
        var head = new byte[8];
        if (!ReadExact(head)) return null;
        var len = BitConverter.ToInt32(head, 4);
        if (len <= 0 || len > 1 << 20) return null;
        var body = new byte[len];
        if (!ReadExact(body)) return null;
        return JsonNode.Parse(body);
    }

    bool ReadExact(byte[] b)
    {
        int off = 0;
        var t = Task.Run(() =>
        {
            while (off < b.Length) { int n = _pipe!.Read(b, off, b.Length - off); if (n <= 0) return false; off += n; }
            return true;
        });
        return t.Wait(2000) && t.Result;
    }

    void Close() { try { _pipe?.Dispose(); } catch { } _pipe = null; }
    public void Dispose() => Close();
}
