# DotMatter SDK Gap Matrix

This document tracks the gap between the **full Matter SDK feature footprint** and the **subset DotMatter currently treats as supported product/Core surface**.

Use it to answer a different question than the product support matrices:

- **Product/Core support matrices**: what DotMatter supports today
- **SDK gap matrix**: what still exists only as generated code, internal plumbing, partial support, or missing platform breadth

## Status legend

| Status | Meaning |
| --- | --- |
| Supported | Part of the current supported Core/product contract |
| Partial | Exists and works in some paths, but is not fully promoted/surfaced/tested as a supported feature band |
| Generated-only | Generated cluster/API surface exists, but it is not promoted as supported product/Core scope |
| Internal-only | Implementation detail present in the repo, not intended as the public supported path |
| Missing | Not implemented today |
| Removed | Intentionally deleted from the repo as obsolete architecture |
| Unsupported | Explicitly out of current support scope |

## Current generated-surface snapshot

| Area | Status | Notes |
| --- | --- | --- |
| Generated cluster files in `DotMatter.Core\Clusters` | Partial | Current repo snapshot generates **144** cluster files spanning both promoted and still-generated-only feature bands. |
| Raw generated `Task<object?>` / `Task<object[]?>` attribute readers | Supported | **0 remain in generated cluster output** after the latest CodeGen/parser fixes and regeneration; only shared internal base helpers still expose raw-read plumbing. |
| Generated cluster members routing through `UnsupportedAttributeAsync` / `UnsupportedCommandAsync` | Supported | **0 remain in generated cluster output**; the helper methods still exist only in `ClusterBase` as internal infrastructure. |
| Future unseen field-shape fallbacks in CodeGen | Internal-only | `DotMatter.CodeGen` intentionally keeps explicit fail-fast `NotSupportedException` guards for unseen writer/reader shapes introduced by future XML inputs. Those guards are generator safety rails, not a partially supported public feature. |

## SDK breadth matrix

| Feature band | Status | Current state |
| --- | --- | --- |
| PASE commissioning, CASE sessions, fabrics, certificates | Supported | Core and controller paths are implemented and end-to-end validated on the current lab. |
| Linux BLE commissioning via BlueZ | Supported | Current supported BLE commissioning runtime path. |
| OTBR-backed operational/Thread discovery | Supported | Supported discovery path for the current controller deployment model. |
| Commissionable-node mDNS browse helpers | Supported | Available through `DotMatter.Core.Discovery.CommissionableDiscovery`. |
| `ResilientSession` operational lifecycle | Supported | Current session/reconnect abstraction. |
| Generic interaction-model APIs (`MatterInteractions`) | Supported | Public reusable read/write/invoke/subscribe helpers exist. |
| Descriptor/topology helpers (`MatterTopology`) | Supported | Public endpoint/cluster/device-type discovery helpers exist. |
| Controller/admin helpers (`MatterAdministration`) | Supported | Commissioning-window state, fabric management, and commissioning-complete helpers exist. |
| ACL / Binding typed readers and writers | Supported | Supported and used by current binding/ACL flows. |
| Typed/raw Matter event reads and subscriptions | Supported | Supported in Core and surfaced in the controller product. |
| Device delete as true decommission | Supported | Default delete now removes the fabric on the node first, then local state. |
| Enhanced commissioning window through controller HTTP API | Supported | Controller now exposes the low-level enhanced-window route with explicit verifier/discriminator/iteration/salt inputs matching the Core contract. |
| `NetworkCommissioning` cluster family | Partial | Generated cluster exists, commissioning now reuses promoted typed Core helpers, controller read/mutation routes now exist, and deployed-controller lab validation succeeded for read, scan, idempotent interface-enabled write, and no-op reorder on a live Thread device. The API now also surfaces named invoke-status metadata for device-side rejections; live `AddOrUpdateThreadNetwork` and `ConnectNetwork` attempts returned `FailsafeRequired (0xCA)` outside active fail-safe/commissioning context. It remains partial because successful credential-changing mutation workflows are still context- and device-dependent rather than broadly validated product scope. |
| `Groups`, `ScenesManagement`, `GroupKeyManagement` | Partial | Promoted typed Core helpers and controller routes now exist for read/query workflows plus Groups, Group Key Management, and Scenes command families, and automated coverage is in place. Deployed-controller live validation on the current light/switch lab reached the new routes successfully, and all tested endpoints returned consistent `409` unsupported responses because those nodes do not host these clusters. The family remains partial because broader product support still needs hardware that actually implements the clusters, especially `ScenesManagement`. |
| OTA clusters (`OTASoftwareUpdateProvider`, `OTASoftwareUpdateRequestor`) | Generated-only | Generated APIs and event mapping exist, but product/Core support is not claimed. |
| Joint Fabric / broader multi-admin clusters | Generated-only | Generated wrappers exist; end-to-end supported controller workflows are not yet promoted. |
| Energy/appliance/device-specific cluster breadth beyond current lab needs | Generated-only | Large portions of the SDK surface compile and generate, but are not part of the current support contract. |
| Legacy `SessionManager` / `NodeRegister` / older `Node` lifecycle path | Removed | Deleted as obsolete architecture so `ResilientSession` is now the only remaining lifecycle story in active code. |
| Full platform-neutral discovery/commissioning stack | Unsupported | Current support remains tied to the controller/host assumptions documented in the Core matrix. |
| Windows BLE commissioning | Unsupported | Explicitly outside the current v1 support contract. |
| Windows hosting as a supported controller deployment target | Unsupported | Product target remains Linux ARM64 / Raspberry Pi. |

## What changed in this pass

The largest concrete generator/parsing gaps from the earlier audit are now closed:

- cluster-scoped shared structs defined in one XML file now merge correctly into related clusters generated from other XML files
- missing scalar aliases such as `int24u`, `int24s`, and `power_mvar` now map to numeric CLR types instead of falling back to `object`
- the generated cluster output no longer contains raw `object` attribute readers for the previously identified cases

That shifts the remaining SDK gap away from **"generated typing is still broken in visible places"** and toward **"breadth is present but not yet promoted/supported as product scope"**.

## Recommended next implementation order

1. Promote the next intentional feature band, not the whole SDK at once.
2. Start with whichever remaining band is actually product-relevant:
   - `Groups` / `Scenes` / `GroupKeyManagement`
   - OTA
   - multi-admin / Joint Fabric
3. Only broaden platform scope afterward if platform-neutral commissioning becomes a real goal.
