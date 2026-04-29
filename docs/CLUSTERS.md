# Generated Clusters

DotMatter includes ~130 auto-generated Matter cluster classes in `DotMatter.Core/Clusters/`.
These are generated from the official Matter XML cluster definitions using `DotMatter.CodeGen`.

## Tested & Actively Used

| Cluster | ID | Status |
|---------|----|--------|
| OnOff | 0x0006 | ✅ Full read/write/subscribe support |
| LevelControl | 0x0008 | ✅ Full read/write support |
| ColorControl | 0x0300 | ✅ Hue/Sat, XY, and Color Temp read/write |
| Descriptor | 0x001D | ✅ Used for endpoint discovery |
| BasicInformation | 0x0028 | ✅ Used for device metadata |
| GeneralCommissioning | 0x0030 | ✅ Used during commissioning |
| OperationalCredentials | 0x003E | ✅ Used during commissioning |
| NetworkCommissioning | 0x0031 | ✅ Used during commissioning |

## Generated But Untested

All other clusters (~120) are auto-generated with correct attribute IDs, command IDs, enums, and bitmaps, but have **not been tested against real hardware**. They should work via the generic `ClusterBase` infrastructure, but edge cases may exist.

## Adding Support for a New Cluster

1. The generated class already exists in `DotMatter.Core/Clusters/`
2. Instantiate it with an active `ISession` and endpoint ID:
   ```csharp
   var cluster = new ThermostatCluster(session, endpointId: 1);
   var temp = await cluster.ReadLocalTemperatureAsync();
   ```
3. Add command handling in your `MatterDeviceHost` subclass

## Re-generating Clusters

```bash
dotnet run --project DotMatter.CodeGen
```

This reads cluster XML definitions from `DotMatter.CodeGen/Xml/` and outputs `.g.cs` files to `DotMatter.Core/Clusters/`.
