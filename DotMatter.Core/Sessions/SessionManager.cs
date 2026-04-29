using DotMatter.Core.Fabrics;
using System.Threading.Channels;

namespace DotMatter.Core.Sessions;

/// <summary>
/// Maintains secure sessions for fabric nodes and processes queued connection attempts.
/// </summary>
public class SessionManager(INodeRegister nodeRegister) : ISessionManager
{
    private readonly Dictionary<Node, ISession> _secureSessions = [];
    private readonly Channel<Node> _connectionsQueue = Channel.CreateUnbounded<Node>();
    private readonly INodeRegister _nodeRegister = nodeRegister ?? throw new ArgumentNullException(nameof(nodeRegister));

    /// <summary>
    /// Gets the established secure session for a node.
    /// </summary>
    /// <param name="node">The node whose secure session should be returned.</param>
    public ISession GetSecureSession(Node node)
    {
        return _secureSessions[node];
    }

    /// <inheritdoc />
    public async Task StartAsync(Fabric fabric, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fabric);

        foreach (var node in fabric.Nodes)
        {
            await _connectionsQueue.Writer.WriteAsync(node, cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            MatterLog.Info("Waiting for a node that needs a connection.");

            var nodeNeedingConnection = await _connectionsQueue.Reader.ReadAsync(cancellationToken);

            try
            {
                var fullNodeName = nodeNeedingConnection.Fabric.GetFullNodeName(nodeNeedingConnection);
                MatterLog.Info("Attempting to connect to node {Node}.", fullNodeName);

                await nodeNeedingConnection.ConnectAsync(_nodeRegister);
            }
            catch (Exception ex)
            {
                MatterLog.Warn(ex, "Failed to connect to node {Node}.", nodeNeedingConnection.NodeName);
            }
        }
    }
}
