using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Quadra.Core.Net;

/// <summary>
/// Packets over a byte stream: reads framed packets and calls back, and writes framed packets from a queue so
/// a caller never waits on a slow connection. A connection that falls too far behind or sends garbage is closed,
/// as in <c>Net_connection_tcp</c> of the original.
/// </summary>
public sealed class PacketStream : IDisposable
{
    /// <summary>The original closes a connection with this much unsent output.</summary>
    public const int MaxQueuedBytes = 262144;

    private readonly Stream _stream;
    private readonly int _netVersion;
    private readonly Channel<byte[]> _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private int _queued;
    private int _closed;

    public PacketStream(Stream stream, int netVersion = PacketEncoder.CurrentVersion)
    {
        _stream = stream;
        _netVersion = netVersion;
    }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Queues a packet to be written.</summary>
    public void Send(Packet packet)
    {
        if (IsClosed)
            return;
        var framed = PacketFraming.Frame(PacketEncoder.Encode(packet, _netVersion));
        if (Interlocked.Add(ref _queued, framed.Length) > MaxQueuedBytes)
        {
            Close();
            return;
        }
        _queue.Writer.TryWrite(framed);
    }

    /// <summary>Stops sending once what is queued has gone out, then ends the connection.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _queue.Writer.TryComplete();
    }

    /// <summary>Reads and writes until the connection ends. <paramref name="onPacket"/> runs on the reading task.</summary>
    public async Task RunAsync(Action<Packet> onPacket, CancellationToken ct = default)
    {
        var writing = WriteLoopAsync(ct);
        try
        {
            await ReadLoopAsync(onPacket, ct);
        }
        finally
        {
            Close();
            await writing;
        }
    }

    private async Task ReadLoopAsync(Action<Packet> onPacket, CancellationToken ct)
    {
        var buffer = new byte[PacketFraming.MaxPacketSize + 2];
        int count = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer.AsMemory(count), ct);
                if (read == 0)
                    return;   // closed by the other side
                count += read;

                int used = 0;
                while (PacketFraming.TryTake(buffer.AsSpan(used, count - used)) is { } taken)
                {
                    used += taken.Used;
                    onPacket(PacketDecoder.Decode(taken.Payload));
                }
                Buffer.BlockCopy(buffer, used, buffer, 0, count - used);
                count -= used;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException or OperationCanceledException)
        {
            // The connection broke or sent garbage: it is over either way.
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var framed in _queue.Reader.ReadAllAsync(ct))
            {
                await _stream.WriteAsync(framed, ct);
                Interlocked.Add(ref _queued, -framed.Length);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            Close();
        }
        finally
        {
            try
            {
                await _stream.FlushAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
            _stream.Dispose();
        }
    }

    public void Dispose() => Close();
}

/// <summary>Runs a <see cref="GameHost"/> on a TCP port (3456 by default, as in the original).</summary>
public sealed class TcpGameServer : IAsyncDisposable
{
    public const int DefaultPort = 3456;

    private readonly GameHost _host;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private Task? _accepting;
    private Task? _ticking;

    public TcpGameServer(GameHost host, IPAddress? address = null, int port = DefaultPort)
    {
        _host = host;
        // Without an address, take IPv4 and IPv6 alike: a list server on the same machine sees a player as ::1.
        _listener = address is null && Socket.OSSupportsIPv6
            ? new TcpListener(IPAddress.IPv6Any, port) { Server = { DualMode = true } }
            : new TcpListener(address ?? IPAddress.Any, port);
    }

    /// <summary>The port in use; useful when started on port 0.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _accepting = AcceptLoopAsync();
        _ticking = TickLoopAsync();
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
                _host.Tick();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                socket.NoDelay = true;
                _ = ServeAsync(socket);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private sealed class Sink(PacketStream stream) : IPacketSink
    {
        public void Send(Packet packet) => stream.Send(packet);

        public void Close() => stream.Close();
    }

    private async Task ServeAsync(TcpClient socket)
    {
        using var _ = socket;
        using var stream = new PacketStream(socket.GetStream());
        var peer = _host.Connect(new Sink(stream));
        try
        {
            await stream.RunAsync(p => _host.Receive(peer, p), _stop.Token);
        }
        finally
        {
            _host.Disconnect(peer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        foreach (var task in new[] { _accepting, _ticking })
            if (task is not null)
                await task;
        _stop.Dispose();
    }
}

/// <summary>A <see cref="GameClient"/> connected to a game host over TCP.</summary>
public sealed class TcpGameLink : IAsyncDisposable
{
    private readonly TcpClient _socket;
    private readonly PacketStream _stream;
    private readonly Task _running;

    private TcpGameLink(TcpClient socket)
    {
        _socket = socket;
        _stream = new PacketStream(socket.GetStream());
        Client = new GameClient(_stream.Send);
        _running = _stream.RunAsync(Client.Enqueue);
    }

    public GameClient Client { get; }

    /// <summary>The connection has ended (closed by either side, or broken).</summary>
    public Task Closed => _running;

    public static async Task<TcpGameLink> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var socket = new TcpClient { NoDelay = true };
        await socket.ConnectAsync(host, port, ct);
        return new TcpGameLink(socket);
    }

    public async ValueTask DisposeAsync()
    {
        _stream.Close();
        await _running;
        _socket.Dispose();
    }
}
