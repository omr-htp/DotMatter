using System.Net;
using System.Net.Sockets;
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

    private readonly IMulticastService _mdns;
    private readonly ServiceDiscovery _sd;
    private readonly bool _ownsMulticastService;
    private readonly Dictionary<string, OperationalNode> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingOperationalNode> _pendingByInstance = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IPAddress>> _addressesByTarget = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    private sealed record OperationalInstanceName(ulong CompressedFabricId, ulong NodeId);

    private sealed class PendingOperationalNode
    {
        public required string InstanceName { get; init; }
        public required ulong CompressedFabricId { get; init; }
        public required ulong NodeId { get; init; }
        public string? TargetName { get; set; }
        public int? Port { get; set; }
        public List<IPAddress> Addresses { get; } = [];
    }

    /// <summary>Raised when a new Matter operational node is discovered.</summary>
    public event Action<OperationalNode>? NodeDiscovered;

    /// <summary>OperationalDiscovery.</summary>
    public OperationalDiscovery()
        : this(new MulticastService(), ownsMulticastService: true)
    {
    }

    /// <summary>OperationalDiscovery.</summary>
    public OperationalDiscovery(IMulticastService mdns)
        : this(mdns, ownsMulticastService: false)
    {
    }

    private OperationalDiscovery(IMulticastService mdns, bool ownsMulticastService)
    {
        _mdns = mdns;
        _ownsMulticastService = ownsMulticastService;
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

        _mdns.NetworkInterfaceDiscovered += OnNetworkInterfaceDiscovered;
        _mdns.AnswerReceived += OnAnswerReceived;
        _mdns.Start();
        _mdns.SendQuery(OperationalService, type: DnsType.PTR);
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
        var instanceName = NormalizeName($"{compressedFabricId:X16}-{nodeId:X16}.{OperationalService}");

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

    private void OnNetworkInterfaceDiscovered(object? sender, NetworkInterfaceEventArgs e)
    {
        _mdns.SendQuery(OperationalService, type: DnsType.PTR);
    }

    private void OnAnswerReceived(object? sender, MessageEventArgs e)
    {
        var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToArray();

        foreach (var record in records)
        {
            if (record is PTRRecord ptr && NamesEqual(ptr.Name.ToString(), OperationalService))
            {
                _mdns.SendQuery(ptr.DomainName, type: DnsType.SRV);
                _mdns.SendQuery(ptr.DomainName, type: DnsType.AAAA);
                _mdns.SendQuery(ptr.DomainName, type: DnsType.A);
            }
        }

        var addressTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addressRecord in records.OfType<AddressRecord>())
        {
            var targetName = NormalizeName(addressRecord.Name.ToString());
            addressTargets.Add(targetName);
            if (!_addressesByTarget.TryGetValue(targetName, out var addresses))
            {
                addresses = [];
                _addressesByTarget[targetName] = addresses;
            }

            if (!addresses.Contains(addressRecord.Address))
            {
                addresses.Add(addressRecord.Address);
            }

            foreach (var pending in _pendingByInstance.Values)
            {
                if (pending.TargetName is not null && NamesEqual(pending.TargetName, targetName)
                    && !pending.Addresses.Contains(addressRecord.Address))
                {
                    pending.Addresses.Add(addressRecord.Address);
                }
            }
        }

        foreach (var pending in _pendingByInstance.Values)
        {
            if (pending.TargetName is not null && addressTargets.Contains(pending.TargetName))
            {
                TryPublish(pending);
            }
        }

        foreach (var srv in records.OfType<SRVRecord>())
        {
            var instanceName = NormalizeName(srv.Name.ToString());
            if (!instanceName.Contains("_matter._tcp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = ParseInstanceName(instanceName);
            if (parsed == null)
            {
                continue;
            }

            if (!_pendingByInstance.TryGetValue(instanceName, out var pending))
            {
                pending = new PendingOperationalNode
                {
                    InstanceName = instanceName,
                    CompressedFabricId = parsed.CompressedFabricId,
                    NodeId = parsed.NodeId,
                };
                _pendingByInstance[instanceName] = pending;
            }

            pending.TargetName = NormalizeName(srv.Target.ToString());
            pending.Port = srv.Port;
            if (_addressesByTarget.TryGetValue(pending.TargetName, out var cachedAddresses))
            {
                foreach (var address in cachedAddresses)
                {
                    if (!pending.Addresses.Contains(address))
                    {
                        pending.Addresses.Add(address);
                    }
                }
            }

            _mdns.SendQuery(srv.Target, type: DnsType.AAAA);
            _mdns.SendQuery(srv.Target, type: DnsType.A);
            TryPublish(pending);
        }
    }

    private void TryPublish(PendingOperationalNode pending)
    {
        if (!pending.Port.HasValue)
        {
            return;
        }

        var address = SelectPreferredAddress(pending.Addresses);
        if (address is null)
        {
            return;
        }

        var node = new OperationalNode
        {
            InstanceName = pending.InstanceName,
            CompressedFabricId = pending.CompressedFabricId,
            NodeId = pending.NodeId,
            Address = address,
            Port = pending.Port.Value,
        };

        _resolved[pending.InstanceName] = node;
        NodeDiscovered?.Invoke(node);
    }

    private static IPAddress? SelectPreferredAddress(IEnumerable<IPAddress> addresses)
        => addresses
            .OrderBy(static address => address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal ? 0 : 1)
            .ThenBy(static address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .FirstOrDefault();

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

    private static string NormalizeName(string name)
        => name.TrimEnd('.');

    private static bool NamesEqual(string left, string right)
        => string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        _mdns.NetworkInterfaceDiscovered -= OnNetworkInterfaceDiscovered;
        _mdns.AnswerReceived -= OnAnswerReceived;
        _sd.Dispose();
        if (_ownsMulticastService)
        {
            _mdns.Stop();
            _mdns.Dispose();
        }
    }
}
