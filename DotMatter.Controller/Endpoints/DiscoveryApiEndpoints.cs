using System.Globalization;
using DotMatter.Controller.Matter;

namespace DotMatter.Controller.Endpoints;

internal static class DiscoveryApiEndpoints
{
    private const int DefaultBrowseTimeoutMs = 3000;
    private const int DefaultResolveTimeoutMs = 5000;
    private const int MaxTimeoutMs = 30000;
    private const int MaxLimit = 256;

    internal static void MapDiscoveryApiEndpoints(this RouteGroupBuilder api)
    {
        var discovery = api.MapGroup("/discovery").WithTags("Discovery");

        discovery.MapGet("/commissionable", async (
            int? timeoutMs,
            string? transport,
            string? discriminator,
            string? vendorId,
            string? productId,
            string? deviceType,
            byte? commissioningMode,
            string? deviceNameContains,
            string? instanceNameContains,
            string? rotatingIdentifierContains,
            int? limit,
            bool? includeTxtRecords,
            ICommissionableDeviceDiscoveryService discoveryService,
            CancellationToken ct) =>
        {
            if (!TryParseTimeout(timeoutMs, DefaultBrowseTimeoutMs, out var timeout, out var timeoutError))
            {
                return Results.BadRequest(new ErrorResponse(timeoutError!));
            }

            if (!TryParseOptionalUShort(discriminator, 0x0FFF, "Discriminator", out var parsedDiscriminator, out var discriminatorError))
            {
                return Results.BadRequest(new ErrorResponse(discriminatorError!));
            }

            if (!TryParseOptionalUShort(vendorId, ushort.MaxValue, "VendorId", out var parsedVendorId, out var vendorError))
            {
                return Results.BadRequest(new ErrorResponse(vendorError!));
            }

            if (!TryParseOptionalUShort(productId, ushort.MaxValue, "ProductId", out var parsedProductId, out var productError))
            {
                return Results.BadRequest(new ErrorResponse(productError!));
            }

            if (!TryParseOptionalUInt(deviceType, "DeviceType", out var parsedDeviceType, out var deviceTypeError))
            {
                return Results.BadRequest(new ErrorResponse(deviceTypeError!));
            }

            if (limit is <= 0 or > MaxLimit)
            {
                return Results.BadRequest(new ErrorResponse($"limit must be between 1 and {MaxLimit}"));
            }

            if (!TryParseTransport(transport, out var parsedTransport, out var transportError))
            {
                return Results.BadRequest(new ErrorResponse(transportError!));
            }

            var request = new CommissionableDeviceDiscoveryRequest(
                Timeout: timeout,
                Transport: parsedTransport,
                Discriminator: parsedDiscriminator,
                VendorId: parsedVendorId,
                ProductId: parsedProductId,
                DeviceType: parsedDeviceType,
                CommissioningMode: commissioningMode,
                DeviceNameContains: NullIfWhiteSpace(deviceNameContains),
                InstanceNameContains: NullIfWhiteSpace(instanceNameContains),
                RotatingIdentifierContains: NullIfWhiteSpace(rotatingIdentifierContains),
                Limit: limit,
                IncludeTxtRecords: includeTxtRecords ?? false);

            var response = await discoveryService.BrowseAsync(request, ct);
            return Results.Ok(response);
        })
            .WithSummary("Browse commissionable devices")
            .WithDescription("Browses current Matter commissionable advertisements over mDNS, BLE, or both and returns typed discovery details with optional filters for discriminator, vendor, product, device type, commissioning mode, names, and rotating identifier.");

        discovery.MapGet("/commissionable/resolve", async (
            string discriminator,
            int? timeoutMs,
            string? transport,
            bool? includeTxtRecords,
            ICommissionableDeviceDiscoveryService discoveryService,
            CancellationToken ct) =>
        {
            if (!TryParseTimeout(timeoutMs, DefaultResolveTimeoutMs, out var timeout, out var timeoutError))
            {
                return Results.BadRequest(new ErrorResponse(timeoutError!));
            }

            if (!TryParseRequiredUShort(discriminator, 0x0FFF, "Discriminator", out var parsedDiscriminator, out var discriminatorError))
            {
                return Results.BadRequest(new ErrorResponse(discriminatorError!));
            }

            if (!TryParseTransport(transport, out var parsedTransport, out var transportError))
            {
                return Results.BadRequest(new ErrorResponse(transportError!));
            }

            var request = new CommissionableDeviceResolveRequest(
                Discriminator: parsedDiscriminator,
                Timeout: timeout,
                Transport: parsedTransport,
                IncludeTxtRecords: includeTxtRecords ?? false);
            var device = await discoveryService.ResolveAsync(request, ct);
            if (device is null)
            {
                return Results.NotFound(new ErrorResponse($"No advertising commissionable device matched discriminator {FormatHex(parsedDiscriminator)}"));
            }

            return Results.Ok(new CommissionableDeviceResolveResponse(
                Discriminator: FormatHex(parsedDiscriminator),
                BrowseWindowMs: (int)Math.Round(timeout.TotalMilliseconds),
                Device: device));
        })
            .WithSummary("Resolve one commissionable device")
            .WithDescription("Resolves the first currently advertising commissionable mDNS node whose discriminator matches the supplied short (0x0-0xF) or long (0x0000-0x0FFF) discriminator.");
    }

    private static bool TryParseTimeout(int? timeoutMs, int defaultTimeoutMs, out TimeSpan timeout, out string? error)
    {
        timeout = TimeSpan.Zero;
        error = null;
        var value = timeoutMs ?? defaultTimeoutMs;
        if (value <= 0 || value > MaxTimeoutMs)
        {
            error = $"timeoutMs must be between 1 and {MaxTimeoutMs}";
            return false;
        }

        timeout = TimeSpan.FromMilliseconds(value);
        return true;
    }

    private static bool TryParseRequiredUShort(string value, ushort maxValue, string name, out ushort parsed, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = default;
            error = $"{name} is required";
            return false;
        }

        if (!TryParseOptionalUShort(value, maxValue, name, out var parsedOptional, out error) || !parsedOptional.HasValue)
        {
            parsed = default;
            return false;
        }

        parsed = parsedOptional.Value;
        return true;
    }

    private static bool TryParseOptionalUShort(string? value, ushort maxValue, string name, out ushort? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseHexOrDecimalUInt(value, out var raw) || raw > maxValue)
        {
            error = $"{name} must be a hex or decimal value between 0 and {maxValue}";
            return false;
        }

        parsed = (ushort)raw;
        return true;
    }

    private static bool TryParseOptionalUInt(string? value, string name, out uint? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseHexOrDecimalUInt(value, out var raw))
        {
            error = $"{name} must be a valid hex or decimal value";
            return false;
        }

        parsed = raw;
        return true;
    }

    private static bool TryParseHexOrDecimalUInt(string value, out uint parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseTransport(string? value, out CommissionableDiscoveryTransport transport, out string? error)
    {
        transport = CommissionableDiscoveryTransport.All;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "all":
                transport = CommissionableDiscoveryTransport.All;
                return true;
            case "mdns":
                transport = CommissionableDiscoveryTransport.Mdns;
                return true;
            case "ble":
            case "bluetooth":
                transport = CommissionableDiscoveryTransport.Ble;
                return true;
            default:
                error = "transport must be one of: all, mdns, ble";
                return false;
        }
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatHex(ushort value)
        => $"0x{value.ToString("X3", CultureInfo.InvariantCulture)}";
}
