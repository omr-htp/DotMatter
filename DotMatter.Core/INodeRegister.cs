namespace DotMatter.Core;

/// <summary>INodeRegister interface.</summary>
public interface INodeRegister
{
    /// <summary>CommissionableNodeDiscovered delegate.</summary>
    delegate void CommissionableNodeDiscovered(object sender, CommissionableNodeDiscoveredEventArgs e);
    /// <summary>CommissionableNodeDiscoveredEvent.</summary>
    /// <summary>Raised when CommissionableNodeDiscoveredEvent occurs.</summary>
    event CommissionableNodeDiscovered CommissionableNodeDiscoveredEvent;

    /// <summary>AddCommissionedNode.</summary>
    void AddCommissionedNode(string nodeIdAndCompressedFabricIdentifier, ushort port, string[] addresses);

    /// <summary>AddCommissionableNode.</summary>
    void AddCommissionableNode(string nodeIdAndCompressedFabricIdentifier, ushort discriminator, ushort port, string[] addresses);

    /// <summary>GetCommissionedNodeAddresses.</summary>
    string[] GetCommissionedNodeAddresses(string nodeIdAndCompressedFabricIdentifier);

    /// <summary>GetCommissionableNodeForDiscriminatorAsync.</summary>
    Task<NodeRegisterDetails> GetCommissionableNodeForDiscriminatorAsync(ushort discriminator);
}
