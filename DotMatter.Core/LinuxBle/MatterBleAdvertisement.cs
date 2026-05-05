namespace DotMatter.Core.LinuxBle;

/// <summary>One actively advertising Matter BLE commissionable device observed through BlueZ.</summary>
public sealed record MatterBleAdvertisement(
    string BluetoothAddress,
    string? Name,
    short? Rssi,
    ushort LongDiscriminator,
    ushort ShortDiscriminator,
    ushort? VendorId,
    ushort? ProductId,
    byte AdvertisementVersion,
    byte? AdditionalDataFlag,
    string ServiceDataHex);
