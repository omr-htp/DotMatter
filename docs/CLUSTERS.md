# Generated Clusters

DotMatter currently includes **141** auto-generated Matter cluster classes in `DotMatter.Core/Clusters/`.
These are generated from the official Matter XML cluster definitions using `DotMatter.CodeGen`.

For the current support-vs-SDK-breadth picture, see:

- `docs/DOTMATTER_CORE_SUPPORT_MATRIX.md`
- `docs/DOTMATTER_PRODUCT_SUPPORT_MATRIX.md`
- `docs/DOTMATTER_SDK_GAP_MATRIX.md`

## Tested & Actively Used

| Cluster | ID | Status |
|---------|----|--------|
| OnOff | 0x0006 | ✅ Full read/write/subscribe support |
| LevelControl | 0x0008 | ✅ Full read/write support |
| ColorControl | 0x0300 | ✅ Hue/Sat, XY, and Color Temp read/write |
| AccessControl | 0x001F | ✅ ACL read/write for commissioning and switch binding |
| Binding | 0x001E | ✅ Binding list read/write for switch OnOff binding |
| Descriptor | 0x001D | ✅ Used for endpoint discovery |
| BasicInformation | 0x0028 | ✅ Used for device metadata |
| GeneralCommissioning | 0x0030 | ✅ Used during commissioning |
| OperationalCredentials | 0x003E | ✅ Used during commissioning |
| NetworkCommissioning | 0x0031 | ✅ Used during commissioning and now surfaced through promoted controller/Core read and mutation helpers |
| Groups | 0x0004 | ⚠️ Promoted through typed Core/controller query and command workflows; current light/switch lab returns live `409` unsupported because those nodes do not host the cluster |
| GroupKeyManagement | 0x003F | ⚠️ Promoted through typed Core/controller query and command workflows; current light/switch lab returns live `409` unsupported because those nodes do not host the cluster |
| ScenesManagement | 0x0062 | ⚠️ Promoted through typed Core/controller query and command workflows; current light/switch lab returns live `409` unsupported and still lacks scene-capable hardware validation |

## Generated But Not Automatically Supported

Much more of the Matter SDK footprint now generates cleanly than DotMatter officially supports as product scope.

- The current generated output has **no remaining raw `Task<object?>` / `Task<object[]?>` attribute readers**.
- That does **not** mean every generated cluster is part of the supported contract.
- Large feature bands still remain **generated-only** until they are intentionally promoted through docs, controller surfacing, and real-device validation.

Use `docs/DOTMATTER_SDK_GAP_MATRIX.md` as the source of truth for that distinction.

## Writable Fabric-Scoped Lists

`AccessControlCluster` and `BindingCluster` include generated typed readers and timed writers for the fabric-scoped list attributes used by real switch binding flows:

```csharp
var accessControl = new AccessControlCluster(targetSession, endpointId: 0);
AccessControlCluster.AccessControlEntryStruct[] acl = await accessControl.ReadACLAsync(ct) ?? [];
WriteResponse aclWrite = await accessControl.WriteACLAsync(acl, timedRequest: true, timedTimeoutMs: 5000, ct: ct);

var binding = new BindingCluster(switchSession, endpointId: 1);
BindingCluster.TargetStruct[] targets = await binding.ReadBindingAsync(ct) ?? [];
WriteResponse bindingWrite = await binding.WriteBindingAsync(targets, timedRequest: true, timedTimeoutMs: 5000, ct: ct);
```

These helpers write the complete list attribute value. Callers should read the current list, preserve unrelated entries, apply the desired entry, and then write the updated list. ACL and Binding reads remain fabric-filtered by the device.

## Adding Support for a New Cluster

1. The generated class already exists in `DotMatter.Core/Clusters/`
2. Instantiate it with an active `ISession` and endpoint ID:
   ```csharp
   var cluster = new ThermostatCluster(session, endpointId: 1);
   var temp = await cluster.ReadLocalTemperatureAsync();
   ```
3. Add command handling in your `MatterDeviceHost` subclass
4. If the generated output is missing a needed reader/writer, update `DotMatter.CodeGen` and regenerate; do not patch `.g.cs` files by hand.

## Re-generating Clusters

```bash
dotnet run --project DotMatter.CodeGen
```

This reads cluster XML definitions from `DotMatter.CodeGen/Xml/` and outputs `.g.cs` files to `DotMatter.Core/Clusters/`.
