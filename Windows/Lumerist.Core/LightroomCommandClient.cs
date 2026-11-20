using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Lumerist.Core;

/// <summary>
/// A reconnectable pair of loopback-only connections to the Lightroom plug-in.
/// Commands travel to the plug-in and short acknowledgements return on a
/// separate port, so a completed TCP write is not mistaken for a completed
/// Lightroom action.
/// </summary>
/// <remarks>
/// Lightroom's LrSocket endpoints stop listening after a short timeout and
/// then rebind, so the client retries on its own with a capped backoff until
/// <see cref="Stop"/> is called. Each channel reconnects on its own: a healthy
/// channel is never torn down because the other one is still waiting.
///
/// Protocol 3: every request gets an increasing id, echoed by the plug-in's
/// ack/result; the message callback receives acks, context events and results.
///
/// State and message callbacks run on a background thread, one at a time and
/// in the order the client decided them. None starts after <see cref="Stop"/>
/// returns (one already running may finish), and nothing from a replaced
/// connection is delivered. Write completions also run on background threads.
/// Callbacks must not throw (an exception is ignored so that it cannot stop
/// the reconnection).
/// </remarks>
public sealed class LightroomCommandClient : IDisposable
{
    public abstract record State
    {
        private State() { }

        /// <summary>Both channels are connected.</summary>
        public sealed record Ready() : State;

        /// <summary>A channel is down; the client is retrying.</summary>
        public sealed record Waiting(string Reason) : State;
    }

    /// <summary>A request was sent while the command channel was not connected.</summary>
    public sealed class NotConnectedException() : Exception("The app is not connected to the Lumerist plug-in.");

    private enum ChannelKind
    {
        Command,
        Response,
    }

    private static string Label(ChannelKind channel) => channel == ChannelKind.Command ? "command" : "response";

    private const int ResponseBufferCapacity = 65_536;

    private readonly Lock gate = new();
    private readonly int commandPort;
    private readonly int responsePort;
    private readonly TimeSpan initialRetryDelay;
    private readonly TimeSpan maximumRetryDelay;
    private readonly Action<State> onState;
    private readonly Action<LightroomMessage> onMessage;
    private long nextRequestId = 1;
    private Connection? commandConnection;
    private Connection? responseConnection;
    private bool commandIsReady;
    private bool responseIsReady;
    private byte[] responseBuffer = new byte[ResponseBufferCapacity];
    private int responseCount;
    private bool isStopped = true;
    private bool isDisposed;

    /// <summary>Callbacks waiting for the delivery thread, in decision order (guarded by the lock).</summary>
    private readonly Queue<Delivery> deliveries = new();
    private bool isDelivering;

    /// <summary>Bumped by <see cref="Stop"/>: deliveries decided before it are dropped.</summary>
    private int epoch;

    /// <summary>
    /// Bumped whenever a channel's connection is replaced, so a pending retry
    /// for an older connection does nothing.
    /// </summary>
    private readonly int[] generations = new int[2];

    private readonly bool[] retryScheduled = new bool[2];
    private TimeSpan retryDelay;

    /// <param name="onState">Ready when both channels are up, Waiting with a reason when one goes down.</param>
    /// <param name="onMessage">Acks, context events and results, in the order they arrive.</param>
    /// <param name="port">The plug-in's request port (tests use their own).</param>
    /// <param name="responsePort">The plug-in's response port.</param>
    /// <param name="initialRetryDelay">First retry delay (default 1 s); doubled after each failure.</param>
    /// <param name="maximumRetryDelay">Cap of the retry delay (default 5 s).</param>
    public LightroomCommandClient(
        Action<State> onState,
        Action<LightroomMessage>? onMessage = null,
        int port = Contract.RequestPort,
        int responsePort = Contract.ResponsePort,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maximumRetryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(onState);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThan(responsePort, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(responsePort, IPEndPoint.MaxPort);
        commandPort = port;
        this.responsePort = responsePort;
        this.initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(1);
        this.maximumRetryDelay = maximumRetryDelay ?? TimeSpan.FromSeconds(5);
        // Zero would retry in a tight loop; a negative delay would never retry.
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.initialRetryDelay, TimeSpan.Zero, nameof(initialRetryDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(this.maximumRetryDelay, this.initialRetryDelay, nameof(maximumRetryDelay));
        retryDelay = this.initialRetryDelay;
        this.onState = onState;
        this.onMessage = onMessage ?? (static _ => { });
    }

    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            isStopped = false;
            retryDelay = initialRetryDelay;
        }
        Reconnect();
    }

    /// <summary>Replaces both connections (the manual "Reconnect" button).</summary>
    public void Reconnect()
    {
        Connect(ChannelKind.Command);
        Connect(ChannelKind.Response);
    }

    public void Stop()
    {
        Connection? command, response;
        lock (gate)
        {
            isStopped = true;
            epoch++;
            deliveries.Clear();
            generations[0]++;
            generations[1]++;
            Array.Clear(retryScheduled);
            command = commandConnection;
            response = responseConnection;
            commandConnection = null;
            responseConnection = null;
            commandIsReady = false;
            responseIsReady = false;
            responseCount = 0;
        }
        command?.Cancel();
        response?.Cancel();
    }

    /// <summary>Stops for good: <see cref="Start"/> then throws.</summary>
    public void Dispose()
    {
        lock (gate) isDisposed = true;
        Stop();
    }

    /// <summary>
    /// Sends a protocol-3 request and returns the id it was given (null when the
    /// command channel is not connected; <paramref name="completion"/> then gets
    /// a <see cref="NotConnectedException"/>). The completion reports the TCP
    /// write only (null = written); Lightroom's verdict arrives as an ack (or
    /// result) with the same id through the message callback.
    /// </summary>
    public long? Send(LightroomRequest request, Action<Exception?>? completion = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        completion ??= static _ => { };
        Connection? connection;
        long id;
        var queued = false;
        lock (gate)
        {
            connection = commandIsReady ? commandConnection : null;
            id = nextRequestId;
            if (connection is not null)
            {
                nextRequestId++;
                // Queued under the lock, so lines reach the wire in id order.
                queued = connection.Writes.Writer.TryWrite(new PendingWrite(RequestCodec.EncodeLine(request, id), completion));
            }
        }
        if (connection is null)
        {
            Invoke(completion, new NotConnectedException());
            return null;
        }
        if (!queued) Invoke(completion, Connection.ClosedError());
        return id;
    }

    /// <summary>Sends a request and waits for the TCP write; returns the request id.</summary>
    /// <exception cref="NotConnectedException">The command channel is not connected.</exception>
    public async Task<long> SendAsync(LightroomRequest request)
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Send(request, error =>
        {
            if (error is null) written.TrySetResult();
            else written.TrySetException(error);
        });
        await written.Task.ConfigureAwait(false);
        return id!.Value;
    }

    /// <summary>Replaces one channel only.</summary>
    private void Connect(ChannelKind channel)
    {
        Connection connection;
        Connection? previous;
        lock (gate)
        {
            if (isStopped) return;
            connection = new Connection(channel);
            if (channel == ChannelKind.Command)
            {
                previous = commandConnection;
                commandConnection = connection;
                commandIsReady = false;
            }
            else
            {
                previous = responseConnection;
                responseConnection = connection;
                responseIsReady = false;
                responseCount = 0;
            }
            generations[(int)channel]++;
            retryScheduled[(int)channel] = false;
        }
        previous?.Cancel();
        // Never call back on the caller's thread (Start, Reconnect).
        _ = Task.Run(() => RunAsync(connection));
    }

    private bool IsCurrent(Connection connection) => connection.Channel == ChannelKind.Command
        ? ReferenceEquals(commandConnection, connection)
        : ReferenceEquals(responseConnection, connection);

    private async Task RunAsync(Connection connection)
    {
        var port = connection.Channel == ChannelKind.Command ? commandPort : responsePort;
        try
        {
            await connection.Client.ConnectAsync(IPAddress.Loopback, port, connection.Cancellation.Token).ConfigureAwait(false);
            connection.Client.NoDelay = true;
        }
        catch (Exception error)
        {
            ConnectFailed(connection, error);
            return;
        }
        if (!BecameReady(connection)) return;

        try
        {
            var stream = connection.Client.GetStream();
            if (connection.Channel == ChannelKind.Command)
            {
                _ = WriteRequestsAsync(connection, stream);
                await WatchForCloseAsync(connection, stream).ConfigureAwait(false);
            }
            else
            {
                await ReceiveResponsesAsync(connection, stream).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // A read failed or the connection was replaced: handled below.
        }
        ChannelClosed(connection);
    }

    private void ConnectFailed(Connection connection, Exception error) =>
        ChannelDown(connection, $"{Label(connection.Channel)} channel: {error.Message}");

    /// <summary>Returns false when the connection was replaced meanwhile.</summary>
    private bool BecameReady(Connection connection)
    {
        lock (gate)
        {
            if (!IsCurrent(connection)) return false;
            if (connection.Channel == ChannelKind.Command) commandIsReady = true;
            else responseIsReady = true;
            if (commandIsReady && responseIsReady)
            {
                retryDelay = initialRetryDelay;
                DeliverLocked(new Delivery(epoch, connection, new State.Ready(), null));
            }
            return true;
        }
    }

    /// <summary>Call with the lock held.</summary>
    private void MarkNotReady(ChannelKind channel)
    {
        if (channel == ChannelKind.Command) commandIsReady = false;
        else responseIsReady = false;
    }

    /// <summary>
    /// Lightroom closes a socket when it rebinds; the command channel never
    /// receives data, so reading from it is how a close is noticed.
    /// </summary>
    private static async Task WatchForCloseAsync(Connection connection, NetworkStream stream)
    {
        var buffer = new byte[1_024];
        while (await stream.ReadAsync(buffer, connection.Cancellation.Token).ConfigureAwait(false) > 0)
        {
        }
    }

    /// <summary>Writes queued request lines in order; a failed write closes the connection.</summary>
    private static async Task WriteRequestsAsync(Connection connection, NetworkStream stream)
    {
        var reader = connection.Writes.Reader;
        PendingWrite? current = null;
        try
        {
            while (await reader.WaitToReadAsync(connection.Cancellation.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var write))
                {
                    current = write;
                    await stream.WriteAsync(write.Line, connection.Cancellation.Token).ConfigureAwait(false);
                    current = null;
                    Invoke(write.Completion, null);
                }
            }
        }
        catch (Exception error)
        {
            // Possibly on the thread that called Stop or Reconnect: report later.
            if (current is { } failed) InvokeLater(failed.Completion, error);
            // The read side then notices the close and reports it.
            connection.Cancel();
        }
    }

    private async Task ReceiveResponsesAsync(Connection connection, NetworkStream stream)
    {
        var buffer = new byte[ResponseBufferCapacity];
        int count;
        while ((count = await stream.ReadAsync(buffer, connection.Cancellation.Token).ConfigureAwait(false)) > 0)
        {
            ConsumeResponse(buffer.AsSpan(0, count), connection);
        }
    }

    private void ChannelClosed(Connection connection) =>
        ChannelDown(connection, $"{Label(connection.Channel)} channel was closed by Lightroom");

    /// <summary>
    /// Reports the channel down and schedules its retry in one lock section, so
    /// the retry belongs to this connection's generation: a Reconnect that
    /// happens meanwhile is never torn down by it.
    /// </summary>
    private void ChannelDown(Connection connection, string reason)
    {
        lock (gate)
        {
            if (!IsCurrent(connection)) return;
            MarkNotReady(connection.Channel);
            DeliverLocked(new Delivery(epoch, connection, new State.Waiting(reason), null));
            var channel = (int)connection.Channel;
            if (isStopped || retryScheduled[channel]) return;
            retryScheduled[channel] = true;
            var delay = retryDelay;
            retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maximumRetryDelay.Ticks));
            // Starts Task.Delay (always positive) and returns at its first await.
            _ = RetryAsync(connection.Channel, delay, generations[channel]);
        }
    }

    private async Task RetryAsync(ChannelKind channel, TimeSpan delay, int scheduledGeneration)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        bool stillWanted;
        lock (gate)
        {
            stillWanted = !isStopped && generations[(int)channel] == scheduledGeneration;
        }
        if (stillWanted) Connect(channel);
    }

    private void ConsumeResponse(ReadOnlySpan<byte> data, Connection connection)
    {
        var lines = new List<byte[]>();
        bool oversized;
        lock (gate)
        {
            if (!ReferenceEquals(responseConnection, connection)) return;
            if (responseCount + data.Length > responseBuffer.Length)
                Array.Resize(ref responseBuffer, Math.Max(responseBuffer.Length * 2, responseCount + data.Length));
            // Only the new bytes can hold a newline: earlier lines were taken out.
            var searchFrom = responseCount;
            data.CopyTo(responseBuffer.AsSpan(responseCount));
            responseCount += data.Length;
            var lineStart = 0;
            int newline;
            while ((newline = responseBuffer.AsSpan(searchFrom, responseCount - searchFrom).IndexOf((byte)'\n')) >= 0)
            {
                var end = searchFrom + newline;
                lines.Add(responseBuffer[lineStart..end]);
                lineStart = end + 1;
                searchFrom = lineStart;
            }
            if (lineStart > 0)
            {
                responseBuffer.AsSpan(lineStart, responseCount - lineStart).CopyTo(responseBuffer);
                responseCount -= lineStart;
            }
            // A partial line longer than any valid message is dropped.
            oversized = responseCount > MessageCodec.MaximumLineBytes;
            if (oversized)
            {
                responseBuffer = new byte[ResponseBufferCapacity];
                responseCount = 0;
            }
        }

        // Decoded outside the lock; this connection's reads arrive one at a
        // time, so its lines keep their order.
        var decoded = new List<(State? State, LightroomMessage? Message)>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            try
            {
                if (MessageCodec.DecodeLine(line) is { } message) decoded.Add((null, message));
            }
            catch (Exception error)
            {
                decoded.Add((new State.Waiting($"response channel returned invalid data: {error.Message}"), null));
            }
        }
        if (oversized) decoded.Add((new State.Waiting("response channel sent an oversized message"), null));
        if (decoded.Count == 0) return;
        lock (gate)
        {
            if (!ReferenceEquals(responseConnection, connection)) return;
            foreach (var (state, message) in decoded) DeliverLocked(new Delivery(epoch, connection, state, message));
        }
    }

    /// <summary>Queues a callback; call with the lock held.</summary>
    private void DeliverLocked(Delivery delivery)
    {
        deliveries.Enqueue(delivery);
        if (isDelivering) return;
        isDelivering = true;
        ThreadPool.QueueUserWorkItem(static client => client.DrainDeliveries(), this, preferLocal: false);
    }

    /// <summary>Runs queued callbacks one at a time, dropping those that went stale.</summary>
    private void DrainDeliveries()
    {
        while (true)
        {
            Delivery next;
            lock (gate)
            {
                if (!deliveries.TryDequeue(out next))
                {
                    isDelivering = false;
                    return;
                }
                if (next.Epoch != epoch || !IsCurrent(next.Source)) continue;
            }
            try
            {
                if (next.State is { } state) onState(state);
                else if (next.Message is { } message) onMessage(message);
            }
            catch (Exception)
            {
                // See the class remarks.
            }
        }
    }

    private static void InvokeLater(Action<Exception?> completion, Exception? error) =>
        ThreadPool.QueueUserWorkItem(static item => Invoke(item.Item1, item.Item2), (completion, error), preferLocal: false);

    private static void Invoke(Action<Exception?> completion, Exception? error)
    {
        try
        {
            completion(error);
        }
        catch (Exception)
        {
            // See the class remarks.
        }
    }

    private readonly record struct PendingWrite(byte[] Line, Action<Exception?> Completion);

    /// <summary>A state or a message decided for <paramref name="Source"/> during <paramref name="Epoch"/>.</summary>
    private readonly record struct Delivery(int Epoch, Connection Source, State? State, LightroomMessage? Message);

    /// <summary>One connection attempt on one channel; replaced, never reused.</summary>
    private sealed class Connection(ChannelKind channel)
    {
        private int cancelled;

        public ChannelKind Channel { get; } = channel;

        public TcpClient Client { get; } = new(AddressFamily.InterNetwork);

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Request lines waiting for the socket (command channel).</summary>
        public Channel<PendingWrite> Writes { get; } =
            // Two readers: the write loop and Cancel, which fails what is left.
            System.Threading.Channels.Channel.CreateUnbounded<PendingWrite>(new UnboundedChannelOptions { SingleReader = false });

        public static Exception ClosedError() =>
            new OperationCanceledException("The connection to the plug-in closed before the request was sent.");

        /// <summary>
        /// Closes the socket and fails the writes still queued, on a pool thread
        /// (never on the caller's: Stop, Reconnect). Safe to call more than once.
        /// </summary>
        public void Cancel()
        {
            if (Interlocked.Exchange(ref cancelled, 1) == 1) return;
            Cancellation.Cancel();
            Writes.Writer.TryComplete();
            while (Writes.Reader.TryRead(out var write)) InvokeLater(write.Completion, ClosedError());
            Client.Dispose();
        }
    }
}
