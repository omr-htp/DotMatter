using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using DotMatter.Core.Mdns;

namespace DotMatter.Core.Discovery;

/// <summary>Commissionable-node information discovered through mDNS browse responses.</summary>
public sealed class CommissionableNode(
    string instanceName,
    string fullyQualifiedName,
    ushort port,
    IReadOnlyList<IPAddress> addresses,
    IReadOnlyDictionary<string, string> txtRecords,
    ushort? longDiscriminator,
    ushort? vendorId,
    ushort? productId,
    uint? deviceType,
    string? deviceName,
    byte? commissioningMode,
    ushort? pairingHint,
    string? pairingInstruction,
    string? rotatingIdentifier)
{
    /// <summary>Service instance label without the service suffix.</summary>
    public string InstanceName { get; } = instanceName;

    /// <summary>Fully-qualified service instance name.</summary>
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    /// <summary>Advertised commissioning port.</summary>
    public ushort Port { get; } = port;

    /// <summary>Advertised IPv4/IPv6 addresses.</summary>
    public IReadOnlyList<IPAddress> Addresses { get; } = addresses;

    /// <summary>Raw TXT records keyed by Matter TXT field name.</summary>
    public IReadOnlyDictionary<string, string> TxtRecords { get; } = txtRecords;

    /// <summary>Long discriminator from TXT field <c>D</c>, when present.</summary>
    public ushort? LongDiscriminator { get; } = longDiscriminator;

    /// <summary>Vendor ID parsed from TXT field <c>VP</c>, when present.</summary>
    public ushort? VendorId { get; } = vendorId;

    /// <summary>Product ID parsed from TXT field <c>VP</c>, when present.</summary>
    public ushort? ProductId { get; } = productId;

    /// <summary>Device type parsed from TXT field <c>DT</c>, when present.</summary>
    public uint? DeviceType { get; } = deviceType;

    /// <summary>Device name parsed from TXT field <c>DN</c>, when present.</summary>
    public string? DeviceName { get; } = deviceName;

    /// <summary>Commissioning mode parsed from TXT field <c>CM</c>, when present.</summary>
    public byte? CommissioningMode { get; } = commissioningMode;

    /// <summary>Pairing hint parsed from TXT field <c>PH</c>, when present.</summary>
    public ushort? PairingHint { get; } = pairingHint;

    /// <summary>Pairing instruction parsed from TXT field <c>PI</c>, when present.</summary>
    public string? PairingInstruction { get; } = pairingInstruction;

    /// <summary>Rotating identifier parsed from TXT field <c>RI</c>, when present.</summary>
    public string? RotatingIdentifier { get; } = rotatingIdentifier;
}

/// <summary>Public browse/resolve helpers for commissionable Matter nodes.</summary>
public sealed class CommissionableDiscovery : IDisposable
{
    private static readonly DomainName _commissionableServiceName = new("_matterc._udp");
    private const string CommissionableServiceSuffix = "._matterc._udp.local";

    private readonly IMulticastService _mdns;
    private readonly ServiceDiscovery _serviceDiscovery;
    private readonly bool _ownsMulticastService;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CommissionableNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Creates a discovery instance that owns its multicast service.</summary>
    public CommissionableDiscovery()
        : this(new MulticastService(), ownsMulticastService: true)
    {
    }

    /// <summary>Creates a discovery instance over a caller-provided multicast service.</summary>
    public CommissionableDiscovery(IMulticastService mdns)
        : this(mdns, ownsMulticastService: false)
    {
    }

    private CommissionableDiscovery(IMulticastService mdns, bool ownsMulticastService)
    {
        ArgumentNullException.ThrowIfNull(mdns);

        _mdns = mdns;
        _ownsMulticastService = ownsMulticastService;
        _serviceDiscovery = new ServiceDiscovery(mdns);
        _serviceDiscovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        _mdns.Start();
    }

    /// <summary>Raised when a new or updated commissionable node is discovered.</summary>
    public event Action<CommissionableNode>? NodeDiscovered;

    /// <summary>Returns the currently known commissionable nodes.</summary>
    public IReadOnlyList<CommissionableNode> GetKnownNodes()
    {
        lock (_lock)
        {
            return _nodes.Values
                .OrderBy(static node => node.InstanceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Browse for commissionable nodes during the given timeout window.</summary>
    public async Task<IReadOnlyList<CommissionableNode>> BrowseAsync(
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _serviceDiscovery.QueryServiceInstances(_commissionableServiceName);
        await Task.Delay(timeout ?? TimeSpan.FromSeconds(3), ct);
        return GetKnownNodes();
    }

    /// <summary>Resolve the first commissionable node that matches the supplied discriminator.</summary>
    public async Task<CommissionableNode?> ResolveByDiscriminatorAsync(
        ushort discriminator,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var existing = FindMatch(discriminator);
        if (existing is not null)
        {
            return existing;
        }

        var nodes = await BrowseAsync(timeout, ct);
        return nodes.FirstOrDefault(node => MatchesDiscriminator(node, discriminator));
    }

    private CommissionableNode? FindMatch(ushort discriminator)
    {
        lock (_lock)
        {
            return _nodes.Values.FirstOrDefault(node => MatchesDiscriminator(node, discriminator));
        }
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        if (!TryCreateNode(e, out var node))
        {
            return;
        }

        lock (_lock)
        {
            _nodes[node.FullyQualifiedName] = node;
        }

        NodeDiscovered?.Invoke(node);
    }

    private static bool TryCreateNode(ServiceInstanceDiscoveryEventArgs e, out CommissionableNode node)
    {
        node = null!;

        var fullyQualifiedName = e.ServiceInstanceName.ToString();
        if (!IsCommissionableServiceInstance(fullyQualifiedName))
        {
            return false;
        }

        var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToArray();
        var srv = records
            .OfType<SRVRecord>()
            .FirstOrDefault(record => NamesEqual(record.Name.ToString(), fullyQualifiedName));
        if (srv is null)
        {
            return false;
        }

        var txt = records
            .OfType<TXTRecord>()
            .FirstOrDefault(record => NamesEqual(record.Name.ToString(), fullyQualifiedName));

        var txtRecords = ParseTxtRecords(txt?.Strings);
        ParseVendorProduct(txtRecords, out var vendorId, out var productId);

        var addresses = records
            .OfType<AddressRecord>()
            .Where(record => NamesEqual(record.Name.ToString(), srv.Target.ToString()))
            .Select(record => record.Address)
            .Distinct()
            .OrderByDescending(static address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal)
            .ThenByDescending(static address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToArray();

        node = new CommissionableNode(
            GetInstanceName(fullyQualifiedName),
            fullyQualifiedName,
            srv.Port,
            addresses,
            new ReadOnlyDictionary<string, string>(txtRecords),
            TryGetUInt16(txtRecords, "D"),
            vendorId,
            productId,
            TryGetUInt32(txtRecords, "DT"),
            TryGetString(txtRecords, "DN"),
            TryGetByte(txtRecords, "CM"),
            TryGetUInt16(txtRecords, "PH"),
            TryGetString(txtRecords, "PI"),
            TryGetString(txtRecords, "RI"));
        return true;
    }

    private static Dictionary<string, string> ParseTxtRecords(IEnumerable<string>? strings)
    {
        var records = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (strings is null)
        {
            return records;
        }

        foreach (var entry in strings)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var separator = entry.IndexOf('=');
            if (separator < 0)
            {
                records[entry.Trim()] = string.Empty;
                continue;
            }

            var key = entry[..separator].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            records[key] = entry[(separator + 1)..].Trim();
        }

        return records;
    }

    private static void ParseVendorProduct(
        Dictionary<string, string> txtRecords,
        out ushort? vendorId,
        out ushort? productId)
    {
        vendorId = null;
        productId = null;

        if (!txtRecords.TryGetValue("VP", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var parts = value.Split('+', 2, StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVendorId))
        {
            vendorId = parsedVendorId;
        }

        if (parts.Length == 2 && ushort.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedProductId))
        {
            productId = parsedProductId;
        }
    }

    private static ushort? TryGetUInt16(Dictionary<string, string> txtRecords, string key)
        => txtRecords.TryGetValue(key, out var value)
            && ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static uint? TryGetUInt32(Dictionary<string, string> txtRecords, string key)
        => txtRecords.TryGetValue(key, out var value)
            && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static byte? TryGetByte(Dictionary<string, string> txtRecords, string key)
        => txtRecords.TryGetValue(key, out var value)
            && byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static string? TryGetString(Dictionary<string, string> txtRecords, string key)
        => txtRecords.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool MatchesDiscriminator(CommissionableNode node, ushort discriminator)
    {
        if (!node.LongDiscriminator.HasValue)
        {
            return false;
        }

        return discriminator <= 0x000F
            ? (node.LongDiscriminator.Value >> 8) == discriminator
            : node.LongDiscriminator.Value == discriminator;
    }

    private static bool IsCommissionableServiceInstance(string name)
        => name.EndsWith(CommissionableServiceSuffix, StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(CommissionableServiceSuffix + ".", StringComparison.OrdinalIgnoreCase);

    private static string GetInstanceName(string fullyQualifiedName)
    {
        var suffixIndex = fullyQualifiedName.IndexOf(CommissionableServiceSuffix, StringComparison.OrdinalIgnoreCase);
        return suffixIndex > 0
            ? fullyQualifiedName[..suffixIndex]
            : fullyQualifiedName.TrimEnd('.');
    }

    private static bool NamesEqual(string left, string right)
        => string.Equals(left.TrimEnd('.'), right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _serviceDiscovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
        _serviceDiscovery.Dispose();
        if (_ownsMulticastService)
        {
            _mdns.Dispose();
        }

        _disposed = true;
    }
}
