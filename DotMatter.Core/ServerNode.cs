using System.Net.Sockets;

namespace DotMatter.Core;

/// <summary>ServerNode class.</summary>
public class ServerNode
{
    private UdpClient _listener = default!;

    /// <summary>ServerNode.</summary>
    public ServerNode()
    {

    }

    /// <summary>StartAsync.</summary>
    public void StartAsync()
    {
        _listener = new UdpClient();
    }
}
