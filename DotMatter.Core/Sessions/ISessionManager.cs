using DotMatter.Core.Fabrics;

namespace DotMatter.Core.Sessions;

/// <summary>
/// Coordinates background connection attempts for nodes in a fabric.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Starts processing connection attempts for all nodes in the fabric.
    /// </summary>
    /// <param name="fabric">The fabric whose nodes should be connected.</param>
    /// <param name="cancellationToken">Token used to stop the background processing loop.</param>
    Task StartAsync(Fabric fabric, CancellationToken cancellationToken = default);
}
