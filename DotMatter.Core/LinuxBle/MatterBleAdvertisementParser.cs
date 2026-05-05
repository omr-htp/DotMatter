using Tmds.DBus.Protocol;

namespace DotMatter.Core.LinuxBle;

internal static class MatterBleAdvertisementParser
{
    private const string MatterBleServiceUuidFragment = "fff6";

    internal static bool CheckMatterDiscriminator(
        Dictionary<string, VariantValue> deviceProps,
        int discriminator,
        bool isShort = false)
    {
        if (!TryParse(deviceProps, out var advertisement))
        {
            return false;
        }

        return isShort
            ? advertisement.ShortDiscriminator == discriminator
            : advertisement.LongDiscriminator == discriminator;
    }

    internal static bool HasMatterServiceData(Dictionary<string, VariantValue> deviceProps)
        => TryGetMatterServiceData(deviceProps, out _);

    internal static bool TryParse(
        Dictionary<string, VariantValue> deviceProps,
        out MatterBleAdvertisement advertisement)
    {
        advertisement = null!;
        if (!TryGetMatterServiceData(deviceProps, out var data) || data.Length < 3)
        {
            return false;
        }

        var opCode = data[0];
        if (opCode != 0x00)
        {
            return false;
        }

        var discAndVersion = (ushort)(data[1] | (data[2] << 8));
        var longDiscriminator = (ushort)(discAndVersion & 0x0FFF);
        var advertisementVersion = (byte)((discAndVersion >> 12) & 0x0F);
        var vendorId = data.Length >= 5 ? (ushort?)(ushort)(data[3] | (data[4] << 8)) : null;
        var productId = data.Length >= 7 ? (ushort?)(ushort)(data[5] | (data[6] << 8)) : null;
        byte? additionalDataFlag = data.Length >= 8 ? data[7] : null;
        var bluetoothAddress = TryGetString(deviceProps, "Address");
        if (string.IsNullOrWhiteSpace(bluetoothAddress))
        {
            return false;
        }

        advertisement = new MatterBleAdvertisement(
            BluetoothAddress: bluetoothAddress,
            Name: TryGetString(deviceProps, "Name") ?? TryGetString(deviceProps, "Alias"),
            Rssi: TryGetInt16(deviceProps, "RSSI"),
            LongDiscriminator: longDiscriminator,
            ShortDiscriminator: (ushort)(longDiscriminator >> 8),
            VendorId: vendorId,
            ProductId: productId,
            AdvertisementVersion: advertisementVersion,
            AdditionalDataFlag: additionalDataFlag,
            ServiceDataHex: Convert.ToHexString(data));
        return true;
    }

    private static bool TryGetMatterServiceData(
        Dictionary<string, VariantValue> deviceProps,
        out byte[] data)
    {
        data = [];
        if (!deviceProps.TryGetValue("ServiceData", out var serviceDataVariant))
        {
            return false;
        }

        try
        {
            var serviceData = serviceDataVariant.GetDictionary<string, VariantValue>();
            foreach (var (uuid, value) in serviceData)
            {
                if (!uuid.Contains(MatterBleServiceUuidFragment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                data = value.GetArray<byte>();
                return data.Length > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string? TryGetString(Dictionary<string, VariantValue> deviceProps, string key)
    {
        if (!deviceProps.TryGetValue(key, out var value))
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static short? TryGetInt16(Dictionary<string, VariantValue> deviceProps, string key)
    {
        if (!deviceProps.TryGetValue(key, out var value))
        {
            return null;
        }

        try
        {
            return value.GetInt16();
        }
        catch
        {
            try
            {
                return checked((short)value.GetInt32());
            }
            catch
            {
                return null;
            }
        }
    }
}
