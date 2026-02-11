using Hik.Communication.Scs.Client;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messengers;
using Hik.Communication.Scs.Server;
using Hik.Communication.ScsServices.Client;
using Hik.Communication.ScsServices.Communication.Messages;
using Hik.Communication.ScsServices.Service;
using Xunit;

namespace Scs.Tests;

/// <summary>
/// Test service interface for RMI.
/// </summary>
[ScsService]
public interface ICalculatorService
{
    int Add(int a, int b);
    string Echo(string input);
    void DoNothing();
    int Multiply(int a, int b);
    string Concatenate(string a, string b);
}

/// <summary>
/// Test service implementation.
/// </summary>
public class CalculatorService : ScsService, ICalculatorService
{
    public int Add(int a, int b) => a + b;
    public string Echo(string input) => input;
    public void DoNothing() { }
    public int Multiply(int a, int b) => a * b;
    public string Concatenate(string a, string b) => a + b;
}

/// <summary>
/// Service that throws exceptions for testing exception propagation.
/// </summary>
[ScsService]
public interface IFaultyService
{
    int WillThrow();
    string ThrowWithMessage(string message);
}

public class FaultyService : ScsService, IFaultyService
{
    public int WillThrow() => throw new InvalidOperationException("Deliberate test failure");
    public string ThrowWithMessage(string message) => throw new ArgumentException(message);
}

/// <summary>
/// Helper to build service server and client with custom wire protocol (needed for .NET 10+
/// since BinaryFormatter is removed).
/// </summary>
internal static class ServiceTestHelper
{
    public static IScsServiceApplication CreateServiceApp(ScsTcpEndPoint endpoint)
    {
        var server = ScsServerFactory.CreateServer(endpoint);
        server.WireProtocolFactory = new TestWireProtocolFactory();
        return new ScsServiceApplication(server);
    }

    public static IScsServiceClient<T> CreateServiceClient<T>(ScsTcpEndPoint endpoint) where T : class
    {
        var client = ScsClientFactory.CreateClient(endpoint);
        client.WireProtocol = new TestWireProtocol();
        return new ScsServiceClient<T>(client, null);
    }
}

/// <summary>
/// RMI tests: interface-based proxy calls, return values, void methods, and exception propagation.
/// </summary>
public class RmiTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServiceApplication _serviceApp;
    private readonly IScsServiceClient<ICalculatorService> _calcClient;

    public RmiTests()
    {
        _port = TestHelpers.GetFreePort();
        var endpoint = new ScsTcpEndPoint("127.0.0.1", _port);

        _serviceApp = ServiceTestHelper.CreateServiceApp(endpoint);
        _serviceApp.AddService<ICalculatorService, CalculatorService>(new CalculatorService());
        _serviceApp.Start();

        _calcClient = ServiceTestHelper.CreateServiceClient<ICalculatorService>(endpoint);
        _calcClient.Connect();
    }

    public void Dispose()
    {
        try { _calcClient.Disconnect(); } catch { }
        try { _serviceApp.Stop(); } catch { }
    }

    [Fact]
    public void Add_ReturnsSumOfArguments()
    {
        var result = _calcClient.ServiceProxy.Add(3, 4);
        Assert.Equal(7, result);
    }

    [Fact]
    public void Multiply_ReturnsProduct()
    {
        var result = _calcClient.ServiceProxy.Multiply(6, 7);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Echo_ReturnsInput()
    {
        var result = _calcClient.ServiceProxy.Echo("test string");
        Assert.Equal("test string", result);
    }

    [Fact]
    public void Echo_EmptyString_ReturnsEmpty()
    {
        var result = _calcClient.ServiceProxy.Echo("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Concatenate_JoinsStrings()
    {
        var result = _calcClient.ServiceProxy.Concatenate("Hello, ", "World!");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void VoidMethod_DoesNotThrow()
    {
        _calcClient.ServiceProxy.DoNothing();
    }

    [Fact]
    public void MultipleCalls_AllSucceed()
    {
        for (int i = 0; i < 10; i++)
        {
            var result = _calcClient.ServiceProxy.Add(i, i);
            Assert.Equal(i * 2, result);
        }
    }

    [Fact]
    public void NegativeNumbers_Work()
    {
        var result = _calcClient.ServiceProxy.Add(-5, -3);
        Assert.Equal(-8, result);
    }
}

/// <summary>
/// Exception propagation tests via RMI.
/// </summary>
public class ExceptionPropagationTests : IDisposable
{
    private readonly int _port;
    private readonly IScsServiceApplication _serviceApp;
    private readonly IScsServiceClient<IFaultyService> _client;

    public ExceptionPropagationTests()
    {
        _port = TestHelpers.GetFreePort();
        var endpoint = new ScsTcpEndPoint("127.0.0.1", _port);

        _serviceApp = ServiceTestHelper.CreateServiceApp(endpoint);
        _serviceApp.AddService<IFaultyService, FaultyService>(new FaultyService());
        _serviceApp.Start();

        _client = ServiceTestHelper.CreateServiceClient<IFaultyService>(endpoint);
        _client.Connect();
    }

    public void Dispose()
    {
        try { _client.Disconnect(); } catch { }
        try { _serviceApp.Stop(); } catch { }
    }

    [Fact]
    public void ServiceException_PropagatedToClient()
    {
        var ex = Assert.Throws<ScsRemoteException>(() => _client.ServiceProxy.WillThrow());
        Assert.Contains("Deliberate test failure", ex.Message);
    }

    [Fact]
    public void ServiceException_WithCustomMessage_PropagatedToClient()
    {
        var ex = Assert.Throws<ScsRemoteException>(() => _client.ServiceProxy.ThrowWithMessage("custom error"));
        Assert.Contains("custom error", ex.Message);
    }

    [Fact]
    public void ServiceException_ContainsVersionInfo()
    {
        var ex = Assert.Throws<ScsRemoteException>(() => _client.ServiceProxy.WillThrow());
        Assert.Contains("Service Version:", ex.Message);
    }
}
