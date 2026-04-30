# DotMatter.Controller

`DotMatter.Controller` is a ready-to-deploy REST API application built on `DotMatter.Core` and `DotMatter.Hosting`. It runs on Raspberry Pi as a systemd service and provides an HTTP API for commissioning and controlling Matter devices.

## Features

- **Auth enabled by default** — non-health endpoints require an API key unless the environment explicitly disables it, with CORS deny-by-default
- **BLE commissioning** — PASE → CSR → NOC → Thread/WiFi provisioning
- **Thread + WiFi control** — OTBR-backed Thread discovery and WiFi commissioning
- **Switch binding** — writes Matter ACL and Binding entries so a switch OnOff client can operate a target OnOff endpoint
- **Bounded runtime** — Owned reconnect loops, readiness/liveness health, SSE cleanup, atomic registry writes
- **AOT-ready** — Publishes as native AOT on Linux ARM64
- **OpenAPI + SSE** — Scalar/OpenAPI UI and server-sent events for clients

## API Surface

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/devices` | List all devices |
| GET | `/api/devices/{id}` | Device details |
| GET | `/api/devices/{id}/state` | Current state |
| POST | `/api/devices/{id}/on` | Turn on |
| POST | `/api/devices/{id}/off` | Turn off |
| POST | `/api/devices/{id}/toggle` | Toggle |
| POST | `/api/devices/{id}/level` | Set brightness level |
| POST | `/api/devices/{id}/color` | Set color (Hue/Saturation) |
| POST | `/api/devices/{id}/color-xy` | Set color (CIE xy) |
| POST | `/api/devices/{id}/bindings/onoff` | Bind switch OnOff client to a target OnOff endpoint |
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

## Switch OnOff Binding

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

`GET /api/events` streams server-sent events for state changes. Each client has a bounded buffer. If a client is slow or disconnected, older events can be dropped, so clients should reconnect and refresh state from `/api/devices/{id}/state` after reconnecting.
