using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BloxCode.Core;

/// <summary>
/// A small client for Discord's local RPC: the named pipe \\.\pipe\discord-ipc-N that the Discord
/// app opens. Frames are an opcode and a length (both little-endian int32) followed by UTF-8 JSON.
/// Only what Rich Presence needs: handshake, SET_ACTIVITY and answering pings.
/// </summary>
public sealed class DiscordRpc : IDisposable
{
    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;

    private readonly string _appId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readLoop;
    private JsonObject? _activity;

    public DiscordRpc(string appId) => _appId = appId;

    public bool IsConnected => _pipe is { IsConnected: true };

    /// <summary>The Discord user it connected as, once the handshake is answered.</summary>
    public string? UserName { get; private set; }

    /// <summary>Connects to the first Discord pipe that answers; false when Discord isn't running.</summary>
    public async Task<bool> ConnectAsync(CancellationToken ct)
    {
        Disconnect();
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(250, ct);
                await WriteAsync(pipe, OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = _appId }, ct);

                var (op, reply) = await ReadAsync(pipe, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
                if (op == OpFrame && Event(reply) == "READY")
                {
                    UserName = reply!["data"]?["user"]?["username"]?.GetValue<string>();
                    _pipe = pipe;
                    _readLoop = new CancellationTokenSource();
                    _ = ReadLoopAsync(pipe, _readLoop.Token);
                    Logger.Info("DiscordRpc::Connect", $"Connected on discord-ipc-{i}");
                    // Discord restarted while we waited: put the status back.
                    if (_activity is not null)
                        await SendActivityAsync(_activity, ct);
                    return true;
                }

                Logger.Warn("DiscordRpc::Connect", $"discord-ipc-{i} refused the handshake: {reply?.ToJsonString()}");
                pipe.Dispose();
                return false;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or JsonException)
            {
                pipe.Dispose();
            }
        }
        return false;
    }

    /// <summary>Shows <paramref name="activity"/> on the profile (null clears it); remembered for reconnects.</summary>
    public async Task SetActivityAsync(JsonObject? activity, CancellationToken ct)
    {
        _activity = activity?.DeepClone().AsObject();
        if (!IsConnected && !await ConnectAsync(ct))
            return;
        await SendActivityAsync(activity, ct);
    }

    private async Task SendActivityAsync(JsonObject? activity, CancellationToken ct)
    {
        var message = new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity?.DeepClone(),
            },
            ["nonce"] = Guid.NewGuid().ToString(),
        };
        try
        {
            await WriteAsync(_pipe!, OpFrame, message, ct);
        }
        catch (IOException ex)
        {
            Logger.Warn("DiscordRpc::SetActivity", ex.Message);
            Disconnect();
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (op, body) = await ReadAsync(pipe, ct);
                switch (op)
                {
                    case OpPing:
                        await WriteAsync(pipe, OpPong, body ?? new JsonObject(), ct);
                        break;
                    case OpClose:
                        Logger.Info("DiscordRpc::Read", $"Discord closed the connection: {body?.ToJsonString()}");
                        Disconnect();
                        return;
                    case OpFrame when Event(body) == "ERROR":
                        Logger.Warn("DiscordRpc::Read", body!.ToJsonString());
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException or EndOfStreamException)
        {
            if (!ct.IsCancellationRequested)
                Disconnect();
        }
    }

    private async Task WriteAsync(Stream pipe, int op, JsonNode body, CancellationToken ct)
    {
        byte[] json = Encoding.UTF8.GetBytes(body.ToJsonString());
        byte[] frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);

        await _writeLock.WaitAsync(ct);
        try
        {
            await pipe.WriteAsync(frame, ct);
            await pipe.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task<(int Op, JsonObject? Body)> ReadAsync(Stream pipe, CancellationToken ct)
    {
        byte[] header = new byte[8];
        await pipe.ReadExactlyAsync(header, ct);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 1 << 20)
            throw new IOException($"Discord sent a {length}-byte frame");
        byte[] body = new byte[length];
        await pipe.ReadExactlyAsync(body, ct);
        return (op, length == 0 ? null : JsonNode.Parse(body) as JsonObject);
    }

    private static string? Event(JsonObject? body) => body?["evt"] is JsonValue evt && evt.TryGetValue(out string? name) ? name : null;

    private void Disconnect()
    {
        _readLoop?.Cancel();
        _readLoop = null;
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        Disconnect();
        _writeLock.Dispose();
    }
}
