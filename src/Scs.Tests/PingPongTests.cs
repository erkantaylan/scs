using Hik.Communication.Scs.Client;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Server;
using Xunit;

namespace Scs.Tests;

/// <summary>
/// Tests for the ping/pong keep-alive mechanism.
/// Server should auto-reply to ScsPingMessage, and pings should not raise MessageReceived.
/// </summary>
public class PingPongTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServer _server;
    private readonly IScsClient _client;

    public PingPongTests()
    {
        _port = TestHelpers.GetFreePort();
        _server = ScsServerFactory.CreateServer(new ScsTcpEndPoint("127.0.0.1", _port));
        _server.WireProtocolFactory = new TestWireProtocolFactory();
        _client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        _client.WireProtocol = new TestWireProtocol();
    }

    public void Dispose()
    {
        try { _client.Disconnect(); } catch { }
        try { _server.Stop(); } catch { }
    }

    [Fact]
    public void PingMessage_CreatesValidMessageId()
    {
        var ping = new ScsPingMessage();
        Assert.False(string.IsNullOrEmpty(ping.MessageId));
    }

    [Fact]
    public void PingReplyMessage_HasRepliedMessageId()
    {
        var originalId = Guid.NewGuid().ToString();
        var pong = new ScsPingMessage(originalId);

        Assert.Equal(originalId, pong.RepliedMessageId);
    }

    [Fact]
    public void PingMessage_NotRaisedAsClientMessageReceived()
    {
        // Ping messages should be filtered out from the MessageReceived event on the client.
        // The client base class checks: if (e.Message is ScsPingMessage) return;
        _server.Start();
        var messageReceived = false;

        _client.MessageReceived += (_, args) =>
        {
            if (args.Message is ScsPingMessage)
                messageReceived = true;
        };

        _client.Connect();

        // Wait a bit to see if any ping messages bubble up (they shouldn't)
        Thread.Sleep(1000);

        Assert.False(messageReceived, "Ping messages should not be raised via MessageReceived on client");
    }

    [Fact]
    public void Connection_StaysAlive_WithPingMechanism()
    {
        // Verify that a connected client stays connected (ping keeps it alive)
        _server.Start();
        _client.Connect();

        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);

        // Wait briefly
        Thread.Sleep(500);

        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);
    }

    [Fact]
    public void PingMessage_ToString_IncludesMessageId()
    {
        var ping = new ScsPingMessage();
        var str = ping.ToString();

        Assert.Contains(ping.MessageId, str);
        Assert.Contains("ScsPingMessage", str);
    }

    [Fact]
    public void PingReplyMessage_ToString_IncludesRepliedId()
    {
        var replyId = "test-reply-id";
        var pong = new ScsPingMessage(replyId);
        var str = pong.ToString();

        Assert.Contains(replyId, str);
        Assert.Contains("Replied To", str);
    }
}
