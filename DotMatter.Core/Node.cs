using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;
using Org.BouncyCastle.Math;
using System.Net;

namespace DotMatter.Core;

/// <summary>
/// Represents a Matter node known to a fabric.
/// </summary>
public class Node
{
    private ISession? _secureSession;

    /// <summary>
    /// Gets the established secure session, if the node is connected.
    /// </summary>
    public ISession? SecureSession => _secureSession;

    /// <summary>
    /// Gets or sets the Matter node identifier.
    /// </summary>
    public BigInteger NodeId { get; set; } = default!;

    /// <summary>
    /// Gets the node identifier formatted as a hexadecimal node name.
    /// </summary>
    public string NodeName => Convert.ToHexString([.. NodeId.ToByteArray().Reverse()]);

    /// <summary>
    /// Gets or sets the last operational IP address discovered for this node.
    /// </summary>
    public IPAddress? LastKnownIpAddress { get; set; }

    /// <summary>
    /// Gets or sets the last operational port discovered for this node.
    /// </summary>
    public ushort? LastKnownPort { get; set; }

    /// <summary>
    /// Gets or sets the fabric that owns this node.
    /// </summary>
    public Fabric Fabric { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether a secure session is currently established.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Gets or sets endpoints discovered on the node.
    /// </summary>
    public List<Endpoint> Endpoints { get; set; } = [];

    /// <summary>
    /// Establishes a CASE secure session to the node.
    /// </summary>
    /// <param name="nodeRegister">Registry used to resolve node information during session establishment.</param>
    public async Task ConnectAsync(INodeRegister nodeRegister)
    {
        ArgumentNullException.ThrowIfNull(nodeRegister);

        try
        {
            var connection = new UdpConnection(LastKnownIpAddress!, LastKnownPort!.Value);

            var unsecureSession = new UnsecureSession(connection);

            CASEClient client = new(this, Fabric, unsecureSession);

            _secureSession = await client.EstablishSessionAsync();

            IsConnected = true;

            MatterLog.Info("Established secure session to node {NodeId}.", NodeId);
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, "Failed to establish connection to node {NodeId}.", NodeId);
            IsConnected = false;
        }
    }

    /// <summary>
    /// Reads the descriptor cluster and refreshes the node endpoint list.
    /// </summary>
    public async Task FetchDescriptionsAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        Endpoints = [];

        var exchange = _secureSession!.CreateExchange();

        var readCluster = new MatterTLV();
        readCluster.AddStructure();

        readCluster.AddArray(tagNumber: 0);

        readCluster.AddList();
        readCluster.AddUInt32(tagNumber: 3, 0x1D); // ClusterId 0x1D - Description
        readCluster.AddUInt32(tagNumber: 4, 0x00); // Attribute 0x00 - DeviceTypeList
        readCluster.EndContainer(); // Close the list

        readCluster.EndContainer(); // Close the array

        readCluster.AddBool(tagNumber: 3, false);

        readCluster.AddUInt8(255, 12);

        readCluster.EndContainer();

        var readClusterMessagePayload = new MessagePayload(readCluster);

        readClusterMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

        // Table 14. Protocol IDs for the Matter Standard Vendor ID
        readClusterMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
        // From Table 18. Secure Channel Protocol Opcodes
        readClusterMessagePayload.ProtocolOpCode = 0x2; // ReadRequest

        var readClusterMessageFrame = new MessageFrame(readClusterMessagePayload);

        readClusterMessageFrame.MessageFlags |= MessageFlags.S;
        readClusterMessageFrame.SecurityFlags = 0x00;

        await exchange.SendAsync(readClusterMessageFrame);

        var readClusterResponseMessageFrame = await exchange.WaitForNextMessageAsync();

        await exchange.AcknowledgeMessageAsync(readClusterMessageFrame.MessageCounter);

        var resultPayload = readClusterResponseMessageFrame.MessagePayload;

        var tlv = resultPayload.ApplicationPayload!;

        MatterLog.Debug(tlv.ToString());

        var reportData = new ReportDataAction(tlv);

        foreach (var attributeReport in reportData.AttributeReports)
        {
            Endpoint endpoint = new(attributeReport.AttributeData!.Path.EndpointId);

            var data = attributeReport.AttributeData.Data as List<object?>;

            if (data is not null)
            {
                var deviceTypeList = data[0] as List<object?>;

                if (deviceTypeList is not null && deviceTypeList[0] is ulong deviceType)
                {
                    endpoint.DeviceType = deviceType;
                }
            }

            Endpoints.Add(endpoint);
        }

        exchange.Close();
    }
}
