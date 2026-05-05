## Workflow
- Never run `git commit`, `git push`, or create branches unless I explicitly ask.

## Build
- Use `dotnet build -c Debug` unless I explicitly ask for a different configuration.
- Do not use `Release` unless the task is specifically deployment.

## Temporary Validation
- For small temporary test scripts or quick checks, prefer C#/.NET first. `dotnet run app.cs`
- Do not use Python or PowerShell if the same validation can reasonably be done with .NET.
- Only use Python or PowerShell when they are clearly better suited for the task.

### Fast deploy
- Use: `dotnet build -t:Deploy` or `dotnet build -t:Deploy /p:DeployType=Fast`
- Remote service: `dot-matter.service`
- Deploy settings come from the ignored local `DotMatter.Controller.Deploy.local.props`

### AOT deploy
- Use: `dotnet build -t:Deploy /p:DeployType=Aot`
- Remote service: `dot-matter-aot.service`
- Deploy settings come from the ignored local `DotMatter.Controller.Deploy.local.props`

## Raspberry PI
- Remote host/share credentials are local-only and must not be added to tracked files

## ESP32-H2
- Use the locally configured serial port and do not commit local serial-port settings or WiFi credentials.
- Useful console commands:
	- BLE Start: matter ble start
	- BLE Stop: matter ble stop
	- BLE State: matter ble state
	- Wi-Fi Mode Disable: matter wifi mode disable
	- Wi-Fi Mode AP: matter wifi mode ap
	- Wi-Fi Mode STA: matter wifi mode sta
	- Wi-Fi Connect: matter esp wifi connect <ssid> <password>
	- Device Configuration: matter config
	- Onboarding Codes: matter onboardingcodes
	- Factory Reset: matter esp factoryreset
	- Get Attribute: matter esp attribute get <endpoint_id> <cluster_id> <attribute_id>
	- Set Attribute: matter esp attribute set <endpoint_id> <cluster_id> <attribute_id> <value>
	- Diagnostics Memory Dump: matter esp diagnostics mem-dump
	- Bridge Command: matter esp bridge <command>
	- Bridge Add Device: matter esp bridge add <parent_endpoint_id> <device_type_id>
