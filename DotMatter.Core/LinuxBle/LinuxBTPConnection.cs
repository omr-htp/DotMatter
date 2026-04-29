using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Tmds.DBus.Protocol;

namespace DotMatter.Core.LinuxBle;

/// <summary>
/// BLE Transport Protocol (BTP) connection for Linux using BlueZ D-Bus API.
/// Implements IConnection for use with Matter commissioning over BLE.
/// </summary>
public partial class LinuxBTPConnection : IConnection
{

    private const string MatterC1Uuid = "18ee2ef5-263d-4559-959f-4f9c429f9d11"; // Write (client→server)
    private const string MatterC2Uuid = "18ee2ef5-263d-4559-959f-4f9c429f9d12"; // Indicate (server→client)

    private DBusConnection? _dbusConnection;
    private Device1? _device;
    private GattCharacteristic1? _writeChar;
    private GattCharacteristic1? _indicateChar;
    private IDisposable? _notifyWatcher;
    private string? _devicePath;
    private string? _c2Path;

    // BTP state
    private ushort _attMtu = 247;
    private byte _peerWindowSize = 1;
    private byte _sentSeqNum;
    private byte _recvSeqNum;
    private byte _ackedSeqNum;
    private bool _handshakeDone;

    private readonly Channel<byte[]> _incomingBtpFrames = Channel.CreateBounded<byte[]>(20);
    private readonly Channel<byte[]> _reassembledMessages = Channel.CreateBounded<byte[]>(10);
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _closeLock = new();
    private readonly Timer _ackTimer;
    private readonly LinuxBleOptions _options;
    private Task? _reassemblyTask;
    private Task? _bluetoothctlOutputTask;

    /// <summary>ConnectionClosed.</summary>
    /// <summary>Raised when ConnectionClosed occurs.</summary>
    public event EventHandler? ConnectionClosed;

    /// <summary>LinuxBTPConnection.</summary>
    public LinuxBTPConnection(LinuxBleOptions? options = null)
    {
        _options = options ?? new LinuxBleOptions();
        _ackTimer = new Timer(static state =>
        {
            if (state is LinuxBTPConnection connection)
            {
                _ = connection.SendStandaloneAckAsync();
            }
        }, this, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Scan for a Matter device, connect via BLE, and perform BTP handshake.
    /// </summary>
    public async Task<bool> ConnectAsync(int discriminator, bool isShortDiscriminator = false, CancellationToken ct = default)
    {
        try
        {
            if (await TryDbusConnectAsync(discriminator, isShortDiscriminator, ct))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            MatterDiagnostics.TransportFailures.Add(1);
            MatterLog.Info("[BLE] D-Bus direct connect failed: {0}", ex.Message);
        }

        try
        {
            MatterLog.Info("[BLE] Falling back to bluetoothctl...");
            return await BluetoothctlConnectAsync(discriminator, isShortDiscriminator, ct);
        }
        catch (Exception ex)
        {
            MatterDiagnostics.TransportFailures.Add(1);
            MatterLog.Info("[BLE] bluetoothctl connect failed: {0}", ex.Message);
        }

        return false;
    }

    #region D-Bus Direct Approach

    private async Task<bool> TryDbusConnectAsync(int discriminator, bool isShort, CancellationToken ct)
    {
        _dbusConnection = new DBusConnection(DBusAddress.System!);
        await _dbusConnection.ConnectAsync();
        MatterLog.Info("[BLE] D-Bus system bus connected");

        var objectManager = new ObjectManager(_dbusConnection, "org.bluez", "/");
        var objects = await objectManager.GetManagedObjectsAsync();

        // Find adapter
        string adapterPath = "";
        foreach (var (path, ifaces) in objects)
        {
            if (ifaces.ContainsKey("org.bluez.Adapter1"))
            {
                adapterPath = path;
                break;
            }
        }

        if (string.IsNullOrEmpty(adapterPath))
        {
            throw new MatterTransportException("No Bluetooth adapter found");
        }

        var adapter = new Adapter1(_dbusConnection, "org.bluez", adapterPath);
        MatterLog.Info("[BLE] Adapter: {0}", adapterPath);

        // Phase 1: Check already-discovered devices that are ACTIVELY advertising (have RSSI)
        // Devices without RSSI are stale cache entries — skip them to avoid connecting to old addresses
        string? targetPath = null;
        foreach (var (path, ifaces) in objects)
        {
            if (ifaces.TryGetValue("org.bluez.Device1", out var props) &&
                ((string)path).StartsWith(adapterPath))
            {
                if (!props.ContainsKey("RSSI"))
                {
                    if (CheckMatterDiscriminator(props, discriminator, isShort))
                    {
                        var addr = props.TryGetValue("Address", out var a) ? a.GetString() : "?";
                        MatterLog.Info("[BLE] Skipping stale cached device {0} (no RSSI)", addr);
                    }
                    continue;
                }
                if (CheckMatterDiscriminator(props, discriminator, isShort))
                {
                    targetPath = path;
                    MatterLog.Info("[BLE] Found active Matter device: {0}", targetPath);
                    break;
                }
            }
        }

        // Phase 2: If not found, clear non-Matter stale devices and scan fresh
        if (targetPath == null)
        {
            // Only remove stale cached Matter devices discovered under this adapter.
            foreach (var (path, ifaces) in objects)
            {
                if (ifaces.TryGetValue("org.bluez.Device1", out Dictionary<string, VariantValue>? devProps) &&
                    ((string)path).StartsWith(adapterPath))
                {
                    if (devProps.TryGetValue("Connected", out var c) && c.GetBool())
                    {
                        continue;
                    }

                    if (!HasMatterServiceData(devProps))
                    {
                        continue;
                    }

                    try { await adapter.RemoveDeviceAsync(path); } catch { }
                }
            }
            MatterLog.Info("[BLE] Cleared stale Matter devices");

            // Set LE discovery filter
            try
            {
                await adapter.SetDiscoveryFilterAsync(new Dictionary<string, VariantValue>
                {
                    { "Transport", VariantValue.String("le") }
                });
            }
            catch (Exception ex)
            {
                MatterLog.Info("[BLE] SetDiscoveryFilter failed (continuing): {0}", ex.Message);
            }

            MatterLog.Info("[BLE] Scanning for Matter device (discriminator={0})...", discriminator);
            await adapter.StartDiscoveryAsync();

            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            scanCts.CancelAfter(_options.ScanTimeout);

            try
            {
                for (int poll = 0; poll < 30; poll++)
                {
                    await Task.Delay(2000, scanCts.Token);

                    var current = await objectManager.GetManagedObjectsAsync();
                    int deviceCount = 0;
                    int withServiceData = 0;
                    int totalObjects = 0;

                    foreach (var (path, ifaces) in current)
                    {
                        totalObjects++;
                        if (ifaces.TryGetValue("org.bluez.Device1", out var props) &&
                            ((string)path).StartsWith(adapterPath))
                        {
                            deviceCount++;
                            if (props.ContainsKey("ServiceData"))
                            {
                                withServiceData++;
                            }

                            if (poll == 0 && deviceCount <= 5)
                            {
                                var addr = props.TryGetValue("Address", out var a) ? a.GetString() : "?";
                                MatterLog.Info("[BLE]   Dev: {0} hasSD={1}", addr, props.ContainsKey("ServiceData"));
                            }
                            if (props.ContainsKey("RSSI") && CheckMatterDiscriminator(props, discriminator, isShort))
                            {
                                targetPath = path;
                                break;
                            }
                        }
                    }

                    if (poll == 0)
                    {
                        MatterLog.Info("[BLE] Poll {0}: {1} objects, {2} device(s), {3} with ServiceData (adapter={4})", poll + 1, totalObjects, deviceCount, withServiceData, adapterPath);
                    }
                    else
                    {
                        MatterLog.Info("[BLE] Poll {0}: {1} device(s), {2} with ServiceData", poll + 1, deviceCount, withServiceData);
                    }

                    if (targetPath != null)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                try { await adapter.StopDiscoveryAsync(); } catch { }
            }
        }

        if (targetPath == null)
        {
            MatterLog.Info("[BLE] No Matter device found after polling");
            return false;
        }

        _devicePath = targetPath;
        MatterLog.Info("[BLE] Found Matter device! Connecting to {0}...", _devicePath);

        _device = new Device1(_dbusConnection, "org.bluez", _devicePath);

        // Connect with retry for intermittent BLE failures (e.g., le-connection-abort-by-local)
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_options.ConnectAttemptTimeout);
            try
            {
                await _device.ConnectAsync().WaitAsync(connectCts.Token);
                break; // Success
            }
            catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                MatterLog.Info("[BLE] Connect attempt {0}/{1} failed: {2}", attempt, maxRetries, ex.Message);
                try { await _device.DisconnectAsync(); } catch { }

                // Remove stale device entry
                try { await adapter.RemoveDeviceAsync(_devicePath); } catch { }
                await Task.Delay(2000, ct);

                // Re-start scanning so the device gets re-discovered
                try { await adapter.StartDiscoveryAsync(); } catch { }
                await Task.Delay(4000, ct); // Wait for BLE advertisements
                try { await adapter.StopDiscoveryAsync(); } catch { }

                // Find the device again (may have same or different address)
                var freshAddr = await FindMatterDeviceViaDBus(discriminator, isShort);
                if (freshAddr != null)
                {
                    _devicePath = $"/org/bluez/hci0/dev_{freshAddr.Replace(':', '_')}";
                    MatterLog.Info("[BLE] Retry with fresh device path: {0}", _devicePath);
                }
                else
                {
                    MatterLog.Info("[BLE] Retry: device not re-discovered, trying same path");
                }
                _device = new Device1(_dbusConnection, "org.bluez", _devicePath);
            }
            catch
            {
                // Final attempt failed — clean up
                try { await _device.DisconnectAsync(); } catch { }
                try { await adapter.RemoveDeviceAsync(_devicePath); } catch { }
                _device = null;
                _devicePath = null;
                throw;
            }
        }

        MatterLog.Info("[BLE] Connected!");

        await WaitForServicesResolvedAsync(ct);
        await SetupGattAndBtpAsync(ct);

        return _handshakeDone;
    }

    #endregion

    #region bluetoothctl Fallback

    private async Task<bool> BluetoothctlConnectAsync(int discriminator, bool isShort = false, CancellationToken ct = default)
    {
        MatterLog.Info("[BLE] Using interactive bluetoothctl for scan+connect...");

        // Start a single interactive bluetoothctl process
        var psi = new ProcessStartInfo("bluetoothctl")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var proc = Process.Start(psi)!;
        var outputLines = new List<string>();
        var outputLock = new object();

        // Read output in background
        _bluetoothctlOutputTask = ReadBluetoothctlOutputAsync(proc, outputLines, outputLock);

        await Task.Delay(1000, ct); // Let bluetoothctl initialize

        // Set scan filter for BLE only
        await SendCmd(proc, "menu scan");
        await Task.Delay(500, ct);
        await SendCmd(proc, "transport le");
        await Task.Delay(500, ct);
        await SendCmd(proc, "back");
        await Task.Delay(500, ct);

        // Start scanning
        await SendCmd(proc, "scan on");

        // Wait for devices to appear and find our Matter device
        string? deviceAddr = null;
        var scanDeadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < scanDeadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);

            // Check D-Bus for discovered devices with ServiceData
            deviceAddr = await FindMatterDeviceViaDBus(discriminator, isShort);
            if (deviceAddr != null)
            {
                MatterLog.Info("[BLE] Found Matter device at {0}", deviceAddr);
                break;
            }
        }

        if (deviceAddr == null)
        {
            MatterLog.Info("[BLE] No Matter device found during scan");
            try { proc.Kill(); } catch { }
            proc.Dispose();
            return false;
        }

        // Stop scanning and connect (with retry for intermittent BLE failures)
        await SendCmd(proc, "scan off");
        await Task.Delay(1000, ct);

        bool connected = false;
        const int btctlMaxRetries = 3;

        for (int attempt = 1; attempt <= btctlMaxRetries && !ct.IsCancellationRequested; attempt++)
        {
            // Clear output to watch for connection result
            lock (outputLock) { outputLines.Clear(); }

            await SendCmd(proc, $"connect {deviceAddr}");

            // Wait for "Connection successful"
            var connectDeadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < connectDeadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                lock (outputLock)
                {
                    if (outputLines.Any(l => l.Contains("Connection successful", StringComparison.OrdinalIgnoreCase)))
                    {
                        connected = true;
                        break;
                    }
                    if (outputLines.Any(l => l.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)))
                    {
                        break;
                    }
                }
            }

            if (connected)
            {
                break;
            }

            if (attempt < btctlMaxRetries)
            {
                MatterLog.Info("[BLE] bluetoothctl: connect attempt {0}/{1} failed, removing and re-scanning...", attempt, btctlMaxRetries);

                // Remove stale device and re-scan
                await SendCmd(proc, $"remove {deviceAddr}");
                await Task.Delay(1000, ct);
                await SendCmd(proc, "scan on");
                await Task.Delay(_options.BluetoothctlScanRetryDelay, ct);

                // Find device again (may have new address)
                var freshAddr = await FindMatterDeviceViaDBus(discriminator, isShort);
                if (freshAddr != null)
                {
                    deviceAddr = freshAddr;
                    MatterLog.Info("[BLE] Re-discovered device at {0}", deviceAddr);
                }

                await SendCmd(proc, "scan off");
                await Task.Delay(1000, ct);
            }
            else
            {
                MatterLog.Info("[BLE] bluetoothctl: connection failed after {0} attempts", btctlMaxRetries);
            }
        }

        // Quit bluetoothctl (but keep the connection alive in BlueZ)
        await SendCmd(proc, "quit");
        await Task.Delay(500, CancellationToken.None);
        if (!proc.HasExited) { try { proc.Kill(); } catch { } }
        if (_bluetoothctlOutputTask != null)
        {
            try { await _bluetoothctlOutputTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None); } catch { }
            _bluetoothctlOutputTask = null;
        }
        proc.Dispose();

        if (!connected)
        {
            MatterLog.Info("[BLE] bluetoothctl: could not connect to device");
            return false;
        }

        MatterLog.Info("[BLE] bluetoothctl connected to {0}!", deviceAddr);
        _devicePath = $"/org/bluez/hci0/dev_{deviceAddr.Replace(':', '_')}";

        // Use D-Bus for GATT (device is already connected in BlueZ)
        if (_dbusConnection == null)
        {
            _dbusConnection = new DBusConnection(DBusAddress.System!);
            await _dbusConnection.ConnectAsync();
        }

        _device = new Device1(_dbusConnection, "org.bluez", _devicePath);

        await WaitForServicesResolvedAsync(ct);
        await SetupGattAndBtpAsync(ct);

        return _handshakeDone;
    }

    /// <summary>
    /// Query D-Bus for already-discovered devices to find one with matching Matter discriminator.
    /// This works even when bluetoothctl did the scan, since BlueZ D-Bus state is shared.
    /// </summary>
    private static async Task<string?> FindMatterDeviceViaDBus(int discriminator, bool isShort = false)
    {
        try
        {
            using var conn = new DBusConnection(DBusAddress.System!);
            await conn.ConnectAsync();

            var objectManager = new ObjectManager(conn, "org.bluez", "/");
            var objects = await objectManager.GetManagedObjectsAsync();

            foreach (var (path, ifaces) in objects)
            {
                if (ifaces.TryGetValue("org.bluez.Device1", out var props))
                {
                    // Require RSSI to ensure device is actively advertising, not stale cache
                    if (props.ContainsKey("RSSI") && CheckMatterDiscriminator(props, discriminator, isShort))
                    {
                        // Extract MAC address from path: /org/bluez/hci0/dev_XX_XX_XX_XX_XX_XX
                        var devPart = ((string)path).Split('/').Last();
                        if (devPart.StartsWith("dev_"))
                        {
                            return devPart[4..].Replace('_', ':');
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MatterLog.Info("[BLE] D-Bus device query error: {0}", ex.Message);
        }
        return null;
    }

    private static async Task SendCmd(Process proc, string command)
    {
        await proc.StandardInput.WriteLineAsync(command);
        await proc.StandardInput.FlushAsync();
    }

    private static string StripAnsi(string text)
    {
        return AnsiEscapePattern().Replace(text, "");
    }

    [GeneratedRegex(@"\x1B\[[^@-~]*[@-~]")]
    private static partial Regex AnsiEscapePattern();

    #endregion

    #region GATT + BTP

    private async Task WaitForServicesResolvedAsync(CancellationToken ct)
    {
        MatterLog.Info("[BLE] Waiting for GATT services to resolve...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.GattResolveTimeout);

        for (int i = 0; i < 40; i++) // 40 × 500ms = 20s max
        {
            try
            {
                if (await _device!.GetServicesResolvedAsync())
                {
                    MatterLog.Info("[BLE] Services resolved after {0}ms", i * 500);
                    return;
                }

                // Check if still connected
                if (!await _device.GetConnectedAsync())
                {
                    MatterLog.Info("[BLE] Device disconnected while waiting for services");
                    throw new MatterTransportException("BLE device disconnected during GATT discovery");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (i == 0)
                {
                    MatterLog.Info("[BLE] GetServicesResolved poll error: {0}", ex.Message);
                }
            }

            await Task.Delay(500, cts.Token);
        }

        throw new MatterTimeoutException($"GATT services did not resolve within {_options.GattResolveTimeout.TotalSeconds:0} seconds");
    }

    private async Task SetupGattAndBtpAsync(CancellationToken ct)
    {
        var objectManager = new ObjectManager(_dbusConnection!, "org.bluez", "/");
        var objects = await objectManager.GetManagedObjectsAsync();

        string? c1Path = null, c2Path = null;

        foreach (var (path, ifaces) in objects)
        {
            if (!((string)path).StartsWith(_devicePath!))
            {
                continue;
            }

            if (ifaces.TryGetValue("org.bluez.GattCharacteristic1", out var charProps))
            {
                if (charProps.TryGetValue("UUID", out var uuidVal))
                {
                    var uuid = uuidVal.GetString();
                    if (string.Equals(uuid, MatterC1Uuid, StringComparison.OrdinalIgnoreCase))
                    {
                        c1Path = path;
                        MatterLog.Info("[BLE] Found C1 (write): {0}", c1Path);
                    }
                    else if (string.Equals(uuid, MatterC2Uuid, StringComparison.OrdinalIgnoreCase))
                    {
                        c2Path = path;
                        MatterLog.Info("[BLE] Found C2 (indicate): {0}", c2Path);
                    }
                }
            }
        }

        if (c1Path == null || c2Path == null)
        {
            throw new MatterTransportException($"Matter GATT characteristics not found (C1={c1Path != null}, C2={c2Path != null})");
        }

        _writeChar = new GattCharacteristic1(_dbusConnection!, "org.bluez", c1Path);
        _indicateChar = new GattCharacteristic1(_dbusConnection!, "org.bluez", c2Path);
        _c2Path = c2Path;

        // Log the characteristic properties
        try
        {
            var c1Flags = await _writeChar.GetFlagsAsync();
            MatterLog.Info("[BLE] C1 flags: {0}", string.Join(", ", c1Flags));
        }
        catch (Exception ex)
        {
            MatterLog.Info("[BLE] Could not read C1 flags: {0}", ex.Message);
        }
        try
        {
            var c2Flags = await _indicateChar.GetFlagsAsync();
            MatterLog.Info("[BLE] C2 flags: {0}", string.Join(", ", c2Flags));
        }
        catch (Exception ex)
        {
            MatterLog.Info("[BLE] Could not read C2 flags: {0}", ex.Message);
        }

        // BTP Handshake
        await DoBtpHandshakeAsync(ct);

        // Start reassembly loop
        _reassemblyTask = ReassembleLoopAsync(_cts.Token);

        // Start ACK timer
        _ackTimer.Change(_options.AckInitialDelay, _options.AckPeriod);
    }

    private async Task DoBtpHandshakeAsync(CancellationToken ct)
    {
        // BTP Handshake Request
        var handshakeReq = new byte[9];
        handshakeReq[0] = 0x65; // Flags: Handshake(0x20) | Management(0x40) | Ending(0x04) | Beginning(0x01)
        handshakeReq[1] = 0x6C; // Management opcode: Handshake Request
        handshakeReq[2] = 0x04; // BTP version range: min=0, max=4
        handshakeReq[3] = 0x00;
        handshakeReq[4] = 0x00;
        handshakeReq[5] = 0x00;
        handshakeReq[6] = 0x00; // ATT_MTU low (0 = use negotiated)
        handshakeReq[7] = 0x00; // ATT_MTU high
        handshakeReq[8] = 0x06; // Client window size

        MatterLog.Info("[BTP] Sending handshake request: {0}",
            MatterLog.FormatBytes(handshakeReq));

        // CRITICAL ordering — CHIP SDK BLEEndPoint.cpp flow:
        //   1. HandleCapabilitiesRequestReceived() — C1 write → queues response in mSendQueue
        //   2. HandleSubscribeReceived() — CCCD write → sends queued response via indication
        // If subscribe arrives BEFORE handshake write, mSendQueue is empty and the peripheral
        // fails with CHIP_ERROR_INCORRECT_STATE, closing the endpoint silently.
        //
        // Correct order: (1) register D-Bus watcher, (2) write handshake, (3) StartNotify (CCCD).

        // Step 1: Register D-Bus property watcher (local signal handler — no device interaction)
        _notifyWatcher = await _indicateChar!.WatchPropertiesChangedAsync((ex, changes) =>
        {
            if (ex != null)
            {
                MatterLog.Info("[BTP] Watcher error: {0}", ex.Message);
                return;
            }

            MatterLog.Info("[BTP] PropertiesChanged signal! HasValueChanged={0}", changes.HasValueChanged);

            if (changes.HasValueChanged)
            {
                try
                {
                    var data = changes.Value;
                    if (data != null && data.Length > 0)
                    {
                        MatterLog.Info("[BTP] Indication received {0} bytes: {1}",
                            data.Length, MatterLog.FormatBytes(data));
                        _incomingBtpFrames.Writer.TryWrite(data);
                    }
                    else
                    {
                        MatterLog.Info("[BTP] Value property was null/empty");
                    }
                }
                catch (Exception valEx)
                {
                    MatterLog.Info("[BTP] Could not read Value: {0}", valEx.Message);
                }
            }
        }, emitOnCapturedContext: false);

        // Step 2: Write handshake request FIRST — peripheral queues the response
        try
        {
            var writeOptions = new Dictionary<string, VariantValue>
            {
                { "type", VariantValue.String("request") }
            };
            await _writeChar!.WriteValueAsync(handshakeReq, writeOptions);
            MatterLog.Info("[BTP] Handshake request written (write-with-response)");
        }
        catch (Exception ex)
        {
            MatterLog.Info("[BTP] Write-with-response failed ({0}), trying write-without-response...", ex.Message);
            try
            {
                var writeOptions = new Dictionary<string, VariantValue>
                {
                    { "type", VariantValue.String("command") }
                };
                await _writeChar!.WriteValueAsync(handshakeReq, writeOptions);
                MatterLog.Info("[BTP] Handshake request written (write-without-response)");
            }
            catch (Exception ex2)
            {
                MatterLog.Info("[BTP] Write-without-response also failed ({0}), trying no options...", ex2.Message);
                await _writeChar!.WriteValueAsync(handshakeReq, []);
                MatterLog.Info("[BTP] Handshake request written (no options)");
            }
        }

        // Step 3: Subscribe (CCCD write) — peripheral sends queued response via indication
        await _indicateChar!.StartNotifyAsync();
        MatterLog.Info("[BLE] StartNotify activated — peripheral should now send handshake response");

        // Step 4: Wait for handshake response
        MatterLog.Info("[BTP] Waiting for handshake response...");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.HandshakeTimeout);

        try
        {
            var response = await _incomingBtpFrames.Reader.ReadAsync(cts.Token);
            MatterLog.Info("[BTP] Handshake response: {0}", MatterLog.FormatBytes(response));

            if (response.Length >= 6 && (response[0] & 0x20) != 0)
            {
                int idx = 1;
                if ((response[0] & 0x40) != 0)
                {
                    idx++;
                }

                var version = response[idx++];
                _attMtu = BitConverter.ToUInt16(response, idx);
                idx += 2;
                _peerWindowSize = response[idx];

                MatterLog.Info("[BTP] Handshake OK — Version={0}, ATT_MTU={1}, WindowSize={2}",
                    version, _attMtu, _peerWindowSize);

                _handshakeDone = true;
                _sentSeqNum = 0;
                _recvSeqNum = 0;
                _ackedSeqNum = 0;
            }
            else
            {
                MatterLog.Info("[BTP] Invalid handshake response!");
                _handshakeDone = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Diagnostic: check connection state
            try
            {
                var connected = await _device!.GetConnectedAsync();
                MatterLog.Info("[BTP] TIMEOUT — device connected={0}", connected);
            }
            catch (Exception diagEx)
            {
                MatterLog.Info("[BTP] TIMEOUT — diagnostic failed: {0}", diagEx.Message);
            }

            MatterLog.Info("[BTP] Handshake response TIMEOUT — no indication received in {0}s", _options.HandshakeTimeout.TotalSeconds);
            _handshakeDone = false;
        }
    }

    private async Task ReassembleLoopAsync(CancellationToken ct)
    {
        var segments = new List<byte[]>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await _incomingBtpFrames.Reader.ReadAsync(ct);
                var flags = frame[0];

                var isAck = (flags & 0x08) != 0;
                var isEnd = (flags & 0x04) != 0;
                var isBegin = (flags & 0x01) != 0;
                var isHandshake = (flags & 0x20) != 0;

                if (isHandshake)
                {
                    continue;
                }

                int idx = 1;

                if (isAck)
                {
                    var ackNum = frame[idx++];
                    MatterLog.Info("[BTP] Peer acked seq {0}", ackNum);
                }

                var seqNum = frame[idx++];
                _recvSeqNum = seqNum;

                if (isBegin)
                {
                    var totalLength = BitConverter.ToUInt16(frame, idx);
                    idx += 2;
                    segments.Clear();
                    MatterLog.Info("[BTP] Begin segment, total msg len={0}", totalLength);
                }

                var payload = new byte[frame.Length - idx];
                Buffer.BlockCopy(frame, idx, payload, 0, payload.Length);
                segments.Add(payload);

                if (isEnd)
                {
                    var totalPayload = new byte[segments.Sum(s => s.Length)];
                    int offset = 0;
                    foreach (var seg in segments)
                    {
                        Buffer.BlockCopy(seg, 0, totalPayload, offset, seg.Length);
                        offset += seg.Length;
                    }

                    MatterLog.Info("[BTP] Reassembled message: {0} bytes", totalPayload.Length);
                    await _reassembledMessages.Writer.WriteAsync(totalPayload, ct);
                    segments.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MatterLog.Info("[BTP] Reassembly error: {0}", ex.Message);
        }
    }

    #endregion

    private static async Task ReadBluetoothctlOutputAsync(Process proc, List<string> outputLines, object outputLock)
    {
        try
        {
            var buffer = new char[4096];
            while (!proc.HasExited)
            {
                var count = await proc.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                if (count <= 0)
                {
                    continue;
                }

                var text = new string(buffer, 0, count);
                lock (outputLock)
                {
                    foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var clean = StripAnsi(line).Trim();
                        if (!string.IsNullOrEmpty(clean))
                        {
                            outputLines.Add(clean);
                            MatterLog.Info("[btctl] {0}", clean);
                        }
                    }
                }
            }
        }
        catch
        {
        }
    }

    #region IConnection

    /// <summary>ReadAsync.</summary>
    public async Task<byte[]> ReadAsync(CancellationToken token)
    {
        return await _reassembledMessages.Reader.ReadAsync(token);
    }

    /// <summary>SendAsync.</summary>
    public async Task SendAsync(byte[] message)
    {
        await _writeLock.WaitAsync();
        try
        {
            var segments = SegmentMessage(message);
            MatterLog.Info("[BTP] Sending {0} bytes in {1} segment(s)", message.Length, segments.Length);

            var writeOptions = new Dictionary<string, VariantValue>
            {
                { "type", VariantValue.String("request") }
            };

            foreach (var segment in segments)
            {
                MatterLog.Info("[BTP] TX: {0}", MatterLog.FormatBytes(segment));
                await _writeChar!.WriteValueAsync(segment, writeOptions);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Close.</summary>
    public void Close()
    {
        lock (_closeLock)
        {
            _cts.Cancel();
            _ackTimer.Dispose();
            _notifyWatcher?.Dispose();

            try
            {
                _device?.DisconnectAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                MatterLog.Warn(ex, "[BTP] Device disconnect failed during close.");
            }

            WaitForBackgroundTask(_reassemblyTask, "[BTP] Reassembly task failed during close.");
            _reassemblyTask = null;
            WaitForBackgroundTask(_bluetoothctlOutputTask, "[BTP] bluetoothctl output task failed during close.");
            _bluetoothctlOutputTask = null;

            _dbusConnection?.Dispose();
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void WaitForBackgroundTask(Task? task, string errorMessage)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, errorMessage);
        }
    }

    /// <summary>OpenConnection.</summary>
    public IConnection OpenConnection()
    {
        return this;
    }

    #endregion

    #region BTP Segmentation

    private byte[][] SegmentMessage(byte[] message)
    {
        var segments = new List<byte[]>();
        int offset = 0;

        while (offset < message.Length)
        {
            var isFirst = offset == 0;
            byte flags = 0;

            int headerLen = 2; // flags + seq
            if (isFirst)
            {
                headerLen += 2; // message length
            }

            bool doAck = _ackedSeqNum != _recvSeqNum;
            if (doAck)
            {
                headerLen += 1;
            }

            int maxPayload = _attMtu - 3 - headerLen;
            int payloadLen = Math.Min(message.Length - offset, maxPayload);
            bool isLast = (offset + payloadLen) >= message.Length;

            if (isFirst)
            {
                flags |= 0x01;
            }

            if (!isFirst && !isLast)
            {
                flags |= 0x02;
            }

            if (isLast)
            {
                flags |= 0x04;
            }

            if (doAck)
            {
                flags |= 0x08;
            }

            var frame = new byte[headerLen + payloadLen];
            int idx = 0;

            frame[idx++] = flags;

            if (doAck)
            {
                _ackedSeqNum = _recvSeqNum;
                frame[idx++] = _ackedSeqNum;
            }

            frame[idx++] = _sentSeqNum++;

            if (isFirst)
            {
                frame[idx++] = (byte)(message.Length & 0xFF);
                frame[idx++] = (byte)((message.Length >> 8) & 0xFF);
            }

            Buffer.BlockCopy(message, offset, frame, idx, payloadLen);
            offset += payloadLen;

            segments.Add(frame);
        }

        return [.. segments];
    }

    #endregion

    #region ACK

    private async Task SendStandaloneAckAsync()
    {
        if (!_handshakeDone || _cts.IsCancellationRequested)
        {
            return;
        }

        if (_ackedSeqNum == _recvSeqNum)
        {
            return;
        }

        try
        {
            await _writeLock.WaitAsync(_cts.Token);
            try
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                _ackedSeqNum = _recvSeqNum;

                var ackFrame = new byte[3];
                ackFrame[0] = 0x08; // ACK flag only
                ackFrame[1] = _ackedSeqNum;
                ackFrame[2] = _sentSeqNum++;

                MatterLog.Info("[BTP] Sending standalone ACK for seq {0}", _ackedSeqNum);

                var writeOptions = new Dictionary<string, VariantValue>
                {
                    { "type", VariantValue.String("command") }
                };
                await _writeChar!.WriteValueAsync(ackFrame, writeOptions);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MatterLog.Debug("[BTP] ACK send error: {0}", ex.Message);
        }
    }

    #endregion

    #region Helpers

    private static bool CheckMatterDiscriminator(
        Dictionary<string, VariantValue> deviceProps, int discriminator, bool isShort = false)
    {
        if (!deviceProps.TryGetValue("ServiceData", out var sdVariant))
        {
            return false;
        }

        // ServiceData is a{sv} — iterate dict entries looking for Matter BLE UUID
        try
        {
            var serviceData = sdVariant.GetDictionary<string, VariantValue>();
            foreach (var (uuid, value) in serviceData)
            {
                if (!uuid.Contains("fff6", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byte[]? data;
                try
                {
                    data = value.GetArray<byte>();
                }
                catch
                {
                    continue;
                }

                if (data == null || data.Length < 3)
                {
                    continue;
                }

                // Matter BLE ServiceData format (from connectedhomeip CHIPBleServiceData.h):
                //   Byte 0: OpCode (0x00 = commissionable)
                //   Bytes 1-2 (LE uint16): bits [0:11] = discriminator, bits [12:15] = adv version
                //   Bytes 3-4: Vendor ID (LE uint16)
                //   Bytes 5-6: Product ID (LE uint16)
                //   Byte 7: Additional Data Flag
                var discAndVersion = (ushort)(data[1] | (data[2] << 8));
                var disc = discAndVersion & 0x0FFF;

                MatterLog.Info("[BLE] ServiceData ({0}): {1}", uuid, MatterLog.FormatBytes(data));
                MatterLog.Info("[BLE] Parsed discriminator={0} (looking for {1}, short={2})", disc, discriminator, isShort);

                if (isShort ? (disc >> 8) == discriminator : disc == discriminator)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            MatterLog.Info("[BLE] ServiceData parse error: {0}", ex.Message);
        }

        return false;
    }

    private static void DumpDeviceProps(Dictionary<string, VariantValue> props)
    {
        if (props.TryGetValue("Address", out var addr))
        {
            MatterLog.Info("[BLE]   Address: {0}", addr.GetString());
        }

        if (props.TryGetValue("Name", out var name))
        {
            MatterLog.Info("[BLE]   Name: {0}", name.GetString());
        }

        if (props.TryGetValue("UUIDs", out var uuids))
        {
            try
            {
                var uuidArr = uuids.GetArray<string>();
                MatterLog.Info("[BLE]   UUIDs: {0}", string.Join(", ", uuidArr));
            }
            catch { }
        }
        if (props.ContainsKey("ServiceData"))
        {
            MatterLog.Info("[BLE]   ServiceData present");
        }
    }

    private static bool HasMatterServiceData(Dictionary<string, VariantValue> deviceProps)
    {
        if (!deviceProps.TryGetValue("ServiceData", out var sdVariant))
        {
            return false;
        }

        try
        {
            var serviceData = sdVariant.GetDictionary<string, VariantValue>();
            return serviceData.Keys.Any(uuid => uuid.Contains("fff6", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

