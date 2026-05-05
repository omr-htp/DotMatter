# DotMatter Product Release Checklist

Use this before calling the full `DotMatter` product production-ready.

## Security and defaults

- API auth is enabled by default for all non-health endpoints.
- Startup fails closed unless a real API key is supplied when auth is enabled.
- No query-string API key path is required for normal operation.
- CORS is deny-by-default unless explicit allowlist origins are configured.
- Detailed or sensitive diagnostics remain disabled by default.
- Reverse-proxy or trusted-LAN deployment guidance is documented for v1.
- No machine-specific deploy targets, hostnames, credentials, or secrets remain in tracked project files.

## Correctness and runtime behavior

- Startup sequencing is clean: OTBR readiness, registry load, device reconnect scheduling, and readiness signaling behave predictably.
- Background work is supervised and owned; no fire-and-forget `Task.Run` paths remain in the production story.
- Registry writes are atomic and durable.
- Readiness and liveness endpoints are both present and semantically distinct.
- Controller failures map to stable HTTP semantics for client-visible error handling.

## AOT and packaging

- `dotnet build DotMatter.slnx --no-restore` succeeds.
- `dotnet test -c Debug --no-build` passes.
- Linux ARM64 AOT publish succeeds with `dotnet msbuild DotMatter.Controller -t:Deploy /p:DeployType=Aot` on a machine with WSL Debian.
- `dot-matter.service` and `dot-matter-aot.service` still match the documented deployment model.
- Service account, state directory, and configuration expectations are documented.

## Product validation

- Supported matrix reflects the actual shipping subset.
- Commission -> persist -> reconnect -> command flows pass against the supported device subset.
- Auth, rate limiting, readiness, and commissioning single-flight tests pass.
- Long-running reconnect/subscription soak validation has been reviewed for the release build.
- No known secret leakage or host-wide destructive side effects remain.
