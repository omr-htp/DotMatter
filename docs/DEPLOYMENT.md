# Deployment Guide

This guide covers deploying `DotMatter.Controller` to a Raspberry Pi running Debian-based Linux.

> **Security:** The controller listens on HTTP by default. Keep it on a trusted LAN or place it behind a reverse proxy that terminates TLS before any untrusted network can reach it. API keys, pairing codes, and WiFi credentials must not cross untrusted networks over plain HTTP.

## Overview

Two services are available for DotMatter. They use `/var/lib/.dot-matter/fabrics` for Matter fabric state, so **only one can run at a time**. systemd `Conflicts=` ensures starting one auto-stops the other.

| Service | Type | Pi Path | Runtime Env File |
|---------|------|---------|------------------|
| `dot-matter` | Debug (FDD) | `/opt/dot-matter` | `/etc/dotmatter/dot-matter.env` |
| `dot-matter-aot` | Release (AOT) | `/opt/dot-matter-aot` | `/etc/dotmatter/dot-matter-aot.env` |

**FDD** = Framework-Dependent Deployment (requires the .NET runtime on the Pi, supports debugging)  
**AOT** = Native Ahead-of-Time compilation (single native binary, no .NET runtime needed)

## Prerequisites

### Pi (Debian-based Linux)

```bash
# .NET 10 runtime (for FDD services)
# Follow https://learn.microsoft.com/dotnet/core/install/linux-debian

# OpenThread Border Router (OTBR)
# See docs/OTBR_SETUP.md

# BlueZ (for BLE commissioning — pre-installed on Raspberry Pi OS)
# Only needed on minimal/server Debian images:
# sudo apt install bluez

# Samba (for file deployment from Windows)
sudo apt install samba
```

### Dev Machine (Windows)

- .NET 10 SDK
- SSH access to the Pi
- WSL with a Linux distro for AOT cross-compilation, for example `wsl --install Debian`
  - .NET 10 SDK installed inside WSL
  - Cross-compilation toolchain: `sudo apt install gcc-aarch64-linux-gnu`

## Recommended Dev-Machine Setup

The tracked project keeps the `Deploy` target, but all machine-specific values should live in an ignored local props file.

1. Copy [DotMatter.Controller.Deploy.local.props.example](../DotMatter.Controller/DotMatter.Controller.Deploy.local.props.example) to `DotMatter.Controller/DotMatter.Controller.Deploy.local.props`
2. Fill in your Pi host, Samba share names, Samba credentials, and optional WSL distro
3. Keep using the normal deploy commands:

```powershell
dotnet msbuild DotMatter.Controller -t:ValidateDeploySettings
dotnet msbuild DotMatter.Controller -t:Deploy
dotnet msbuild DotMatter.Controller -t:Deploy /p:DeployType=Aot
```

You can still override any setting on the command line, but the intended fast-loop workflow is the ignored local props file.

## One-Time Pi Setup

### 1. Create directories and service account

```bash
sudo useradd --system --home /var/lib/.dot-matter --shell /usr/sbin/nologin dotmatter || true
sudo mkdir -p /opt/dot-matter /opt/dot-matter-aot
sudo chown dotmatter:dotmatter /opt/dot-matter /opt/dot-matter-aot
sudo mkdir -p /var/lib/.dot-matter /etc/dotmatter
sudo chown dotmatter:dotmatter /var/lib/.dot-matter
sudo chmod 750 /etc/dotmatter
```

### 2. Configure Samba shares

Add to `/etc/samba/smb.conf`:

```ini
[dot-matter]
   path = /opt/dot-matter
   browseable = yes
   read only = no
   valid users = dotmatter
   create mask = 0755
   directory mask = 0755

[dot-matter-aot]
   path = /opt/dot-matter-aot
   browseable = yes
   read only = no
   valid users = dotmatter
   create mask = 0755
   directory mask = 0755
```

Set the Samba password and restart:

```bash
sudo smbpasswd -a dotmatter
sudo systemctl restart smbd
```

### 3. Install service files

Copy the service files from the repo to the Pi:

```bash
sudo cp dot-matter.service /etc/systemd/system/
sudo cp dot-matter-aot.service /etc/systemd/system/

sudo systemctl daemon-reload
sudo systemctl enable dot-matter dot-matter-aot
```

Or run the tracked helper script from the repo root to recreate the systemd units,
env examples, and Samba share include file in one step:

```bash
sudo bash scripts/install-dotmatter-system-files.sh
```

### 4. Install runtime env files

Copy the tracked example files and then edit them with host-local values:

```bash
sudo cp dot-matter.env.example /etc/dotmatter/dot-matter.env
sudo cp dot-matter-aot.env.example /etc/dotmatter/dot-matter-aot.env
sudo chown root:dotmatter /etc/dotmatter/dot-matter.env /etc/dotmatter/dot-matter-aot.env
sudo chmod 640 /etc/dotmatter/dot-matter.env /etc/dotmatter/dot-matter-aot.env
```

Recommended contents:

- `dot-matter.env`: production-safe framework-dependent overrides including a real `Controller__Security__ApiKey`
- `dot-matter-aot.env`: production overrides including a real `Controller__Security__ApiKey`
- For trusted-LAN development only, add `DOTNET_ENVIRONMENT=Development` and `Controller__Security__RequireApiKey=false` to the local env file. Do not use that mode on untrusted networks.

### 5. Grant OTBR sudo access

The controller needs to call `ot-ctl` via sudo. Create `/etc/sudoers.d/dotmatter-otbr`:

```text
dotmatter ALL=(ALL) NOPASSWD: /usr/sbin/ot-ctl
```

## Deploy Commands

All deployment is done through MSBuild targets. The host/share/user/password values come from the ignored local props file or explicit `/p:` overrides.

```powershell
cd <repo-root>

# Validate that local deploy settings are present
dotnet msbuild DotMatter.Controller -t:ValidateDeploySettings

# Debug (framework-dependent) deploy
dotnet msbuild DotMatter.Controller -t:Deploy

# AOT deploy (cross-compiles via WSL, then copies the native binary)
dotnet msbuild DotMatter.Controller -t:Deploy /p:DeployType=Aot
```

### What happens during deploy

1. Validates that deploy properties are available
2. Builds the project
3. For AOT, runs `wsl -d <WslDistro> dotnet publish -c Release -r linux-arm64`
4. Stops the target service via SSH
5. Authenticates to the configured Samba share with `net use`
6. Copies files via `robocopy` over Samba (`/MIR` mirrors source to destination)
7. Starts the target service via SSH

Because the service units use `EnvironmentFile=`, runtime tuning stays on the Pi and does not require editing tracked repo files before deploy.

The included service files default to production-safe settings:

- `dot-matter.service` → `DOTNET_ENVIRONMENT=Production`
- `dot-matter-aot.service` → `DOTNET_ENVIRONMENT=Production`

Because the tracked base config no longer includes an API key, both services fail closed until the env file supplies `Controller__Security__ApiKey`. Only opt into `Development` locally when you intentionally want the tracked development overrides.

## Reverse Proxy / TLS

`DotMatter.Controller` does not terminate public TLS itself. A public or semi-public deployment should use a reverse proxy such as nginx, Caddy, or Traefik:

```text
[Client] -- HTTPS --> [Reverse proxy / TLS termination] -- HTTP trusted LAN --> [dot-matter:5000]
```

Keep `/etc/dotmatter/*.env` readable only by root and the `dotmatter` group:

```bash
sudo chown root:dotmatter /etc/dotmatter/dot-matter.env /etc/dotmatter/dot-matter-aot.env
sudo chmod 640 /etc/dotmatter/dot-matter.env /etc/dotmatter/dot-matter-aot.env
```

Generate API keys with a cryptographically random source, for example:

```bash
openssl rand -hex 32
```

## Switching Between Services

Starting one service automatically stops the other:

```bash
# Manual switching on the Pi
sudo systemctl start dot-matter       # stops dot-matter-aot
sudo systemctl start dot-matter-aot   # stops dot-matter

# Check which service is running
systemctl is-active dot-matter dot-matter-aot
```

Deploying via MSBuild handles this automatically.

## AOT Publishing

AOT cross-compilation from Windows requires WSL with a Linux distro.

### WSL setup (one-time)

```powershell
# Install WSL with Debian (or another distro)
wsl --install Debian
```

Inside WSL:

```bash
# Install .NET 10 SDK
# Follow https://learn.microsoft.com/dotnet/core/install/linux-debian

# Install the cross-compilation toolchain
sudo apt install gcc-aarch64-linux-gnu
```

### How AOT deploy works

When you run `dotnet msbuild -t:Deploy /p:DeployType=Aot`:

1. `dotnet build` runs on Windows for restore and project evaluation
2. `wsl -d <WslDistro> dotnet publish -c Release -r linux-arm64` runs inside WSL for native compilation
3. The published binary is copied to the configured Pi share via Samba
4. The service starts on the Pi

The AOT binary requires no .NET runtime on the Pi.

## Data Paths

| Path | Purpose |
|------|---------|
| `/var/lib/.dot-matter/fabrics` | Matter fabric keys and node info |
| `/etc/dotmatter/dot-matter.env` | Debug service environment overrides |
| `/etc/dotmatter/dot-matter-aot.env` | AOT service environment overrides |
| `/opt/dot-matter` | Debug deployment directory |
| `/opt/dot-matter-aot` | AOT deployment directory |

## Customization

To customize the deployment model for your environment, update:

- `DotMatter.Controller/DotMatter.Controller.Deploy.local.props` for host/share/user/password and optional `WslDistro`
- `/etc/dotmatter/*.env` for host-local runtime configuration
- `dot-matter.service` / `dot-matter-aot.service` only if you need a different service account or filesystem layout
- `/etc/samba/smb.conf` if you use different share names or credentials

## Troubleshooting

```bash
# Check service status
sudo systemctl status dot-matter

# View logs
sudo journalctl -u dot-matter -f

# Check which service is active
systemctl is-active dot-matter dot-matter-aot

# Verify Samba shares
smbclient -L //<pi-host> -U dotmatter

# Test SSH from Windows
ssh <pi-user>@<pi-host> "echo ok"
```
