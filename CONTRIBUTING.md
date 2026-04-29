# Contributing to DotMatter

Thank you for your interest in DotMatter! This guide helps you get started.

## Prerequisites

- .NET 10 SDK (the repo pins `10.0.202` via `global.json`)
- Raspberry Pi 5 with BlueZ (pre-installed on Raspberry Pi OS; for hardware testing)
- OpenThread Border Router (OTBR) with RCP radio (for Thread device testing)

## Building

```bash
dotnet build DotMatter.slnx -c Debug
```

## Running Tests

```bash
dotnet test DotMatter.Tests/DotMatter.Tests.csproj -c Debug
```

All tests should pass. Tests run without hardware dependencies.

## Project Structure

| Project | Description |
|---------|-------------|
| `DotMatter.Core` | Protocol implementation: TLV, clusters, sessions, mDNS, BLE |
| `DotMatter.Hosting` | Shared device host infrastructure: registry, sessions, subscriptions |
| `DotMatter.Controller` | Standalone REST API controller service (runs on Raspberry Pi) |
| `DotMatter.CodeGen` | Cluster code generator from Matter XML definitions |
| `DotMatter.Tests` | Unit tests |

## Code Style

- Follow existing patterns in the codebase
- Use `TreatWarningsAsErrors` — the build must be warning-free
- XML doc comments on all public types and members
- Keep methods short and focused

## Pull Requests

1. Fork the repository
2. Create a feature branch from `main`
3. Make your changes with clear commit messages
4. Ensure `dotnet build` and `dotnet test` pass
5. Submit a PR with a description of what changed and why

## Reporting Issues

Use GitHub Issues. Include:
- .NET SDK version (`dotnet --info`)
- Hardware setup (Pi model, radio module)
- Steps to reproduce
- Expected vs actual behavior
