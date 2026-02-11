using Hik.Communication.Scs.Client;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Server;
using Xunit;

namespace Scs.Tests;

/// <summary>
/// Tests for connection lifecycle: connect, disconnect, reconnect, and event firing.
/// </summary>
public class ConnectionLifecycleTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServer _server;
    private readonly IScsClient _client;

    public ConnectionLifecycleTests()
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
    public void Client_InitialState_IsDisconnected()
    {
        Assert.Equal(CommunicationStates.Disconnected, _client.CommunicationState);
    }

    [Fact]
    public void Client_Connect_StateBecomesConnected()
    {
        _server.Start();
        _client.Connect();

        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);
    }

    [Fact]
    public void Client_ConnectAndDisconnect_StateBecomesDisconnected()
    {
        _server.Start();
        _client.Connect();
        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);

        _client.Disconnect();
        Assert.Equal(CommunicationStates.Disconnected, _client.CommunicationState);
    }

    [Fact]
    public void Client_ConnectedEvent_Fires()
    {
        _server.Start();
        var connected = false;
        _client.Connected += (_, _) => connected = true;

        _client.Connect();

        Assert.True(connected);
    }

    [Fact]
    public void Client_DisconnectedEvent_Fires()
    {
        _server.Start();
        var disconnectedEvent = new ManualResetEventSlim(false);
        _client.Disconnected += (_, _) => disconnectedEvent.Set();

        _client.Connect();
        _client.Disconnect();

        Assert.True(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Server_ClientConnectedEvent_Fires()
    {
        _server.Start();
        var connectedEvent = new ManualResetEventSlim(false);
        _server.ClientConnected += (_, _) => connectedEvent.Set();

        _client.Connect();

        Assert.True(connectedEvent.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Server_ClientDisconnectedEvent_Fires()
    {
        _server.Start();
        var disconnectedEvent = new ManualResetEventSlim(false);
        _server.ClientDisconnected += (_, _) => disconnectedEvent.Set();

        _client.Connect();
        _client.Disconnect();

        Assert.True(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Server_TracksConnectedClients()
    {
        _server.Start();
        var connectedEvent = new ManualResetEventSlim(false);
        _server.ClientConnected += (_, _) => connectedEvent.Set();

        _client.Connect();
        connectedEvent.Wait(TimeSpan.FromSeconds(5));

        Assert.Single(_server.Clients.GetAllItems());
    }

    [Fact]
    public void Client_DisconnectWhenAlreadyDisconnected_DoesNotThrow()
    {
        _server.Start();
        // Never connected, disconnect should be a no-op
        _client.Disconnect();
    }

    [Fact]
    public void Client_ConnectToStoppedServer_Throws()
    {
        // Server not started
        Assert.ThrowsAny<Exception>(() => _client.Connect());
    }

    [Fact]
    public void Client_SendMessageAndReceive_Works()
    {
        _server.Start();
        var receivedEvent = new ManualResetEventSlim(false);
        IScsMessage? receivedMessage = null;

        _server.ClientConnected += (_, args) =>
        {
            args.Client.MessageReceived += (_, msgArgs) =>
            {
                receivedMessage = msgArgs.Message;
                receivedEvent.Set();
            };
        };

        _client.Connect();
        _client.SendMessage(new ScsTextMessage("hello"));

        Assert.True(receivedEvent.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<ScsTextMessage>(receivedMessage);
        Assert.Equal("hello", ((ScsTextMessage)receivedMessage!).Text);
    }

    [Fact]
    public void Server_SendToClient_ClientReceives()
    {
        _server.Start();
        var receivedEvent = new ManualResetEventSlim(false);
        IScsMessage? receivedMessage = null;
        var serverReady = new ManualResetEventSlim(false);
        IScsServerClient? serverClient = null;

        _server.ClientConnected += (_, args) =>
        {
            serverClient = args.Client;
            serverReady.Set();
        };

        _client.MessageReceived += (_, args) =>
        {
            receivedMessage = args.Message;
            receivedEvent.Set();
        };

        _client.Connect();
        serverReady.Wait(TimeSpan.FromSeconds(5));
        serverClient!.SendMessage(new ScsTextMessage("from server"));

        Assert.True(receivedEvent.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("from server", ((ScsTextMessage)receivedMessage!).Text);
    }

    [Fact]
    public void MultipleClients_CanConnectSimultaneously()
    {
        _server.Start();
        var clientCount = new ManualResetEventSlim(false);
        var connectedCount = 0;

        _server.ClientConnected += (_, _) =>
        {
            if (Interlocked.Increment(ref connectedCount) >= 3)
                clientCount.Set();
        };

        var clients = new List<IScsClient>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var c = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
                c.WireProtocol = new TestWireProtocol();
                c.Connect();
                clients.Add(c);
            }

            Assert.True(clientCount.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(3, connectedCount);
        }
        finally
        {
            foreach (var c in clients)
                try { c.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void Reconnect_AfterDisconnect_Succeeds()
    {
        _server.Start();

        _client.Connect();
        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);

        _client.Disconnect();
        Assert.Equal(CommunicationStates.Disconnected, _client.CommunicationState);

        // Reconnect
        _client.Connect();
        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);
    }

    [Fact]
    public void ClientReConnecter_ReconnectsAfterServerRestart()
    {
        _server.Start();
        _client.Connect();
        Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);

        using var reconnecter = new ClientReConnecter(_client);
        reconnecter.ReConnectCheckPeriod = 500; // Check every 500ms for fast test

        // Stop the server - client will disconnect
        _server.Stop();
        Thread.Sleep(500); // Give time for disconnect to propagate

        // Restart server on the same port with the test wire protocol
        var server2 = ScsServerFactory.CreateServer(new ScsTcpEndPoint("127.0.0.1", _port));
        server2.WireProtocolFactory = new TestWireProtocolFactory();
        try
        {
            server2.Start();

            // Wait for reconnection (up to 5 seconds)
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_client.CommunicationState != CommunicationStates.Connected && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            Assert.Equal(CommunicationStates.Connected, _client.CommunicationState);
        }
        finally
        {
            server2.Stop();
        }
    }
}
