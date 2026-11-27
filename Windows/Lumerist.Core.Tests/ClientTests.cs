using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// Loopback tests for the command/acknowledgement client (ClientTests.swift).
/// They use ephemeral ports only, never Lightroom's 50550/50551.
/// </summary>
public class ClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DeliversRequestsWithIncreasingIds()
    {
        using var receiver = LoopbackServer.LineReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        using var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: receiver.Port, responsePort: responseSender.Port);
        client.Start();
        await WaitUntilReady(states.Reader);

        var first = LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.1);
        var firstId = await client.SendAsync(first).WaitAsync(Timeout);
        var received = RequestCodec.DecodeLine(await receiver.NextData());
        Assert.Equal(TestToken.Value, received.Token);
        Assert.Equal(first, received.Request);
        Assert.Equal(firstId, received.Id);

        var second = LightroomRequest.Command(LightroomCommandName.CreateMask, "aiSelection/sky");
        var secondId = await client.SendAsync(second).WaitAsync(Timeout);
        Assert.Equal(firstId + 1, secondId);
        var decoded = RequestCodec.DecodeLine(await receiver.NextData());
        Assert.Equal(second, decoded.Request);
        Assert.Equal(secondId, decoded.Id);
    }

    [Fact]
    public async Task AdjustmentsReachThePlugInAsProtocolThree()
    {
        using var receiver = LoopbackServer.ChunkReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        using var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: receiver.Port, responsePort: responseSender.Port);
        client.Start();
        await WaitUntilReady(states.Reader);

        var request = LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.1);
        var id = await client.SendAsync(request).WaitAsync(Timeout);
        var decoded = RequestCodec.DecodeLine(await receiver.NextData());
        Assert.Equal(request, decoded.Request);
        Assert.Equal(id, decoded.Id);
    }

    [Fact]
    public async Task ReceivesAcksEventsAndLargeResults()
    {
        using var commandReceiver = LoopbackServer.ChunkReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        var messages = Channel.CreateUnbounded<LightroomMessage>();
        using var client = new LightroomCommandClient(
            TestToken.Value,
            state => states.Writer.TryWrite(state),
            message => messages.Writer.TryWrite(message),
            port: commandReceiver.Port, responsePort: responseSender.Port);
        client.Start();

        var returnConnection = await responseSender.NextConnection();
        await WaitUntilReady(states.Reader);
        var id = await client.SendAsync(LightroomRequest.Ping).WaitAsync(Timeout);
        _ = await commandReceiver.NextData();

        // A preset list of more than 200 KB, several times the read buffer, split across many reads.
        var items = Enumerable.Range(0, 3_000)
            .Select(index => new PresetItem($"UUID-{index}", $"Preset number {index} with a long name", $"Folder {index % 7}"))
            .ToList();
        var ack = new LightroomAck(id, true, "Lumerist plug-in ready (protocol 4)");
        var payload = MessageCodec.EncodeLine(new LightroomMessage.Context(new LightroomContext("develop", "masking")))
            .Concat(MessageCodec.EncodeLine(new LightroomMessage.Ack(ack)))
            .Concat(MessageCodec.EncodeLine(new LightroomMessage.Result(new LightroomResult.Presets(99, items, false))))
            .ToArray();
        Assert.True(payload.Length > 200_000);
        await returnConnection.GetStream().WriteAsync(payload).AsTask().WaitAsync(Timeout);

        var received = new List<LightroomMessage>();
        while (received.Count < 3) received.Add(await messages.Reader.ReadAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(new LightroomMessage.Context(new LightroomContext("develop", "masking")), received[0]);
        Assert.Equal(new LightroomMessage.Ack(ack), received[1]);
        Assert.Equal(new LightroomMessage.Result(new LightroomResult.Presets(99, items, false)), received[2]);
    }

    [Fact]
    public async Task SkipsBadLinesAndKeepsReading()
    {
        using var commandReceiver = LoopbackServer.LineReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        var messages = Channel.CreateUnbounded<LightroomMessage>();
        using var client = new LightroomCommandClient(
            TestToken.Value,
            state => states.Writer.TryWrite(state),
            message => messages.Writer.TryWrite(message),
            port: commandReceiver.Port, responsePort: responseSender.Port);
        client.Start();
        var returnConnection = await responseSender.NextConnection();
        await WaitUntilReady(states.Reader);

        var ack = new LightroomMessage.Ack(new LightroomAck(7, true, "Café prêt"));
        var payload = Encoding.UTF8.GetBytes("not json\n\n{\"kind\":\"telemetry\",\"version\":4}\n")
            .Concat(MessageCodec.EncodeLine(ack)).ToArray();
        // Sent in pieces that split a line, the two bytes of "é" and the final newline.
        var insideE = Array.IndexOf(payload, (byte)0xC3) + 1;
        returnConnection.NoDelay = true;
        var cuts = new[] { 0, 4, insideE, payload.Length - 1, payload.Length };
        for (var piece = 0; piece + 1 < cuts.Length; piece++)
        {
            await returnConnection.GetStream().WriteAsync(payload.AsMemory(cuts[piece], cuts[piece + 1] - cuts[piece])).AsTask().WaitAsync(Timeout);
            await Task.Delay(30);
        }

        Assert.Equal(ack, await messages.Reader.ReadAsync().AsTask().WaitAsync(Timeout));
        // Only the bad line is reported: the empty line and the unknown kind are skipped silently.
        await Task.Delay(100);
        var waiting = new List<string>();
        while (states.Reader.TryRead(out var state))
        {
            if (state is LightroomCommandClient.State.Waiting { Reason: var reason }) waiting.Add(reason);
        }
        Assert.Single(waiting);
        Assert.StartsWith("response channel returned invalid data", waiting[0]);
    }

    [Fact]
    public async Task RefusesToSendBeforeTheCommandChannelIsReady()
    {
        var unusedPort = ReserveFreePort();
        using var client = new LightroomCommandClient(TestToken.Value, _ => { }, port: unusedPort, responsePort: unusedPort);
        client.Start();

        Exception? failure = null;
        Assert.Null(client.Send(LightroomRequest.Ping, error => failure = error));
        Assert.IsType<LightroomCommandClient.NotConnectedException>(failure);
        await Assert.ThrowsAsync<LightroomCommandClient.NotConnectedException>(() => client.SendAsync(LightroomRequest.Ping));
    }

    [Fact]
    public async Task ReconnectsOnItsOwnWhenLightroomStartsListeningLater()
    {
        using var responseSender = LoopbackServer.ResponseSender();
        // Nothing listens on this port yet, like Lightroom while it rebinds.
        var commandPort = ReserveFreePort();

        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        using var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: commandPort, responsePort: responseSender.Port,
            initialRetryDelay: TimeSpan.FromSeconds(0.2), maximumRetryDelay: TimeSpan.FromSeconds(0.4));
        client.Start();
        // Let the first attempt fail.
        var waiting = await NextWaiting(states.Reader);
        Assert.StartsWith("command channel:", waiting.Reason);

        using var receiver = LoopbackServer.ChunkReceiver(commandPort);
        Assert.Equal(commandPort, receiver.Port);

        await WaitUntilReady(states.Reader);
        await client.SendAsync(LightroomRequest.Ping).WaitAsync(Timeout);
        Assert.Equal(LightroomRequest.Ping, RequestCodec.DecodeLine(await receiver.NextData()).Request);
    }

    [Fact]
    public async Task ReconnectsAfterLightroomClosesTheResponseChannel()
    {
        using var commandReceiver = LoopbackServer.LineReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        using var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: commandReceiver.Port, responsePort: responseSender.Port,
            initialRetryDelay: TimeSpan.FromSeconds(0.1), maximumRetryDelay: TimeSpan.FromSeconds(0.1));
        client.Start();
        var first = await responseSender.NextConnection();
        await WaitUntilReady(states.Reader);

        first.Dispose();
        Assert.Equal("response channel was closed by Lightroom", (await NextWaiting(states.Reader)).Reason);
        _ = await responseSender.NextConnection();
        await WaitUntilReady(states.Reader);
        // Long enough for a wrongly replaced command channel to show up.
        await Task.Delay(500);
        Assert.Equal(1, commandReceiver.Connections);
    }

    [Fact]
    public async Task AReconnectDuringARetryKeepsTheNewConnection()
    {
        using var commandReceiver = LoopbackServer.LineReceiver();
        var responsePort = ReserveFreePort();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        using var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: commandReceiver.Port, responsePort: responsePort,
            initialRetryDelay: TimeSpan.FromSeconds(1.5), maximumRetryDelay: TimeSpan.FromSeconds(1.5));
        client.Start();
        Assert.StartsWith("response channel:", (await NextWaiting(states.Reader)).Reason);

        // The plug-in starts listening and the user presses Reconnect before the retry is due.
        using var responseListener = LoopbackServer.Counter(responsePort);
        client.Reconnect();
        await WaitUntilReady(states.Reader);

        // The retry for the failed attempt comes due and must leave the new connection alone.
        await Task.Delay(2_500);
        Assert.Equal(1, responseListener.Connections);
        Assert.False(states.Reader.TryRead(out var later), $"unexpected {later}");
    }

    [Fact]
    public async Task RetryingOneChannelKeepsTheHealthyChannelOpen()
    {
        using var commandListener = LoopbackServer.Counter();
        // The response endpoint stays closed, so that channel keeps retrying.
        var responsePort = ReserveFreePort();
        var attempts = 0;
        using var client = new LightroomCommandClient(TestToken.Value, state => { if (state is LightroomCommandClient.State.Waiting) Interlocked.Increment(ref attempts); },
            port: commandListener.Port, responsePort: responsePort,
            initialRetryDelay: TimeSpan.FromSeconds(0.1), maximumRetryDelay: TimeSpan.FromSeconds(0.1));
        client.Start();
        // A refused loopback connection can take a second or two to fail on Windows.
        var deadline = DateTime.UtcNow + Timeout;
        while (Volatile.Read(ref attempts) < 3 && DateTime.UtcNow < deadline) await Task.Delay(50);

        Assert.True(Volatile.Read(ref attempts) >= 3, $"only {attempts} response attempts");
        Assert.Equal(1, commandListener.Connections);
    }

    [Fact]
    public async Task StopEndsTheRetries()
    {
        var unusedPort = ReserveFreePort();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: unusedPort, responsePort: unusedPort,
            initialRetryDelay: TimeSpan.FromSeconds(0.1), maximumRetryDelay: TimeSpan.FromSeconds(0.1));
        client.Start();
        await Task.Delay(400);
        client.Stop();

        // Drain what was emitted before stop, then make sure nothing follows.
        await Task.Delay(100);
        using var receiver = LoopbackServer.ChunkReceiver(unusedPort);
        await Task.Delay(600);
        states.Writer.Complete();

        var sawReady = false;
        await foreach (var state in states.Reader.ReadAllAsync()) sawReady |= state is LightroomCommandClient.State.Ready;
        Assert.False(sawReady);
        Assert.Equal(0, receiver.Connections);
    }

    [Fact]
    public async Task StopSettlesEveryQueuedWriteExactlyOnce()
    {
        using var receiver = LoopbackServer.LineReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        var client = new LightroomCommandClient(TestToken.Value, state => states.Writer.TryWrite(state),
            port: receiver.Port, responsePort: responseSender.Port);
        client.Start();
        await WaitUntilReady(states.Reader);

        const int count = 2_000;
        var calls = new int[count];
        var failures = new Exception?[count];
        var settled = 0;
        var allSettled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopperThread = Environment.CurrentManagedThreadId;
        var calledOnStopper = false;
        for (var index = 0; index < count; index++)
        {
            var slot = index;
            Assert.NotNull(client.Send(LightroomRequest.Ping, error =>
            {
                Interlocked.Increment(ref calls[slot]);
                failures[slot] = error;
                if (Environment.CurrentManagedThreadId == stopperThread) calledOnStopper = true;
                if (Interlocked.Increment(ref settled) == count) allSettled.TrySetResult();
            }));
        }
        client.Stop();
        await allSettled.Task.WaitAsync(Timeout);
        await Task.Delay(100); // A second call for any write would land here.

        Assert.All(calls, calledTimes => Assert.Equal(1, calledTimes));
        // A write either reached the socket (null) or failed; failures are cancellations or socket errors.
        Assert.All(failures, error => Assert.True(error is null or OperationCanceledException or IOException or ObjectDisposedException, $"{error}"));
        Assert.False(calledOnStopper, "a completion ran on the thread that called Stop");
        Assert.Null(client.Send(LightroomRequest.Ping));
    }

    [Fact]
    public async Task NoCallbackStartsAfterStopReturns()
    {
        using var commandReceiver = LoopbackServer.LineReceiver();
        using var responseSender = LoopbackServer.ResponseSender();
        var states = Channel.CreateUnbounded<LightroomCommandClient.State>();
        var delivered = 0;
        var firstMessage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new LightroomCommandClient(
            TestToken.Value,
            state => states.Writer.TryWrite(state),
            _ =>
            {
                Interlocked.Increment(ref delivered);
                firstMessage.TrySetResult();
                Thread.Sleep(1); // A slow consumer, so messages queue up.
            },
            port: commandReceiver.Port, responsePort: responseSender.Port);
        client.Start();
        var returnConnection = await responseSender.NextConnection();
        await WaitUntilReady(states.Reader);

        var payload = Enumerable.Range(1, 2_000)
            .SelectMany(id => MessageCodec.EncodeLine(new LightroomMessage.Ack(new LightroomAck(id, true)))).ToArray();
        await returnConnection.GetStream().WriteAsync(payload).AsTask().WaitAsync(Timeout);
        await firstMessage.Task.WaitAsync(Timeout);
        client.Stop();
        var atStop = Volatile.Read(ref delivered);
        await Task.Delay(300);

        // At most the callback already running when Stop was called may finish.
        Assert.InRange(Volatile.Read(ref delivered), atStop, atStop + 1);
        Assert.True(atStop < 2_000, "every message was delivered before Stop: the test proves nothing");
    }

    [Fact]
    public void DisposeIsFinal()
    {
        var unusedPort = ReserveFreePort();
        var client = new LightroomCommandClient(TestToken.Value, _ => { }, port: unusedPort, responsePort: unusedPort);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(client.Start);
        client.Dispose(); // Twice is fine.
    }

    [Fact]
    public void RetryDelaysMustBePositiveAndOrdered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightroomCommandClient(TestToken.Value, _ => { }, initialRetryDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightroomCommandClient(TestToken.Value, _ => { }, initialRetryDelay: TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightroomCommandClient(TestToken.Value, _ => { },
            initialRetryDelay: TimeSpan.FromSeconds(2), maximumRetryDelay: TimeSpan.FromSeconds(1)));
    }

    // Helpers -----------------------------------------------------------------

    private static async Task WaitUntilReady(ChannelReader<LightroomCommandClient.State> states)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (await states.ReadAsync(timeout.Token) is not LightroomCommandClient.State.Ready)
        {
        }
    }

    private static async Task<LightroomCommandClient.State.Waiting> NextWaiting(ChannelReader<LightroomCommandClient.State> states)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (true)
        {
            if (await states.ReadAsync(timeout.Token) is LightroomCommandClient.State.Waiting waiting) return waiting;
        }
    }

    /// <summary>Finds a port that is free right now by listening briefly on an ephemeral one.</summary>
    private static int ReserveFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}

/// <summary>A loopback listener that hands every accepted connection to a handler.</summary>
internal sealed class LoopbackServer : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<TcpClient> accepted = [];
    private readonly Channel<byte[]> data = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<TcpClient> connections = Channel.CreateUnbounded<TcpClient>();
    private int connectionCount;

    private LoopbackServer(int port, Func<LoopbackServer, TcpClient, Task> handler)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptAsync(handler);
    }

    public int Port { get; }

    public int Connections => Volatile.Read(ref connectionCount);

    /// <summary>Keeps each connection open and yields every received line (newline kept).</summary>
    public static LoopbackServer LineReceiver() => new(0, static async (server, client) =>
    {
        var stream = client.GetStream();
        var buffer = new byte[65_536];
        var pending = new List<byte>();
        int count;
        while ((count = await stream.ReadAsync(buffer, server.stopping.Token)) > 0)
        {
            pending.AddRange(buffer.AsSpan(0, count));
            int newline;
            while ((newline = pending.IndexOf((byte)'\n')) >= 0)
            {
                server.data.Writer.TryWrite(pending.GetRange(0, newline + 1).ToArray());
                pending.RemoveRange(0, newline + 1);
            }
        }
    });

    /// <summary>Reads one chunk from each connection, yields it and closes the connection.</summary>
    public static LoopbackServer ChunkReceiver(int port = 0) => new(port, static async (server, client) =>
    {
        var buffer = new byte[16_384];
        var count = await client.GetStream().ReadAsync(buffer, server.stopping.Token);
        if (count > 0) server.data.Writer.TryWrite(buffer[..count]);
        client.Dispose();
    });

    /// <summary>Yields each connection, kept open, so a test can write plug-in messages.</summary>
    public static LoopbackServer ResponseSender() => new(0, static (server, client) =>
    {
        server.connections.Writer.TryWrite(client);
        return Task.CompletedTask;
    });

    /// <summary>Only counts the connections and keeps them open.</summary>
    public static LoopbackServer Counter(int port = 0) => new(port, static (_, _) => Task.CompletedTask);

    public async Task<byte[]> NextData() => await data.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

    public async Task<TcpClient> NextConnection() => await connections.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

    private async Task AcceptAsync(Func<LoopbackServer, TcpClient, Task> handler)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                Interlocked.Increment(ref connectionCount);
                lock (accepted) accepted.Add(client);
                _ = Task.Run(async () =>
                {
                    try { await handler(this, client); }
                    catch (Exception) { /* the connection went away */ }
                });
            }
        }
        catch (Exception)
        {
            // Stopped.
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        lock (accepted)
        {
            foreach (var client in accepted) client.Dispose();
        }
        data.Writer.TryComplete();
        connections.Writer.TryComplete();
    }
}

/// <summary>Protocol 3 messages from the plug-in: acks, context events, results (MessageTests.swift).</summary>
public class MessageTests
{
    private static LightroomMessage? Decode(string line) => MessageCodec.DecodeLine(Encoding.UTF8.GetBytes(line + "\n"));

    private static MessageCodec.ValidationError DecodeError(string line) =>
        Assert.Throws<MessageCodec.ValidationException>(() => Decode(line)).Error;

    [Fact]
    public void DecodesTheAckThePlugInWrites()
    {
        var line = """{"accepted":true,"id":12,"kind":"ack","maximum":50000,"message":"accepted","minimum":2000,"parameter":"Temperature","scope":"develop","unit":"K","value":5100,"version":4}""";
        var ack = Assert.IsType<LightroomMessage.Ack>(Decode(line)).Value;
        Assert.Equal(12, ack.Id);
        Assert.True(ack.Accepted);
        Assert.Equal("Temperature", ack.Parameter);
        Assert.Equal(ParameterScope.Develop, ack.Scope);
        Assert.Equal(5100, ack.Value);
        Assert.Equal(2000, ack.Minimum);
        Assert.Equal(50000, ack.Maximum);
        Assert.Equal("K", ack.Unit);
        Assert.Equal(new ParameterKey(ParameterScope.Develop, "Temperature"), ack.ParameterKey);
    }

    [Fact]
    public void RejectedAckWithoutIdOrValues()
    {
        var ack = Assert.IsType<LightroomMessage.Ack>(Decode("""{"accepted":false,"kind":"ack","message":"malformed JSON","version":4}""")).Value;
        Assert.Null(ack.Id);
        Assert.False(ack.Accepted);
        Assert.Null(ack.Value);
        Assert.Null(ack.ParameterKey);
    }

    [Fact]
    public void MaskAckCarriesTheLocalParameter()
    {
        var line = """{"accepted":true,"id":3,"kind":"ack","maximum":4,"message":"accepted","minimum":-4,"parameter":"local_Exposure","scope":"develop","unit":"EV","value":0.25,"version":4}""";
        var ack = Assert.IsType<LightroomMessage.Ack>(Decode(line)).Value;
        Assert.Equal("local_Exposure", ack.Parameter);
        Assert.Equal(0.25, ack.Value);
    }

    [Fact]
    public void AnUnknownScopeIsLeftOut()
    {
        var ack = Assert.IsType<LightroomMessage.Ack>(Decode("""{"accepted":true,"id":3,"kind":"ack","parameter":"Exposure","scope":"map","version":4}""")).Value;
        Assert.Null(ack.Scope);
        Assert.Equal(new ParameterKey(ParameterScope.Develop, "Exposure"), ack.ParameterKey);
    }

    [Fact]
    public void DecodesTheContextEvent()
    {
        var message = Decode("""{"kind":"event","module":"develop","name":"context","tool":"masking","version":4}""");
        Assert.Equal(new LightroomMessage.Context(new LightroomContext("develop", "masking")), message);
        var context = Assert.IsType<LightroomMessage.Context>(message).Value;
        Assert.True(context.IsMasking);
    }

    [Fact]
    public void DecodesResults()
    {
        var presets = Decode("""{"id":4,"items":[{"group":"Favourites","id":"A1-B2","name":"Warm \"Film\""}],"kind":"result","name":"presets","version":4}""");
        Assert.Equal(new LightroomMessage.Result(new LightroomResult.Presets(4, [new PresetItem("A1-B2", "Warm \"Film\"", "Favourites")], false)), presets);
        var catalog = Decode("""{"id":5,"kind":"result","name":"catalog","skipped":["local_Amount"],"unsupported":["LensBlurAmount"],"version":4}""");
        Assert.Equal(new LightroomMessage.Result(new LightroomResult.Catalog(5, ["LensBlurAmount"], ["local_Amount"])), catalog);
        var context = Decode("""{"id":6,"kind":"result","module":"library","name":"context","tool":"","version":4}""");
        Assert.Equal(new LightroomMessage.Result(new LightroomResult.Context(6, new LightroomContext("library", ""))), context);
        Assert.Equal(6, Assert.IsType<LightroomMessage.Result>(context).Value.Id);
    }

    [Fact]
    public void UnknownKindsEventsAndResultsAreIgnored()
    {
        Assert.Null(Decode("""{"kind":"telemetry","version":4}"""));
        Assert.Null(Decode("""{"kind":"event","name":"weather","version":4}"""));
        Assert.Null(Decode("""{"id":1,"kind":"result","name":"secrets","version":4}"""));
        Assert.NotNull(Decode("""{"accepted":true,"future":[1,2],"kind":"ack","version":4}"""));
    }

    [Fact]
    public void OtherVersionsAndMalformedLinesThrow()
    {
        var other = Assert.Throws<MessageCodec.ValidationException>(() => Decode("""{"accepted":true,"action":"ping","message":"x","version":2}"""));
        Assert.Equal(MessageCodec.ValidationError.UnsupportedVersion, other.Error);
        Assert.Equal(2, other.Version);
        Assert.Equal(MessageCodec.ValidationError.Malformed, DecodeError("""{"kind":"ack","version":4}"""));
        Assert.Equal(MessageCodec.ValidationError.Malformed, DecodeError("not json"));
        Assert.Equal(MessageCodec.ValidationError.Malformed, DecodeError("""{"accepted":"yes","kind":"ack","version":4}"""));
        Assert.Equal(MessageCodec.ValidationError.Malformed, DecodeError("""{"kind":"ack","accepted":true}"""));
        Assert.Equal(MessageCodec.ValidationError.InvalidNumber, DecodeError("""{"accepted":true,"kind":"ack","value":1e400,"version":4}"""));
    }

    [Fact]
    public void LinesOverOneMegabyteAreRefused()
    {
        var name = new string('x', MessageCodec.MaximumLineBytes);
        Assert.Equal(MessageCodec.ValidationError.LineTooLong, DecodeError($$"""{"kind":"event","name":"{{name}}","version":4}"""));
    }

    [Fact]
    public void AckValidation()
    {
        Assert.Equal(MessageCodec.ValidationError.InvalidNumber,
            Assert.Throws<MessageCodec.ValidationException>(() => new LightroomAck(1, true, value: double.NaN)).Error);
        Assert.Equal(MessageCodec.ValidationError.UnitTooLong,
            Assert.Throws<MessageCodec.ValidationException>(() => new LightroomAck(1, true, unit: "123456789")).Error);
        Assert.Equal(MessageCodec.ValidationError.MessageTooLong,
            Assert.Throws<MessageCodec.ValidationException>(() => new LightroomAck(1, false, message: new string('x', 1_025))).Error);
        // Bytes, not characters: 513 two-byte letters are too many.
        Assert.Equal(MessageCodec.ValidationError.MessageTooLong,
            Assert.Throws<MessageCodec.ValidationException>(() => new LightroomAck(1, false, message: new string('é', 513))).Error);
        _ = new LightroomAck(1, true, unit: "12345678");
    }

    [Fact]
    public void MessagesRoundTrip()
    {
        LightroomMessage[] messages =
        [
            new LightroomMessage.Ack(new LightroomAck(9, true, "accepted", "Clarity", ParameterScope.Library, 12, -100, 100, "pt")),
            new LightroomMessage.Context(new LightroomContext("develop", "crop")),
            new LightroomMessage.Result(new LightroomResult.Presets(2, [new PresetItem("X", "Y", "Z")], true)),
            new LightroomMessage.Result(new LightroomResult.Catalog(3, [], [])),
        ];
        foreach (var message in messages) Assert.Equal(message, MessageCodec.DecodeLine(MessageCodec.EncodeLine(message)));
    }

    [Fact]
    public void EncodesLinesAsThePlugInWrites()
    {
        var ack = new LightroomMessage.Ack(new LightroomAck(12, true, "accepted", "Temperature", ParameterScope.Develop, 5100, 2000, 50000, "K"));
        Assert.Equal(
            """{"accepted":true,"id":12,"kind":"ack","maximum":50000,"message":"accepted","minimum":2000,"parameter":"Temperature","scope":"develop","unit":"K","value":5100,"version":4}""" + "\n",
            Encoding.UTF8.GetString(MessageCodec.EncodeLine(ack)));
        var presets = new LightroomMessage.Result(new LightroomResult.Presets(4, [new PresetItem("A1-B2", "Café / Film", "Favourites")], false));
        Assert.Equal(
            """{"id":4,"items":[{"group":"Favourites","id":"A1-B2","name":"Caf""" + "é" + """ / Film"}],"kind":"result","name":"presets","version":4}""" + "\n",
            Encoding.UTF8.GetString(MessageCodec.EncodeLine(presets)));
    }
}
