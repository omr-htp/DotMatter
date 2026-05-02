# DotMatter Product Support Matrix

`DotMatter` v1 is the full controller product, not every possible Matter artifact in the repository. Support is defined by the runtime environment, tested flows, and documented API surface below.

## Supported for v1

### Runtime platform

| Area | Status | Notes |
| --- | --- | --- |
| Linux ARM64 / Raspberry Pi | Supported | Primary deployment target. |
| BlueZ BLE commissioning | Supported | Required for BLE commissioning flows. |
| OTBR-backed Thread discovery | Supported | `ot-ctl` integration is the supported discovery path for Thread devices. |
| Native AOT publish on Linux ARM64 | Supported | Must be produced on a Linux host because cross-OS native compilation is not supported by .NET. |

### Product flows

| Area | Status | Notes |
| --- | --- | --- |
| BLE commissioning for current working devices | Supported | Includes PASE, CSR/NOC exchange, registry persistence, and follow-up connect. |
| Session recovery and reconnect for commissioned devices | Supported | Bounded background work, explicit shutdown, and stale subscription recovery are part of the contract. |
| Lighting and current device-control flows | Supported | On/Off, Level, and Color operations used by the host are in scope for v1. |
| Switch OnOff binding flow | Supported | Writes target ACL and source Binding entries for devices on the same controller fabric. |
| Matter event inspection for live testing | Supported | Includes one-shot device event reads and a dedicated Matter SSE stream separate from controller state-change events, with typed payload JSON when DotMatter has generated support. |
| WiFi commissioning path | Supported | Requires explicit WiFi SSID and password input. |

### Controller API

| Area | Status | Notes |
| --- | --- | --- |
| Authenticated LAN API | Supported | API key required by default for non-health endpoints. |
| `GET /api/acls` | Supported | Aggregates live AccessControl ACL reads across known devices on a controller fabric; returns per-device errors. |
| `GET /api/devices/{id}/acl` | Supported | Reads one device's endpoint-0 AccessControl ACL list. |
| `GET /api/bindings` | Supported | Aggregates live Binding cluster reads across known devices on a controller fabric; returns per-source errors. |
| `GET /api/devices/{id}/bindings` | Supported | Reads one source device endpoint's fabric-scoped Binding list. |
| `GET /api/devices/{id}/matter/events` | Supported for live testing | Reads Matter event envelopes from one device for a selected cluster and optional event ID, including typed payload JSON when available. |
| `POST /api/devices/{id}/bindings/onoff` | Supported | Binds a switch endpoint to a target OnOff endpoint after both devices are commissioned and reachable. |
| `POST /api/devices/{id}/bindings/onoff/remove` | Supported | Removes the switch OnOff route and conservatively preserves broader/manual ACL entries when they should not be deleted. |
| `POST /api/devices/{id}/bindings/remove` | Supported | Removes matching Binding entries from one source endpoint using explicit request-body match criteria. |
| `POST /api/devices/{id}/acl/remove` | Supported | Removes matching ACL entries from endpoint 0 using explicit request-body match criteria. |
| `GET /api/matter/events` | Supported for live testing | Dedicated authenticated SSE stream for Matter event envelopes observed from discovery-driven subscribed devices, including typed payload JSON when available. |
| `GET /api/system/runtime` | Supported | Returns an authenticated safe runtime snapshot with readiness, device counts, and in-process diagnostics counters. |
| `GET /api/system/diagnostics` | Supported when explicitly enabled | Disabled by default; returns a more detailed authenticated runtime/configuration snapshot when `Controller__Diagnostics__EnableDetailedRuntimeEndpoint=true`. |
| Health endpoints | Supported | `/health`, `/health/live`, and `/health/ready` remain anonymous. |
| CORS allowlist | Supported | Disabled by default unless explicit origins are configured. |
| Query-string API key auth | Unsupported | Header-based auth is the supported path. |
| Direct public HTTP exposure | Unsupported | v1 expects trusted LAN or reverse-proxy/TLS termination in front of the controller. |

### Generated APIs

| Area | Status | Notes |
| --- | --- | --- |
| Generated members used by the controller product flows | Supported | Must be covered by product tests and supported docs. |
| Generated ACL and Binding readers/writers | Supported | Required for commissioning ACL verification and switch binding. |
| Generated APIs with missing serializer/parser support | Unsupported | They may compile, but they are not part of the product contract. |
| Entire generated cluster surface | Unsupported | Support is narrower than the generated footprint. |

## Explicitly unsupported for v1

- Windows hosting as a supported controller deployment target
- Raw unauthenticated LAN control as the default operating mode
- In-process public TLS termination as part of the appliance contract
- Half-supported legacy BTP or commented BLE commissioner paths
- Any generated cluster feature that lacks verified end-to-end support

## Operational assumptions

- `DotMatter.Controller` runs under a dedicated non-root `dotmatter` service account.
- OTBR and `ot-ctl` are installed and reachable on the target host.
- Runtime state lives under `/var/lib/.dot-matter`.
- TLS is supplied by deployment architecture, typically via reverse proxy on the trusted LAN.
