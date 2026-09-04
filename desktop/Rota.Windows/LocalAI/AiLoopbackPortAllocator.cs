using System.Net;
using System.Net.Sockets;

namespace Rota.Desktop.LocalAI;

public sealed class AiLoopbackPortAllocator : IAiLoopbackPortAllocator
{
    public int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
