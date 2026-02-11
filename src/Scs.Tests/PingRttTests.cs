using Hik.Communication.Scs.Client;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Server;
using Xunit;

namespace Scs.Tests;

/// <summary>
/// Tests for ping RTT measurement: LastPingRtt, AveragePingRtt, PingCompleted event,
/// and configurable PingInterval.
/// </summary>
public class PingRttTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServer _server;
    private readonly IScsClient _client;

    public PingRttTests()
    {
        _port = TestHelpers.GetFreePort();
        _server = ScsServerFactory.CreateServer(new ScsTcpEndPoint("127.0.0.1", _port));
        _client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
    }

    public void Dispose()
    {
        try { _client.Disconnect(); } catch { }
        try { _server.Stop(); } catch { }
    }

    [Fact]
    public void PingInterval_DefaultIs30000()
    {
        Assert.Equal(30000, _client.PingInterval);
    }

    [Fact]
    public void PingInterval_CanBeSetBeforeConnect()
    {
        _client.PingInterval = 5000;
        Assert.Equal(5000, _client.PingInterval);
    }

    [Fact]
    public void PingInterval_CanBeSetWhileConnected()
    {
        _server.Start();
        _client.Connect();

        _client.PingInterval = 10000;
        Assert.Equal(10000, _client.PingInterval);
    }

    [Fact]
    public void LastPingRtt_IsNullBeforeAnyPing()
    {
        Assert.Null(_client.LastPingRtt);
    }

    [Fact]
    public void AveragePingRtt_IsNullBeforeAnyPing()
    {
        Assert.Null(_client.AveragePingRtt);
    }

    [Fact]
    public void LastPingRtt_IsNullWhenConnectedButNoPingYet()
    {
        _server.Start();
        _client.Connect();

        // Just connected - no ping has been sent/received yet
        Assert.Null(_client.LastPingRtt);
    }

    [Fact]
    public void PingCompleted_FiresWithRtt_WhenPingSentManually()
    {
        // Use a short ping interval to trigger a ping quickly.
        // But the timer only sends pings if there's been no activity in the last minute.
        // So we'll test by directly sending a ping message and verifying the server replies.
        _server.Start();
        _client.PingInterval = 500;
        _client.Connect();

        var pingCompleted = new ManualResetEventSlim(false);
        long? capturedRtt = null;

        _client.PingCompleted += (sender, args) =>
        {
            capturedRtt = args.RoundTripTimeMs;
            pingCompleted.Set();
        };

        // Send a ping message directly - the server should auto-reply
        _client.SendMessage(new ScsPingMessage());

        // Wait for the reply to arrive
        var received = pingCompleted.Wait(TimeSpan.FromSeconds(5));

        Assert.True(received, "PingCompleted event should have fired");
        Assert.NotNull(capturedRtt);
        Assert.True(capturedRtt >= 0, "RTT should be non-negative");
    }

    [Fact]
    public void LastPingRtt_SetAfterPingReply()
    {
        _server.Start();
        _client.Connect();

        var pingCompleted = new ManualResetEventSlim(false);
        _client.PingCompleted += (_, __) => pingCompleted.Set();

        _client.SendMessage(new ScsPingMessage());
        var received = pingCompleted.Wait(TimeSpan.FromSeconds(5));

        Assert.True(received);
        Assert.NotNull(_client.LastPingRtt);
        Assert.True(_client.LastPingRtt >= 0);
    }

    [Fact]
    public void AveragePingRtt_SetAfterPingReply()
    {
        _server.Start();
        _client.Connect();

        var pingCompleted = new ManualResetEventSlim(false);
        _client.PingCompleted += (_, __) => pingCompleted.Set();

        _client.SendMessage(new ScsPingMessage());
        var received = pingCompleted.Wait(TimeSpan.FromSeconds(5));

        Assert.True(received);
        Assert.NotNull(_client.AveragePingRtt);
        Assert.True(_client.AveragePingRtt >= 0);
    }

    [Fact]
    public void AveragePingRtt_AveragesMultiplePings()
    {
        _server.Start();
        _client.Connect();

        var count = 0;
        var allDone = new ManualResetEventSlim(false);

        _client.PingCompleted += (_, __) =>
        {
            if (Interlocked.Increment(ref count) >= 3)
                allDone.Set();
        };

        // Send 3 pings
        for (int i = 0; i < 3; i++)
        {
            _client.SendMessage(new ScsPingMessage());
            Thread.Sleep(50); // Brief delay between pings
        }

        var received = allDone.Wait(TimeSpan.FromSeconds(5));
        Assert.True(received, "Should have received 3 ping replies");

        Assert.NotNull(_client.AveragePingRtt);
        Assert.True(_client.AveragePingRtt >= 0);
    }

    [Fact]
    public void PingCompleted_EventArgs_ContainsRoundTripTime()
    {
        _server.Start();
        _client.Connect();

        PingCompletedEventArgs? capturedArgs = null;
        var pingCompleted = new ManualResetEventSlim(false);

        _client.PingCompleted += (_, args) =>
        {
            capturedArgs = args;
            pingCompleted.Set();
        };

        _client.SendMessage(new ScsPingMessage());
        var received = pingCompleted.Wait(TimeSpan.FromSeconds(5));

        Assert.True(received);
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs!.RoundTripTimeMs >= 0);
    }

    [Fact]
    public void PingCompletedEventArgs_StoresRoundTripTime()
    {
        var args = new PingCompletedEventArgs(42);
        Assert.Equal(42, args.RoundTripTimeMs);
    }

    [Fact]
    public void PingCompletedEventArgs_StoresZeroRtt()
    {
        var args = new PingCompletedEventArgs(0);
        Assert.Equal(0, args.RoundTripTimeMs);
    }

    [Fact]
    public void LastPingRtt_UpdatesOnEachPing()
    {
        _server.Start();
        _client.Connect();

        var pingCompleted = new ManualResetEventSlim(false);

        _client.PingCompleted += (_, __) => pingCompleted.Set();

        // First ping
        _client.SendMessage(new ScsPingMessage());
        Assert.True(pingCompleted.Wait(TimeSpan.FromSeconds(5)));
        var firstRtt = _client.LastPingRtt;
        Assert.NotNull(firstRtt);

        // Second ping
        pingCompleted.Reset();
        _client.SendMessage(new ScsPingMessage());
        Assert.True(pingCompleted.Wait(TimeSpan.FromSeconds(5)));
        var secondRtt = _client.LastPingRtt;
        Assert.NotNull(secondRtt);

        // Both should be valid (non-negative) - we can't predict exact values
        Assert.True(firstRtt >= 0);
        Assert.True(secondRtt >= 0);
    }

    [Fact]
    public void NonPingReply_DoesNotAffectRtt()
    {
        // A ping message without a RepliedMessageId (not a reply) should not
        // affect RTT tracking. Only actual ping replies should.
        _server.Start();
        _client.Connect();

        var pingFired = false;
        _client.PingCompleted += (_, __) => pingFired = true;

        // Send a non-ping message - should not trigger RTT
        _client.SendMessage(new ScsTextMessage("hello"));
        Thread.Sleep(500);

        Assert.False(pingFired, "PingCompleted should not fire for non-ping messages");
        Assert.Null(_client.LastPingRtt);
    }
}
