# DotMatter Configuration Reference

`DotMatter.Controller` and `DotMatter.Ui` use standard .NET configuration binding
via `appsettings.json` plus environment-specific overrides like
`appsettings.Development.json`.

Environment variables use `__` to represent section nesting.

In Pi deployments, the tracked systemd units also load optional env files:

- `/etc/dotmatter/dot-matter.env`
- `/etc/dotmatter/dot-matter-aot.env`
- `/etc/dotmatter/dot-matter-ui.env`

The tracked systemd units default to `DOTNET_ENVIRONMENT=Production`, so API-key
auth remains enabled unless you explicitly opt into development overrides in a
machine-local env file.

> **Security:** The controller and UI talk over HTTP by default. Keep them on a trusted LAN or behind a reverse proxy with TLS termination. WiFi passwords, pairing codes, and API keys are sensitive request data.

## Security

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Security__RequireApiKey` | `true` | Require auth for non-health endpoints. |
| `Controller__Security__HeaderName` | `X-API-Key` | Header checked for API authentication. |
| `Controller__Security__ApiKey` | none | Required whenever `RequireApiKey=true`. The tracked base config omits it, so startup fails closed until an operator supplies a real value. |
| `Controller__Security__AllowedCorsOrigins__0` | none | Optional first allowed CORS origin. |

## API behavior

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Api__RateLimitPermitLimit` | `60` | Requests allowed per window. |
| `Controller__Api__RateLimitWindow` | `00:01:00` | Fixed-window rate limit duration. |
| `Controller__Api__RateLimitQueueLimit` | `5` | Rate-limit queue depth. |
| `Controller__Api__SseClientBufferCapacity` | `100` | Per-client SSE buffer cap. Slow clients can lose older events when the buffer fills. |
| `Controller__Api__CommandTimeout` | `00:00:10` | Default device command timeout. |
| `Controller__Api__EnableOpenApi` | `false` | Enable OpenAPI and Scalar UI. `appsettings.Development.json` turns this on for local development. |

## Diagnostics

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Diagnostics__EnableDetailedRuntimeEndpoint` | `false` | Enable `GET /api/system/diagnostics`. Keep disabled unless you intentionally want authenticated operators to see the detailed runtime/configuration snapshot. |

## Commissioning

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Commissioning__DefaultFabricNamePrefix` | `device` | Prefix used when naming new device fabrics. |
| `Controller__Commissioning__SharedFabricName` | `DotMatter` | Existing fabric directory whose controller fabric material is copied into newly named device directories so commissioned devices join the shared fabric. |
| `Controller__Commissioning__FollowUpConnectTimeout` | `00:00:30` | Timeout for post-commission connect attempts. |
| `Controller__Commissioning__RegulatoryLocation` | `IndoorOutdoor` | Regulatory location sent during commissioning. Valid values: `Indoor`, `Outdoor`, `IndoorOutdoor`. |
| `Controller__Commissioning__RegulatoryCountryCode` | `XX` | Two-letter uppercase country code sent during commissioning. |
| `Controller__Commissioning__AttestationPolicy` | `RequireDacChain` | Device attestation policy. `RequireDacChain` validates DAC -> PAI and the attestation signature, `AllowTestDevices` logs warnings and continues for failing test devices, and `Disabled` skips verifier wiring. Full PAA trust-store validation is still future work. |

`SharedFabricName` is important for Binding: the source switch and target device must be on the same Matter fabric before the controller can write a useful ACL entry on the target and Binding entry on the switch. The default shared fabric name is `DotMatter`.

## Persistence and recovery

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Registry__BasePath` | `/var/lib/.dot-matter/fabrics` | Registry/fabric storage path. |
| `Controller__SessionRecovery__SubscriptionStaleThreshold` | `00:01:30` | Threshold for stale subscription detection. |
| `Controller__SessionRecovery__StartupConnectTimeout` | `00:00:15` | Startup reconnect timeout per device. |
| `Controller__SessionRecovery__EndpointDiscoveryTimeout` | `00:00:10` | Discovery timeout. |
| `Controller__SessionRecovery__StateReadTimeout` | `00:00:10` | Read-state timeout. |
| `Controller__SessionRecovery__SubscriptionSetupTimeout` | `00:00:10` | Subscription setup timeout. |
| `Controller__SessionRecovery__MonitoringLoopDelay` | `00:00:01` | Monitoring loop delay. |
| `Controller__SessionRecovery__BackgroundShutdownTimeout` | `00:00:10` | Graceful background shutdown timeout. |

## OTBR integration

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__Otbr__EnableSrpServerOnStartup` | `true` | Try to enable SRP on startup. |
| `Controller__Otbr__CommandPath` | `ot-ctl` | OTBR command path. |
| `Controller__Otbr__SudoCommand` | `sudo` | Optional sudo wrapper. |
| `Controller__Otbr__ThreadIpDiscoveryMaxAttempts` | `10` | Thread IP discovery retries. |
| `Controller__Otbr__ThreadIpDiscoveryDelay` | `00:00:02` | Delay between OTBR discovery retries. |

## Core logging

| Setting | Default | Purpose |
| --- | --- | --- |
| `Controller__MatterLog__EnableSensitiveDiagnostics` | `false` | Gate for sensitive payload logging. |
| `Controller__MatterLog__MaxRenderedBytes` | `32` | Max rendered bytes for non-sensitive payload formatting. |

## Minimal production env file

```dotenv
ASPNETCORE_URLS=http://0.0.0.0:5000
Controller__Security__ApiKey=replace-with-a-long-random-value
Controller__Registry__BasePath=/var/lib/.dot-matter/fabrics
Controller__Commissioning__SharedFabricName=DotMatter
Controller__Otbr__CommandPath=ot-ctl
Controller__Otbr__SudoCommand=sudo
```

For systemd-based Pi installs, place these values in `/etc/dotmatter/dot-matter.env`
or `/etc/dotmatter/dot-matter-aot.env` rather than editing tracked project files.

## DotMatter.Ui controller client

| Setting | Default | Purpose |
| --- | --- | --- |
| `ControllerApi__BaseUrl` | `http://localhost:5000` | Base URL used by the UI's server-side controller client. Pi deployments typically use `http://127.0.0.1:5000`. |
| `ControllerApi__ApiKeyHeaderName` | `X-API-Key` | Header name sent by the UI when controller auth is enabled. |
| `ControllerApi__ApiKey` | none | API key used by the UI's server-side client. Required whenever the target controller keeps `RequireApiKey=true`. |
| `ControllerApi__RequestTimeoutSeconds` | `45` | Default timeout for non-stream controller requests. |
| `ControllerApi__StreamBufferLimit` | `160` | Max buffered SSE messages kept by the dedicated live-feed page. |

The UI is server-hosted, so `ControllerApi__ApiKey` stays on the UI host and is not exposed to the browser.

## Minimal UI env file

```dotenv
ASPNETCORE_URLS=http://0.0.0.0:5001
ControllerApi__BaseUrl=http://127.0.0.1:5000
ControllerApi__ApiKey=replace-with-the-controller-api-key
ControllerApi__RequestTimeoutSeconds=45
ControllerApi__StreamBufferLimit=160
```

Place UI overrides in `/etc/dotmatter/dot-matter-ui.env` on the Pi, or in
development user secrets / environment variables for local runs.

## Endpoint security notes

- API keys are accepted only through the configured header, `X-API-Key` by default. Query-string API keys are not supported.
- `/health`, `/health/live`, and `/health/ready` are intentionally unauthenticated for monitoring and orchestration.
- `/api/events` and `/api/matter/events` are SSE streams. Rate limiting applies to the initial request, not the full connection lifetime. Clients should reconnect on disconnect and refresh device state after reconnecting.
- `/api/system/runtime` is authenticated and safe for normal operator use.
- `/api/system/diagnostics` is authenticated and disabled by default unless `Controller__Diagnostics__EnableDetailedRuntimeEndpoint=true`.
- Keep OpenAPI disabled in production unless the endpoint is protected by your deployment architecture.
