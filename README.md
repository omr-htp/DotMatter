# DotMatter

[![CI](https://img.shields.io/github/actions/workflow/status/omr-htp/DotMatter/ci.yml?branch=main&label=CI)](https://github.com/omr-htp/DotMatter/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/omr-htp/DotMatter)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](global.json)

> ⚠️ **This project is experimental and under active development.** APIs may change, and stability is not guaranteed yet.

A .NET 10 Matter protocol implementation for building Matter controllers on Linux. It includes a complete protocol stack, 130+ generated cluster definitions, and a Raspberry Pi-focused controller application with a documented deployment model.

## Project Status

- Experimental: APIs and supported flows may change.
- Product focus: Linux and Raspberry Pi controller deployments.
- Network model: intended for trusted LAN use or deployment behind a reverse proxy that handles external exposure.

## At a Glance

- `DotMatter.Core`: protocol, crypto, sessions, transport, discovery, and generated clusters.
- `DotMatter.Hosting`: shared host/runtime infrastructure: registry, storage, commissioning persistence, recovery, OTBR integration, binding/ACL helpers, device cataloging, common command primitives, and long-running host behavior.
- `DotMatter.Controller`: authenticated REST API host for Raspberry Pi deployments, including runtime diagnostics, ACL/binding write/query/remove endpoints, and a thin Matter event testing surface.
- `DotMatter.Ui`: Blazor/Radzen operator console for commissioning, device workbench flows, discovery, and live controller/Matter event monitoring.

## Quick Start

```bash
# Build
dotnet build DotMatter.slnx

# Run tests (no hardware needed)
dotnet test

# Run the controller locally
dotnet run --project DotMatter.Controller

# Run the operator UI locally
dotnet run --project DotMatter.Ui
```

The UI points at `http://localhost:5000` by default. Override it with `ControllerApi__BaseUrl` and `ControllerApi__ApiKey` when you want the operator console to target another controller host.

For deployment, configuration, routes, and runtime expectations, start with the [UI Guide](docs/UI.md), [Controller Guide](docs/CONTROLLER.md), and [Deployment Guide](docs/DEPLOYMENT.md).

## What's Inside

### DotMatter.Core — Matter Protocol Library

The core library implements the Matter protocol from scratch in C#:

- **TLV codec** — Matter's tag-length-value wire format, fully typed encode/decode
- **Cryptography** — PASE (Spake2+), CASE (Sigma), certificate generation and signing (P-256/ECDSA)
- **Sessions** — PASE and CASE session establishment with encrypted message framing
- **BLE transport** — Linux BlueZ-based BLE commissioning via D-Bus
- **UDP transport** — Operational messaging over IPv6/UDP
- **Discovery** — mDNS service discovery and OTBR SRP/DNS-SD integration
- **Interaction Model** — Read, Write, Invoke, Subscribe operations, including public generic `MatterInteractions` helpers
- **Topology helpers** — public `MatterTopology` APIs for endpoint, cluster, and device-type discovery
- **Commissionable discovery** — public `CommissionableDiscovery` browse/resolve helpers for `_matterc._udp.local`
- **Operational controller helpers** — public `MatterAdministration` APIs for commissioning-window and fabric-management flows
- **Recommended client lifecycle** — `ResilientSession.GetConnectedSessionAsync(...)` / `UseSessionAsync(...)` for reconnecting operational access
- **Fabric ACL and Binding writes** — generated timed writers for AccessControl ACL and Binding list attributes
- **Matter events** — raw `MatterEvents` APIs, generated typed cluster `ReadEventsAsync(...)` / `SubscribeEventsAsync(...)` helpers, and controller-facing typed event envelopes for live testing
- **130+ generated clusters** — Auto-generated from official Matter XML definitions (10 tested, rest generated with correct IDs/types)
- **AOT compatible** — Fully trimming and Native AOT safe

### DotMatter.Hosting — Shared Host Runtime

Shared infrastructure for building Matter controller applications:

- Device registry with atomic file persistence and safe fabric-name/path helpers
- Commissioning storage helpers for shared fabric identity and `node_info.json`
- Session recovery with bounded reconnect and subscription monitoring
- OTBR service integration (`ot-ctl`)
- Reusable binding/ACL operations, device catalog/capability labels, and common OnOff/level/color command primitives

### DotMatter.Controller — Ready-to-Deploy Application

An authenticated REST API controller that runs on Raspberry Pi. The tracked deployment model uses generic systemd unit files plus machine-local deploy settings and Pi env files. See [Controller Guide](docs/CONTROLLER.md) for API surface, deployment, and configuration.

### DotMatter.Ui — Operator Console

A server-hosted Blazor Web App that consumes the controller REST API for lab workflows that are cumbersome in Scalar alone: commissioning, discovery, capability-aware per-device operations, and split controller/Matter live-feed surfaces.

See the [UI Guide](docs/UI.md) for page-by-page details, configuration notes, and the full screenshot set.

<img src="docs/images/ui/dashboard.png" alt="DotMatter UI dashboard" width="960" />

## Usage Example

```csharp
using DotMatter.Core.Clusters;
using DotMatter.Core.Sessions;

// With an established Matter secure session...
ISession session = GetSessionSomehow();

var onOff = new OnOffCluster(session, endpointId: 1);
await onOff.OnAsync();

var level = new LevelControlCluster(session, endpointId: 1);
await level.MoveToLevelAsync(128, transitionTime: 10);

var color = new ColorControlCluster(session, endpointId: 1);
await color.MoveToHueAndSaturationAsync(hue: 180, saturation: 254, transitionTime: 10);
```

Fabric-scoped ACL and Binding attributes can be read and written through generated cluster APIs. Writes default to timed interactions because Matter devices commonly require timed writes for these list attributes.

```csharp
var accessControl = new AccessControlCluster(targetSession, endpointId: 0);
var acl = await accessControl.ReadACLAsync() ?? [];
var writeAcl = await accessControl.WriteACLAsync(acl);

var binding = new BindingCluster(switchSession, endpointId: 1);
var bindings = await binding.ReadBindingAsync() ?? [];
var writeBinding = await binding.WriteBindingAsync(bindings);
```

Typed Matter event reads are also available from generated clusters, with a raw fallback surface available through `DotMatter.Core.InteractionModel.MatterEvents`. The controller testing surface preserves the common metadata while also surfacing typed payload JSON when DotMatter has generated support for the cluster/event.

```csharp
var switchCluster = new SwitchCluster(session, endpointId: 1);
var events = await switchCluster.ReadEventsAsync();

await using var subscription = await switchCluster.SubscribeEventsAsync();
subscription.OnEvent += reports =>
{
    foreach (var evt in reports)
    {
        Console.WriteLine($"{evt.EventName} #{evt.EventNumber}");
    }
};
```

DotMatter.Core also exposes generic controller/client helpers when you need arbitrary Interaction Model access without adding a generated wrapper first.

```csharp
using DotMatter.Core;
using DotMatter.Core.InteractionModel;

var onOff = await MatterInteractions.ReadAttributeAsync<bool>(
    session,
    endpointId: 1,
    clusterId: 0x0006,
    attributeId: 0x0000);

var topology = await MatterTopology.DescribeAsync(session);
var commissioning = await MatterAdministration.ReadCommissioningStateAsync(session);
```

For reconnecting operational flows, `ResilientSession.UseSessionAsync(...)` can run these same helpers on the recommended resilient controller lifecycle without exposing `Node` / `SessionManager` plumbing.

Commissioning is provided by `DotMatter.Core.Commissioning.MatterCommissioner`. Production-ready orchestration, persistence, and HTTP control flow live in `DotMatter.Controller` and `DotMatter.Hosting`.

## Requirements

- .NET 10 SDK
- Linux with BlueZ for BLE commissioning (pre-installed on Raspberry Pi OS)
- OpenThread Border Router (OTBR) for Thread device discovery

## Project Structure

```text
DotMatter/
├── DotMatter.Core/          # Protocol: TLV, crypto, sessions, BLE, discovery, 130+ clusters
├── DotMatter.Hosting/       # Shared host runtime: storage, registry, recovery, OTBR, commands, binding/ACL
├── DotMatter.Controller/    # Authenticated REST API host for Raspberry Pi
├── DotMatter.Ui/            # Blazor operator console and Pi companion service
├── DotMatter.CodeGen/       # CLI tool to generate cluster definitions from Matter XML
├── DotMatter.Benchmarks/    # Performance benchmarks for hot paths
├── DotMatter.Tests/         # NUnit test project
└── docs/                    # Guides: controller, deployment, clusters, configuration
```

## Documentation

- [UI Guide](docs/UI.md) — Routes, screenshots, controller-client behavior, and Pi-hosted operator workflows
- [Controller Guide](docs/CONTROLLER.md) — API surface, deployment model, service files
- [Deployment Guide](docs/DEPLOYMENT.md) — Pi setup, local deploy props, env files, Samba fast-loop, AOT
- [Configuration Reference](docs/CONFIGURATION.md) — All settings with defaults
- [Cluster Reference](docs/CLUSTERS.md) — Generated clusters and how to add support
- [OTBR Setup](docs/OTBR_SETUP.md) — OpenThread Border Router installation
- [Core Support Matrix](docs/DOTMATTER_CORE_SUPPORT_MATRIX.md) — What's supported in Core v1
- [Product Support Matrix](docs/DOTMATTER_PRODUCT_SUPPORT_MATRIX.md) — Controller product scope
- [Core Release Checklist](docs/DOTMATTER_CORE_RELEASE_CHECKLIST.md)
- [Product Release Checklist](docs/DOTMATTER_PRODUCT_RELEASE_CHECKLIST.md)
- [Benchmark Thresholds](docs/DOTMATTER_CORE_BENCHMARK_THRESHOLDS.md)
- [Third-Party Notices](THIRD_PARTY_NOTICES.md) — Vendored input provenance and licensing notes
- [Contributing Guide](CONTRIBUTING.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)

## Acknowledgements

DotMatter was initially inspired by the early exploration work in [`tomasmcguinness/dotnet-matter`](https://github.com/tomasmcguinness/dotnet-matter) by Tomas McGuinness.

## Getting Help

- Security issues: follow [SECURITY.md](SECURITY.md) and use private vulnerability reporting instead of public issues.
- Contributions: start with [CONTRIBUTING.md](CONTRIBUTING.md).
- Community expectations: see [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## License

MIT
