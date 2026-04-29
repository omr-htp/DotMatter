# DotMatter.Core Release Checklist

Use this checklist before marking a `DotMatter.Core` build as production-ready.

## Correctness and security

- No `async void` runtime paths remain in the supported Core surface.
- No generic `Exception` is thrown from supported TLV, discovery, transport, or protocol parsing paths.
- TLV UTF-8 strings encode byte length, not `string.Length`.
- Fabric metadata updates persist on overwrite, not just first creation.
- Sensitive material is redacted from logs by default.
- Linux BLE cleanup never removes unrelated BlueZ cached devices.

## API and support contract

- Supported transports, discovery flows, session flows, and cluster operations are reflected in the support matrix.
- Unsupported generated cluster methods fail clearly instead of behaving as partial implementations.
- Unsupported generated cluster attributes fail clearly instead of returning partially parsed objects.
- Public API review/approval checks pass for the intended release surface.
- Platform assumptions for v1 are documented.
- CodeGen changes that alter generated API behavior have been regenerated intentionally before packaging.

## Operability

- Structured logging remains usable without dumping large payload hex by default.
- Activity and meter hooks are present for session connects, reconnect loops, discovery attempts, and TLV parse failures.
- Retry and reconnect behavior is bounded and includes jitter to avoid synchronized reconnect storms.

## Validation

- Unit tests pass.
- Integration tests for commissioning, CASE establishment, reconnect, and discovery fallback pass.
- Negative-path tests for malformed payloads, bad signatures, and cancellation pass.
- Stress/soak coverage for reconnect churn and long-running subscriptions is clean for the supported matrix.
- Benchmark thresholds for hot paths are reviewed and acceptable for the target release.
