using System.Net;
using System.Net.Sockets;

namespace Scs.Tests;

internal static class TestHelpers
{
    /// <summary>
    /// Gets a free TCP port by briefly binding to port 0 and reading the assigned port.
    /// </summary>
    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
