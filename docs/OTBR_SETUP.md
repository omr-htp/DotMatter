# OpenThread Border Router (OTBR) Setup

Setup guide for installing and configuring OTBR on a Raspberry Pi for use with DotMatter.

## Installation

```bash
sudo apt update
sudo apt upgrade -y
sudo apt install -y git

git clone https://github.com/openthread/ot-br-posix
cd ot-br-posix
sudo ./script/bootstrap
```

## Configuration

Find your network interface:

```bash
ip -br addr
```

Run the setup script (replace `wlan0` with your interface):

```bash
sudo INFRA_IF_NAME=wlan0 ./script/setup
```

## Running

The OTBR agent needs three things:
- **`-I wpan0`** — the Thread (802.15.4) network interface created by the agent
- **`-B wlan0`** — the backbone interface (your Pi's WiFi or Ethernet). Use `ip -br addr` to find the correct name (e.g., `wlan0`, `eth0`, `end0`)
- **`spinel+hdlc+uart:///dev/ttyACM1`** — the serial port of your RCP radio. Run `ls /dev/ttyACM*` or `ls /dev/ttyUSB*` to find it. If you have multiple USB devices, the port number may change — use `udevadm info -a /dev/ttyACM1` to identify the correct device

```bash
cd ~/ot-br-posix

# Replace wlan0 and ttyACM1 with your actual interface and serial port
sudo OTBR_AGENT_OPTS="-I wpan0 -B wlan0 spinel+hdlc+uart:///dev/ttyACM1?uart-baudrate=460800" ./script/server
```

> **Tip:** To find your RCP radio port, unplug it, run `ls /dev/ttyACM*`, plug it back in, and run again — the new entry is your radio.

## Verification

Check the agent status:

```bash
sudo journalctl -u otbr-agent -n 50 --no-pager
```

## Thread Network Setup

```bash
# Check current state
sudo ot-ctl state

# Initialize and start Thread network
sudo ot-ctl dataset init new
sudo ot-ctl dataset commit active
sudo ot-ctl ifconfig up
sudo ot-ctl thread start

# Verify state (should show "leader" or "router")
sudo ot-ctl state

# Get active dataset (needed for commissioning)
sudo ot-ctl dataset active -x
```
