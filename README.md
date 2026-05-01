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
- `DotMatter.Hosting`: registry, recovery, OTBR integration, and long-running host behavior.
- `DotMatter.Controller`: authenticated REST API host for Raspberry Pi deployments, including ACL and binding write/query/remove endpoints.

## Quick Start

```bash
# Build
dotnet build DotMatter.slnx

# Run tests (no hardware needed)
dotnet test

# Run the controller locally
dotnet run --project DotMatter.Controller
```

For deployment, configuration, and runtime expectations, start with the [Controller Guide](docs/CONTROLLER.md) and [Deployment Guide](docs/DEPLOYMENT.md).

## What's Inside

### DotMatter.Core — Matter Protocol Library

The core library implements the Matter protocol from scratch in C#:

- **TLV codec** — Matter's tag-length-value wire format, fully typed encode/decode
- **Cryptography** — PASE (Spake2+), CASE (Sigma), certificate generation and signing (P-256/ECDSA)
- **Sessions** — PASE and CASE session establishment with encrypted message framing
- **BLE transport** — Linux BlueZ-based BLE commissioning via D-Bus
- **UDP transport** — Operational messaging over IPv6/UDP
- **Discovery** — mDNS service discovery and OTBR SRP/DNS-SD integration
- **Interaction Model** — Read, Write, Invoke, Subscribe operations
- **Fabric ACL and Binding writes** — generated timed writers for AccessControl ACL and Binding list attributes
- **130+ generated clusters** — Auto-generated from official Matter XML definitions (10 tested, rest generated with correct IDs/types)
- **AOT compatible** — Fully trimming and Native AOT safe

### DotMatter.Hosting — Device Lifecycle

Shared infrastructure for building Matter controller applications:

- Device registry with atomic file persistence
- Session recovery with bounded reconnect and subscription monitoring
- OTBR service integration (`ot-ctl`)

### DotMatter.Controller — Ready-to-Deploy Application

An authenticated REST API controller that runs on Raspberry Pi. The tracked deployment model uses generic systemd unit files plus machine-local deploy settings and Pi env files. See [Controller Guide](docs/CONTROLLER.md) for API surface, deployment, and configuration.

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

Commissioning is provided by `DotMatter.Core.Commissioning.MatterCommissioner`. Production-ready orchestration, persistence, and HTTP control flow live in `DotMatter.Controller` and `DotMatter.Hosting`.

## Requirements

- .NET 10 SDK
- Linux with BlueZ for BLE commissioning (pre-installed on Raspberry Pi OS)
- OpenThread Border Router (OTBR) for Thread device discovery

## Project Structure

```text
DotMatter/
├── DotMatter.Core/          # Protocol: TLV, crypto, sessions, BLE, discovery, 130+ clusters
├── DotMatter.Hosting/       # Device lifecycle: registry, recovery, OTBR integration
├── DotMatter.Controller/    # Authenticated REST API host for Raspberry Pi
├── DotMatter.CodeGen/       # CLI tool to generate cluster definitions from Matter XML
├── DotMatter.Benchmarks/    # Performance benchmarks for hot paths
├── DotMatter.Tests/         # NUnit test project
└── docs/                    # Guides: controller, deployment, clusters, configuration
```

## Documentation

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
