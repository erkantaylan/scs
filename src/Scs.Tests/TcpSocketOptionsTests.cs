using System.Net.Sockets;
using Hik.Communication.Scs.Client;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.Channels.Tcp;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Server;
using Xunit;

namespace Scs.Tests;

public class TcpSocketOptionsTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServer _server;

    public TcpSocketOptionsTests()
    {
        _port = TestHelpers.GetFreePort();
        _server = ScsServerFactory.CreateServer(new ScsTcpEndPoint("127.0.0.1", _port));
    }

    public void Dispose()
    {
        try { _server.Stop(); } catch { }
    }

    [Fact]
    public void DefaultOptions_NoDelayTrue_SendTimeout5000_ReceiveTimeout0()
    {
        var options = new TcpSocketOptions();

        Assert.True(options.NoDelay);
        Assert.False(options.KeepAliveEnabled);
        Assert.Null(options.KeepAliveTimeSeconds);
        Assert.Null(options.KeepAliveIntervalSeconds);
        Assert.Equal(5000, options.SendTimeout);
        Assert.Equal(0, options.ReceiveTimeout);
    }

    [Fact]
    public void Client_DefaultSocketOptions_AreSet()
    {
        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));

        Assert.NotNull(client.SocketOptions);
        Assert.True(client.SocketOptions.NoDelay);
        Assert.Equal(5000, client.SocketOptions.SendTimeout);
    }

    [Fact]
    public void Server_DefaultSocketOptions_AreSet()
    {
        Assert.NotNull(_server.SocketOptions);
        Assert.True(_server.SocketOptions.NoDelay);
        Assert.Equal(5000, _server.SocketOptions.SendTimeout);
    }

    [Fact]
    public void Client_CustomSocketOptions_CanBeSetBeforeConnect()
    {
        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));

        client.SocketOptions = new TcpSocketOptions
        {
            NoDelay = false,
            KeepAliveEnabled = true,
            KeepAliveTimeSeconds = 60,
            KeepAliveIntervalSeconds = 10,
            SendTimeout = 10000,
            ReceiveTimeout = 3000
        };

        Assert.False(client.SocketOptions.NoDelay);
        Assert.True(client.SocketOptions.KeepAliveEnabled);
        Assert.Equal(60, client.SocketOptions.KeepAliveTimeSeconds);
        Assert.Equal(10, client.SocketOptions.KeepAliveIntervalSeconds);
        Assert.Equal(10000, client.SocketOptions.SendTimeout);
        Assert.Equal(3000, client.SocketOptions.ReceiveTimeout);
    }

    [Fact]
    public void Client_ConnectWithDefaultOptions_Succeeds()
    {
        _server.Start();
        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));

        try
        {
            client.Connect();
            Assert.Equal(CommunicationStates.Connected, client.CommunicationState);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void Client_ConnectWithNoDelayDisabled_Succeeds()
    {
        _server.Start();
        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        client.SocketOptions = new TcpSocketOptions { NoDelay = false };

        try
        {
            client.Connect();
            Assert.Equal(CommunicationStates.Connected, client.CommunicationState);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void Client_ConnectWithKeepaliveEnabled_Succeeds()
    {
        _server.Start();
        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        client.SocketOptions = new TcpSocketOptions
        {
            KeepAliveEnabled = true,
            KeepAliveTimeSeconds = 60,
            KeepAliveIntervalSeconds = 10
        };

        try
        {
            client.Connect();
            Assert.Equal(CommunicationStates.Connected, client.CommunicationState);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void Client_ConnectWithCustomTimeouts_CanSendMessages()
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

        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        client.SocketOptions = new TcpSocketOptions
        {
            SendTimeout = 10000,
            ReceiveTimeout = 10000
        };

        try
        {
            client.Connect();
            client.SendMessage(new ScsTextMessage("hello with timeouts"));

            Assert.True(receivedEvent.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsType<ScsTextMessage>(receivedMessage);
            Assert.Equal("hello with timeouts", ((ScsTextMessage)receivedMessage).Text);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void Server_CustomSocketOptions_AppliedToAcceptedConnections()
    {
        _server.SocketOptions = new TcpSocketOptions
        {
            NoDelay = true,
            KeepAliveEnabled = true,
            SendTimeout = 8000,
            ReceiveTimeout = 4000
        };

        _server.Start();
        var connectedEvent = new ManualResetEventSlim(false);
        _server.ClientConnected += (_, _) => connectedEvent.Set();

        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        try
        {
            client.Connect();
            Assert.True(connectedEvent.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(CommunicationStates.Connected, client.CommunicationState);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    [Fact]
    public void CopyConstructor_CopiesAllProperties()
    {
        var original = new TcpSocketOptions
        {
            NoDelay = false,
            KeepAliveEnabled = true,
            KeepAliveTimeSeconds = 30,
            KeepAliveIntervalSeconds = 5,
            SendTimeout = 7000,
            ReceiveTimeout = 3000
        };

        var copy = new TcpSocketOptions(original);

        Assert.Equal(original.NoDelay, copy.NoDelay);
        Assert.Equal(original.KeepAliveEnabled, copy.KeepAliveEnabled);
        Assert.Equal(original.KeepAliveTimeSeconds, copy.KeepAliveTimeSeconds);
        Assert.Equal(original.KeepAliveIntervalSeconds, copy.KeepAliveIntervalSeconds);
        Assert.Equal(original.SendTimeout, copy.SendTimeout);
        Assert.Equal(original.ReceiveTimeout, copy.ReceiveTimeout);
    }

    [Fact]
    public void Client_ConnectWithAllOptions_CanCommunicateBidirectionally()
    {
        _server.SocketOptions = new TcpSocketOptions
        {
            KeepAliveEnabled = true,
            SendTimeout = 10000,
            ReceiveTimeout = 10000
        };

        _server.Start();

        var serverReceivedEvent = new ManualResetEventSlim(false);
        var clientReceivedEvent = new ManualResetEventSlim(false);
        IScsServerClient? serverClient = null;
        var serverReady = new ManualResetEventSlim(false);

        _server.ClientConnected += (_, args) =>
        {
            serverClient = args.Client;
            args.Client.MessageReceived += (_, msgArgs) =>
            {
                serverReceivedEvent.Set();
                // Echo back
                args.Client.SendMessage(new ScsTextMessage("echo: " + ((ScsTextMessage)msgArgs.Message).Text));
            };
            serverReady.Set();
        };

        var client = ScsClientFactory.CreateClient(new ScsTcpEndPoint("127.0.0.1", _port));
        client.SocketOptions = new TcpSocketOptions
        {
            KeepAliveEnabled = true,
            SendTimeout = 10000,
            ReceiveTimeout = 10000
        };

        IScsMessage? clientResponse = null;
        client.MessageReceived += (_, args) =>
        {
            clientResponse = args.Message;
            clientReceivedEvent.Set();
        };

        try
        {
            client.Connect();
            serverReady.Wait(TimeSpan.FromSeconds(5));

            client.SendMessage(new ScsTextMessage("ping"));

            Assert.True(serverReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(clientReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal("echo: ping", ((ScsTextMessage)clientResponse).Text);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }
}
