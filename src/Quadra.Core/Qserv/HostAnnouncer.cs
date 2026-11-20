using Quadra.Core.Net;

namespace Quadra.Core.Qserv;

/// <summary>
/// Keeps a hosted game on the game list: posts it every <see cref="Interval"/> (the server forgets a game that
/// stays silent for three minutes) and takes it off when disposed. A game that cannot be posted is retried
/// at the next interval; <see cref="Failed"/> says what went wrong.
/// </summary>
public sealed class HostAnnouncer : IAsyncDisposable
{
    private readonly GameHost _host;
    private readonly QservClient _client;
    private readonly int _port;
    private readonly IReadOnlyList<string> _addresses;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public HostAnnouncer(GameHost host, QservClient client, int port, TimeSpan? interval = null, IEnumerable<string>? addresses = null)
    {
        _host = host;
        _client = client;
        _port = port;
        _addresses = [.. addresses ?? []];
        Interval = interval ?? TimeSpan.FromSeconds(60);
        _loop = RunAsync();
    }

    public TimeSpan Interval { get; }

    /// <summary>How many posts the server has accepted.</summary>
    public int Posted { get; private set; }

    /// <summary>The last error, or null if the last attempt went through.</summary>
    public string? Failed { get; private set; }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await PostAsync(_stop.Token);
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PostAsync(CancellationToken ct)
    {
        try
        {
            if (await _client.PostGameAsync(_host.Announcement(_port, _addresses), ct))
            {
                Posted++;
                Failed = null;
            }
            else
                Failed = "The game list server did not accept the game.";
        }
        // A refused connection can still arrive as an HttpRequestException after the stop was requested; only a
        // cancellation caused by the stop must pass through to RunAsync.
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            Failed = e.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
        try
        {
            await _client.DeleteGameAsync(_port);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // The list drops a silent game by itself after the timeout.
        }
    }
}
