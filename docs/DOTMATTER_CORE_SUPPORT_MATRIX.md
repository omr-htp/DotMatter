# DotMatter.Core Support Matrix

## Supported for v1

`DotMatter.Core` is supported as a reusable controller platform for the currently working commissioning, session-establishment, and device-control path. The supported surface is intentionally narrower than the generated code footprint.

### Transports

| Area | Status | Notes |
| --- | --- | --- |
| UDP operational transport | Supported | Required for CASE sessions and operational cluster traffic. |
| Linux BLE / BlueZ commissioning transport | Supported | Primary commissioning runtime path for v1. Must not remove unrelated cached BlueZ devices. |
| Windows BLE transport | Unsupported | Present in older code paths only; not part of the v1 support contract. |

### Discovery

| Area | Status | Notes |
| --- | --- | --- |
| SRP discovery via OTBR tooling | Supported | Primary Thread discovery path. |
| DNS-SD fallback via OTBR tooling | Supported | Used when SRP lookup does not resolve a commissioned device. |
| Stored IP fallback | Supported | Used only when explicit discovery fails and a last-known address exists. |
| Full platform-neutral discovery stack | Unsupported | Host/controller-specific integrations remain out of scope for `DotMatter.Core` v1. |

### Sessions and commissioning

| Area | Status | Notes |
| --- | --- | --- |
| PASE commissioning flow | Supported | Must remain covered by protocol vectors and negative-path tests. |
| CASE session establishment | Supported | Includes certificate chain verification and session key derivation. |
| Automatic reconnect with backoff | Supported | Uses bounded retries and jittered reconnect delays. |
| Long-running session soak profile | Conditionally supported | Release requires soak validation before claiming stable support. |

### Certificates and crypto

| Area | Status | Notes |
| --- | --- | --- |
| Certificate generation/signing for current controller flow | Supported | Public surface should stay on platform types; low-level interop remains internal. |
| P-256 key import/export interop bridge | Internal | Required implementation detail, not part of the supported public contract. |
| Arbitrary ASN.1/point-math helpers | Internal | Keep behind bridge APIs; do not expose BouncyCastle-specific types publicly. |

### Cluster APIs

| Area | Status | Notes |
| --- | --- | --- |
| Working generated command/attribute paths used by current device flows | Supported | Must be covered by integration tests. |
| Generated APIs with serializer/parser TODO behavior | Unsupported | The generator must emit explicit `NotSupportedException` paths for these members; do not patch generated files by hand. |
| Complex generated attribute readers without verified parsers | Unsupported | These stay out of the supported surface until CodeGen emits a concrete parser. |
| All generated artifacts by default | Unsupported | Code generation output is not equivalent to a supported contract. |

## Platform assumptions

- Linux with BlueZ is the primary BLE commissioning target for v1.
- `DotMatter.Core` assumes the host can provide OTBR tooling for SRP/DNS discovery where Thread discovery is required.
- Public consumers should not rely on raw BouncyCastle types crossing the Core boundary.

## Release expectations

- All supported paths have automated tests.
- Unsupported paths fail clearly rather than silently running partial implementations.
- Public API changes follow semantic versioning and require review.
- Generator changes that affect supported cluster APIs must be accompanied by an intentional regeneration step before release.
