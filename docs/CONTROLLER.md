# DotMatter.Controller

`DotMatter.Controller` is a ready-to-deploy REST API application built on `DotMatter.Core` and `DotMatter.Hosting`. It runs on Raspberry Pi as a systemd service and provides an HTTP API for commissioning and controlling Matter devices.

## Features

- **Auth enabled by default** — non-health endpoints require an API key unless the environment explicitly disables it, with CORS deny-by-default
- **BLE commissioning** — PASE → CSR → NOC → Thread/WiFi provisioning
- **Thread + WiFi control** — OTBR-backed Thread discovery and WiFi commissioning
- **Runtime diagnostics** — authenticated runtime status plus optional detailed diagnostics endpoint
- **Matter event inspection** — one-shot event reads plus a dedicated Matter-event SSE stream with typed payload JSON when available
- **Switch binding + removal** — creates and reconciles Matter ACL and Binding state for a switch OnOff route
- **Bounded runtime** — Owned reconnect loops, readiness/liveness health, SSE cleanup, atomic registry writes
- **AOT-ready** — Publishes as native AOT on Linux ARM64
- **OpenAPI + SSE** — Scalar/OpenAPI UI and server-sent events for clients

## API Surface

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/acls` | Query AccessControl ACL entries across a controller fabric |
| GET | `/api/bindings` | Query Binding entries across a controller fabric |
| GET | `/api/devices` | List all devices |
| GET | `/api/devices/{id}` | Device details |
| GET | `/api/devices/{id}/acl` | Query AccessControl ACL entries from one device |
| GET | `/api/devices/{id}/bindings` | Query Binding entries from one source device endpoint |
| GET | `/api/devices/{id}/matter/events` | Read Matter event envelopes from one device |
| POST | `/api/devices/{id}/acl/remove` | Remove matching AccessControl ACL entries from one device |
| POST | `/api/devices/{id}/bindings/remove` | Remove matching Binding entries from one source device endpoint |
| GET | `/api/devices/{id}/state` | Current state |
| POST | `/api/devices/{id}/on` | Turn on |
| POST | `/api/devices/{id}/off` | Turn off |
| POST | `/api/devices/{id}/toggle` | Toggle |
| POST | `/api/devices/{id}/level` | Set brightness level |
| POST | `/api/devices/{id}/color` | Set color (Hue/Saturation) |
| POST | `/api/devices/{id}/color-xy` | Set color (CIE xy) |
| POST | `/api/devices/{id}/bindings/onoff` | Bind switch OnOff client to a target OnOff endpoint |
| POST | `/api/devices/{id}/bindings/onoff/remove` | Remove switch OnOff route and reconcile matching ACL |
| GET | `/api/system/runtime` | Safe authenticated runtime snapshot |
| GET | `/api/system/diagnostics` | Detailed runtime diagnostics when enabled |
| GET | `/api/matter/events` | Dedicated SSE stream for live Matter event envelopes |
| POST | `/api/commission` | Commission Thread device |
| POST | `/api/commission/wifi` | Commission WiFi device |
| DELETE | `/api/devices/{id}` | Remove device |
| GET | `/health` | Overall health |
| GET | `/health/live` | Liveness check |
| GET | `/health/ready` | Readiness check |

## Deployment

Two services on the Pi, **only one runs at a time** (systemd `Conflicts=` auto-switches):

| Service | Type | Deploy Command |
|---------|------|----------------|
| `dot-matter` | Debug (FDD) | `dotnet msbuild DotMatter.Controller -t:Deploy` |
| `dot-matter-aot` | AOT (native) | `dotnet msbuild DotMatter.Controller -t:Deploy /p:DeployType=Aot` |

- `systemd` unit files are included in the repo root (`dot-matter.service`, `dot-matter-aot.service`)
- Deploy uses tracked MSBuild targets with machine-local values loaded from an ignored `DotMatter.Controller.Deploy.local.props`
- Services run as a dedicated non-root `dotmatter` user
- Runtime overrides can be supplied through `/etc/dotmatter/dot-matter.env` and `/etc/dotmatter/dot-matter-aot.env`
- `dot-matter` and `dot-matter-aot` default to `Production`; trusted-LAN development can opt into `Development` in local env files
- Runtime state lives under `/var/lib/.dot-matter`

See [Deployment Guide](DEPLOYMENT.md) for local deploy props setup, Pi env files, Samba fast-loop configuration, and AOT cross-compilation.

## Runtime Diagnostics

`GET /api/system/runtime` returns a safe authenticated runtime snapshot for the controller service. It is intended for operators and automation that need more detail than `/health` without exposing sensitive internals.

The runtime payload includes:

- startup / ready / stopping state
- uptime and startup timestamp
- environment name
- known and online device counts
- in-process counters for:
  - commissioning attempts / rejections
  - API authentication failures
  - rate-limit rejections
  - managed reconnect requests
  - subscription restarts
  - registry persistence failures

Example request:

```http
GET /api/system/runtime
X-API-Key: replace-with-a-long-random-value
```

`GET /api/system/diagnostics` returns a more detailed runtime/configuration snapshot, but it is **disabled by default**. Enable it only when you intentionally want authenticated operators to see additional runtime configuration detail.

Enable it with:

```dotenv
Controller__Diagnostics__EnableDetailedRuntimeEndpoint=true
```

When enabled, the detailed response includes:

- the full safe runtime snapshot
- non-secret API/runtime configuration such as rate-limit settings and OpenAPI state
- diagnostics gating state, including whether sensitive core diagnostics are enabled

Example request:

```http
GET /api/system/diagnostics
X-API-Key: replace-with-a-long-random-value
```

## ACL, Binding, and Route Removal

AccessControl ACL state is stored on target devices, not in DotMatter's local fabric files. ACL query endpoints therefore read the live `AccessControl.ACL` attribute from device endpoint 0.

`GET /api/acls` queries known devices on the configured shared fabric and returns each device's ACL entries.

The fabric-wide response includes per-device errors for devices that are offline or cannot be read; one failed device does not fail the whole query.

For normal use, the controller selects the fabric from `Controller__Commissioning__SharedFabricName`. A legacy `fabricName` query string is still accepted for compatibility, but it is not part of the primary API shape.

`GET /api/devices/{id}/acl` reads the ACL list from endpoint 0 of one device. It returns `404` for an unknown device, `503` when the device is known but disconnected or times out, and `502` for transport/decode failures.

ACL entries include privilege, auth mode, raw subject values, targets, fabric index, and optional subject resolution back to known DotMatter devices when the subject matches an operational node ID.

Example fabric-wide ACL query:

```http
GET /api/acls
X-API-Key: replace-with-a-long-random-value
```

Example single-device ACL query:

```http
GET /api/devices/light-device-id/acl
X-API-Key: replace-with-a-long-random-value
```

Binding state is stored on source devices, not in DotMatter's local fabric files. Query endpoints therefore read live Binding cluster attributes from devices on the controller fabric.

`GET /api/bindings` queries known devices on the configured shared fabric and returns each source device's Binding entries.

- `endpoint` — optional source endpoint. When omitted, DotMatter uses discovered Binding endpoints, falling back to endpoint 1.

The fabric-wide response includes per-source errors for devices that are offline or cannot be read; one failed source does not fail the whole query.

For normal use, the controller selects the fabric from `Controller__Commissioning__SharedFabricName`. A legacy `fabricName` query string is still accepted for compatibility, but it is not part of the primary API shape.

`GET /api/devices/{id}/bindings?endpoint=1` reads the Binding list from one source device endpoint. It returns `404` for an unknown source device, `503` when the device is known but disconnected or times out, and `502` for transport/decode failures.

Example fabric-wide query:

```http
GET /api/bindings
X-API-Key: replace-with-a-long-random-value
```

Example single-device query:

```http
GET /api/devices/switch-device-id/bindings?endpoint=1
X-API-Key: replace-with-a-long-random-value
```

`POST /api/devices/{id}/bindings/onoff` configures the source device identified by `{id}` as a switch and binds its OnOff client to a target device's OnOff server endpoint.

The controller performs both required Matter writes:

1. It writes the target device's AccessControl ACL on endpoint 0 so the switch node has `Operate` privilege for cluster `0x0006` on the target endpoint.
2. It writes the source switch endpoint's Binding list so button events target the requested node, endpoint, and OnOff cluster.

Existing ACL and Binding list entries are preserved. The generated cluster writers use timed writes by default, and the controller returns failure if the device rejects either write.

Example request:

```http
POST /api/devices/switch-device-id/bindings/onoff
X-API-Key: replace-with-a-long-random-value
Content-Type: application/json

{
  "targetDeviceId": "light-device-id",
  "sourceEndpoint": 1,
  "targetEndpoint": 1
}
```

Both devices must already be commissioned, reachable, and on the same controller fabric.

## Matter Event Testing Surface

DotMatter now exposes a separate Matter-event testing surface alongside the existing state-change API.

`GET /api/devices/{id}/matter/events` performs a one-shot event read for one cluster on one device.

- `cluster` — required cluster ID, in decimal or `0x`-prefixed hex
- `eventId` — optional event ID, in decimal or `0x`-prefixed hex
- `endpoint` — optional endpoint override; when omitted, DotMatter uses endpoint discovery and falls back to endpoint `0` for root clusters such as AccessControl / GeneralDiagnostics and endpoint `1` otherwise
- `fabricFiltered` — optional, defaults to `false`

The response preserves endpoint, cluster, event ID, event number, priority, timestamps, status code, and raw payload TLV hex. When DotMatter has generated support for the cluster/event, the response also includes:

- `clusterName`
- `eventName`
- `payload.kind = "typed"`
- `payload.data` with typed event fields in JSON form

When DotMatter cannot materialize a typed payload, the controller still returns the same metadata plus `payload.kind = "unknown"` and a reason string.

Example one-shot read:

```http
GET /api/devices/switch-device-id/matter/events?cluster=0x003B&eventId=0x0001
X-API-Key: replace-with-a-long-random-value
```

`GET /api/matter/events` is a dedicated **standard SSE** stream for live Matter event envelopes observed by the controller's device subscriptions. It is intentionally separate from `/api/events`.

Live event subscriptions are now discovery-driven: when endpoint discovery succeeds, DotMatter subscribes to every discovered cluster on the device for which DotMatter has generated event support, rather than a narrow hardcoded allowlist.

The stream stays idle when no Matter events are being reported. Clients should keep the `text/event-stream` request open and wait for normal `data:` frames rather than expecting synthetic heartbeat payloads.

Example live stream:

```http
GET /api/matter/events
X-API-Key: replace-with-a-long-random-value
```

`POST /api/devices/{id}/bindings/onoff/remove` performs the inverse route cleanup:

1. It removes the matching source Binding entry for the target node / endpoint / OnOff cluster.
2. It removes the matching target ACL grant only when DotMatter can prove it is the exact route-owned `Operate` entry it would have created.

If a broader or manual ACL entry still covers the route, DotMatter preserves it and returns a `preserved` ACL outcome instead of deleting it.

Example request:

```http
POST /api/devices/switch-device-id/bindings/onoff/remove
X-API-Key: replace-with-a-long-random-value
Content-Type: application/json

{
  "targetDeviceId": "light-device-id",
  "sourceEndpoint": 1,
  "targetEndpoint": 1
}
```

For advanced operator workflows, DotMatter also exposes raw removal surfaces:

- `POST /api/devices/{id}/bindings/remove`
- `POST /api/devices/{id}/acl/remove`

These endpoints accept explicit request-body match criteria and remove all matching entries on the selected device. They are intended for repair/recovery workflows when the higher-level route API is not specific enough.

Example raw Binding removal:

```http
POST /api/devices/switch-device-id/bindings/remove
X-API-Key: replace-with-a-long-random-value
Content-Type: application/json

{
  "endpoint": 1,
  "cluster": 6,
  "targetEndpoint": 1
}
```

Example raw ACL removal:

```http
POST /api/devices/light-device-id/acl/remove
X-API-Key: replace-with-a-long-random-value
Content-Type: application/json

{
  "privilege": "Operate",
  "authMode": "CASE",
  "subjects": ["16846372162258142440"],
  "targets": [
    {
      "cluster": 6,
      "endpoint": 1
    }
  ]
}
```

Both raw removal endpoints use request bodies instead of query strings so multi-field match criteria stay explicit and extensible.

## Configuration

See [Configuration Reference](CONFIGURATION.md) for all settings.

> **Important:** The tracked base config no longer includes an API key. When `RequireApiKey` is enabled, startup fails until you supply `Controller__Security__ApiKey` through environment variables or the Pi env files.

Minimal production environment:

```dotenv
ASPNETCORE_URLS=http://0.0.0.0:5000
Controller__Security__ApiKey=replace-with-a-long-random-value
Controller__Registry__BasePath=/var/lib/.dot-matter/fabrics
Controller__Otbr__CommandPath=ot-ctl
Controller__Otbr__SudoCommand=sudo
```

## Running Locally

```bash
dotnet run --project DotMatter.Controller
```

The OpenAPI UI is available at `http://localhost:5000/scalar/v1` when `EnableOpenApi` is true.

`EnableOpenApi` is enabled by the tracked development settings and disabled by the production base settings.

## SSE Clients

`GET /api/events` streams controller state changes such as online/offline and command-state transitions.

`GET /api/matter/events` streams raw Matter event envelopes observed from live subscriptions.

Each SSE client has a bounded buffer. If a client is slow or disconnected, older events can be dropped, so clients should reconnect and refresh state from the relevant read endpoint after reconnecting.
