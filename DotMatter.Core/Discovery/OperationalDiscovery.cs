using System.Net;
using DotMatter.Core.Mdns;

namespace DotMatter.Core.Discovery;

/// <summary>
/// Discovered Matter operational node from mDNS browse.
/// </summary>
public sealed class OperationalNode
{
    /// <summary>InstanceName.</summary>
    /// <summary>Gets or sets the InstanceName value.</summary>
    public required string InstanceName
    {
        get; init;
    }
    /// <summary>CompressedFabricId.</summary>
    /// <summary>Gets or sets the CompressedFabricId value.</summary>
    public required ulong CompressedFabricId
    {
        get; init;
    }
    /// <summary>NodeId.</summary>
    /// <summary>Gets or sets the NodeId value.</summary>
    public required ulong NodeId
    {
        get; init;
    }
    /// <summary>Address.</summary>
    /// <summary>Gets or sets the Address value.</summary>
    public required IPAddress Address
    {
        get; init;
    }
    /// <summary>Port.</summary>
    /// <summary>Gets or sets the Port value.</summary>
    public required int Port
    {
        get; init;
    }
}

/// <summary>
/// mDNS-based operational discovery for Matter nodes on the local network.
/// Browses <c>_matter._tcp.local</c> and resolves SRV/AAAA records.
/// </summary>
public sealed class OperationalDiscovery : IDisposable
{
    private const string OperationalService = "_matter._tcp.local.";
    private const string CommissionableService = "_matterc._udp.local.";

    private readonly MulticastService _mdns;
    private readonly ServiceDiscovery _sd;
    private readonly Dictionary<string, OperationalNode> _resolved = [];
    private bool _started;

    private sealed record OperationalInstanceName(ulong CompressedFabricId, ulong NodeId);

    /// <summary>Raised when a new Matter operational node is discovered.</summary>
    public event Action<OperationalNode>? NodeDiscovered;

    /// <summary>OperationalDiscovery.</summary>
    public OperationalDiscovery()
    {
        _mdns = new MulticastService();
        _sd = new ServiceDiscovery(_mdns);
    }

    /// <summary>Start mDNS browsing for operational Matter nodes.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _mdns.NetworkInterfaceDiscovered += (_, _) =>
        {
            _mdns.SendQuery(OperationalService, type: DnsType.PTR);
        };

        _mdns.AnswerReceived += OnAnswerReceived;
        _mdns.Start();
    }

    /// <summary>
    /// Resolve a specific node by compressed fabric ID and node ID.
    /// Sends a targeted mDNS query and waits for a response.
    /// </summary>
    /// <param name="compressedFabricId">Compressed fabric ID (8 bytes, hex-encoded in instance name).</param>
    /// <param name="nodeId">Node operational ID (8 bytes, hex-encoded in instance name).</param>
    /// <param name="timeout">How long to wait for a response.</param>
    /// <returns>The discovered node, or null if not found within timeout.</returns>
    public async Task<OperationalNode?> ResolveNodeAsync(
        ulong compressedFabricId, ulong nodeId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var instanceName = $"{compressedFabricId:X16}-{nodeId:X16}.{OperationalService}";

        // Check if already resolved
        if (_resolved.TryGetValue(instanceName, out var cached))
        {
            return cached;
        }

        var tcs = new TaskCompletionSource<OperationalNode?>();
        void handler(OperationalNode node)
        {
            if (node.CompressedFabricId == compressedFabricId && node.NodeId == nodeId)
            {
                tcs.TrySetResult(node);
            }
        }

        NodeDiscovered += handler;
        try
        {
            if (!_started)
            {
                Start();
            }

            _mdns.SendQuery(instanceName, type: DnsType.SRV);
            _mdns.SendQuery(instanceName, type: DnsType.AAAA);
            _mdns.SendQuery(instanceName, type: DnsType.A);

            using var cts = new CancellationTokenSource(effectiveTimeout);
            cts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task;
        }
        finally
        {
            NodeDiscovered -= handler;
        }
    }

    /// <summary>Return all currently-known operational nodes.</summary>
    public IReadOnlyCollection<OperationalNode> GetKnownNodes() => _resolved.Values;

    private void OnAnswerReceived(object? sender, MessageEventArgs e)
    {
        foreach (var record in e.Message.Answers.Concat(e.Message.AdditionalRecords))
        {
            if (record is PTRRecord ptr && ptr.Name.ToString() == OperationalService)
            {
                // Discovered an instance — query for SRV and address
                _mdns.SendQuery(ptr.DomainName, type: DnsType.SRV);
                _mdns.SendQuery(ptr.DomainName, type: DnsType.AAAA);
                _mdns.SendQuery(ptr.DomainName, type: DnsType.A);
            }
        }

        // Try to assemble from SRV + address records
        foreach (var record in e.Message.Answers.Concat(e.Message.AdditionalRecords))
        {
            if (record is SRVRecord srv)
            {
                var instanceName = srv.Name.ToString();
                if (!instanceName.Contains("_matter._tcp"))
                {
                    continue;
                }

                var parsed = ParseInstanceName(instanceName);
                if (parsed == null)
                {
                    continue;
                }

                // Find address record in the same message
                // Collect all addresses, prefer non-link-local IPv6, then IPv4, then link-local
                IPAddress? address;
                IPAddress? ipv4Addr = null;
                IPAddress? linkLocalAddr = null;
                IPAddress? globalV6Addr = null;
                foreach (var ar in e.Message.Answers.Concat(e.Message.AdditionalRecords))
                {
                    if (ar is AAAARecord aaaa && aaaa.Name == srv.Target)
                    {
                        if (aaaa.Address.IsIPv6LinkLocal)
                        {
                            linkLocalAddr ??= aaaa.Address;
                        }
                        else
                        {
                            globalV6Addr ??= aaaa.Address;
                        }
                    }
                    if (ar is ARecord a && a.Name == srv.Target)
                    {
                        ipv4Addr ??= a.Address;
                    }
                }
                // Priority: global IPv6 > IPv4 > link-local IPv6
                address = globalV6Addr ?? ipv4Addr ?? linkLocalAddr;

                if (address == null)
                {
                    // Query for the address separately
                    _mdns.SendQuery(srv.Target, type: DnsType.AAAA);
                    _mdns.SendQuery(srv.Target, type: DnsType.A);
                    continue;
                }

                var node = new OperationalNode
                {
                    InstanceName = instanceName,
                    CompressedFabricId = parsed.CompressedFabricId,
                    NodeId = parsed.NodeId,
                    Address = address,
                    Port = srv.Port,
                };

                _resolved[instanceName] = node;
                NodeDiscovered?.Invoke(node);
            }
        }
    }

    /// <summary>
    /// Parse the operational instance name: {compressedFabricId}-{nodeId}._matter._tcp.local.
    /// Both IDs are 16-char uppercase hex strings.
    /// </summary>
    private static OperationalInstanceName? ParseInstanceName(string name)
    {
        // Format: "XXXXXXXXXXXXXXXX-XXXXXXXXXXXXXXXX._matter._tcp.local."
        var dotIdx = name.IndexOf("._matter._tcp", StringComparison.Ordinal);
        if (dotIdx < 0)
        {
            return null;
        }

        var prefix = name[..dotIdx];
        var dashIdx = prefix.IndexOf('-');
        if (dashIdx < 0)
        {
            return null;
        }

        if (ulong.TryParse(prefix[..dashIdx], System.Globalization.NumberStyles.HexNumber, null, out var fabricId) &&
            ulong.TryParse(prefix[(dashIdx + 1)..], System.Globalization.NumberStyles.HexNumber, null, out var nodeId))
        {
            return new OperationalInstanceName(fabricId, nodeId);
        }

        return null;
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        _mdns.Stop();
        (_mdns as IDisposable)?.Dispose();
    }
}
