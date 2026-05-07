using System.Net;
using System.Security.Cryptography;
using System.Text;
using DotMatter.Core.Certificates;
using DotMatter.Core.Clusters;
using DotMatter.Core.Cryptography;
using DotMatter.Core.Discovery;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace DotMatter.Core.Commissioning;

/// <summary>Generated Node Operational Certificate and its serial number.</summary>
public sealed record GeneratedNoc(X509Certificate Noc, BigInteger Serial);

/// <summary>
/// Transport-agnostic Matter commissioning engine.
/// Implements the full commissioning flow: PASE → CSR → NOC → Thread → CASE → Complete.
/// </summary>
public class MatterCommissioner
{
    private const int OperationalCaseMaxAttempts = 3;
    private static readonly TimeSpan _operationalCaseAttemptTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger _log;

    /// <summary>MatterCommissioner.</summary>
    public MatterCommissioner(ILogger logger)
    {
        _log = logger;
    }

    /// <summary>MatterCommissioner.</summary>
    public MatterCommissioner(ILogger<MatterCommissioner> logger)
    {
        _log = logger;
    }

    // ─────────────────────────────────────────────────────────
    //  Full commissioning flow
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the complete Matter commissioning sequence over an already-connected transport.
    /// </summary>
    /// <param name="connection">An open transport connection (BLE/BTP, UDP, etc.)</param>
    /// <param name="fabric">The fabric to commission into</param>
    /// <param name="passcode">Device setup passcode</param>
    /// <param name="threadDataset">Thread operational dataset bytes (from OTBR), or null to skip Thread provisioning</param>
    /// <param name="wifiSsid">Wi-Fi SSID for network provisioning, or null to skip Wi-Fi</param>
    /// <param name="wifiPassword">Wi-Fi password, or null to skip Wi-Fi</param>
    /// <param name="regulatoryLocation">Regulatory location sent through General Commissioning.</param>
    /// <param name="regulatoryCountryCode">Two-letter uppercase regulatory country code sent through General Commissioning.</param>
    /// <param name="attestationVerifier">Optional device attestation verifier</param>
    /// <param name="onProgress">Optional progress callback</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<CommissioningResult> CommissionAsync(
        IConnection connection,
        Fabric fabric,
        uint passcode,
        byte[]? threadDataset,
        string? wifiSsid = null,
        string? wifiPassword = null,
        GeneralCommissioningCluster.RegulatoryLocationTypeEnum regulatoryLocation = GeneralCommissioningCluster.RegulatoryLocationTypeEnum.IndoorOutdoor,
        string regulatoryCountryCode = "XX",
        IDeviceAttestationVerifier? attestationVerifier = null,
        Action<CommissioningProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        var progress = onProgress ?? (_ => { });
        ValidateRegulatoryConfiguration(regulatoryCountryCode);

        try
        {
            var node = Fabric.CreateNode();
            _log.LogInformation("Fabric ready: NodeId={NodeId}", node.NodeId);

            // ── PASE ──
            Report(progress, "PASE", 10, "Starting PASE key exchange...");
            var paseSession = await EstablishPaseAsync(connection, passcode, progress, ct);

            // ── ArmFailSafe ──
            Report(progress, "ArmFailSafe", 30, "Arming fail-safe timer (120s)...");
            var gc = new GeneralCommissioningCluster(paseSession, endpointId: 0);
            var armFailSafeResponse = await gc.ArmFailSafeAsync(expiryLengthSeconds: 120, breadcrumb: 0, ct: ct);
            EnsureCommissioningCommandSucceeded(armFailSafeResponse, "ArmFailSafe");
            _log.LogInformation("ArmFailSafe OK");

            // ── SetRegulatoryConfig ──
            Report(progress, "RegulatoryConfig", 35, "Setting regulatory config...");
            var regulatoryResponse = await gc.SetRegulatoryConfigAsync(
                newRegulatoryConfig: regulatoryLocation,
                countryCode: regulatoryCountryCode,
                breadcrumb: 0,
                ct: ct);
            EnsureCommissioningCommandSucceeded(regulatoryResponse, "SetRegulatoryConfig");
            _log.LogInformation("SetRegulatoryConfig OK");

            // ── CSR ──
            Report(progress, "CSR", 40, "Requesting CSR from device...");
            var csrNonce = RandomNumberGenerator.GetBytes(32);
            var opCreds = new OperationalCredentialsCluster(paseSession, endpointId: 0);
            var csrResp = await opCreds.CSRRequestAsync(csrNonce, ct: ct);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("CSR response: Success={Success}, StatusCode={Code}, HasFields={Has}",
                csrResp.Success, csrResp.StatusCode, csrResp.ResponseFields != null);
            }

            if (csrResp.ResponseFields != null && _log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("CSR ResponseFields bytes: {Hex}",
                    Convert.ToHexString(csrResp.ResponseFields.GetBytes()));
            }

            if (!csrResp.Success || csrResp.ResponseFields is null)
            {
                throw new InvalidOperationException($"CSRRequest failed (status={csrResp.StatusCode})");
            }

            var peerPublicKey = ParseCsrResponse(csrResp.ResponseFields);
            var peerPublicKeyBytes = peerPublicKey.Q.GetEncoded(false);
            var peerKeyId = SHA1.HashData(peerPublicKeyBytes).AsSpan()[..20].ToArray();
            _log.LogInformation("CSR received, peer public key extracted");

            // ── Device Attestation (optional) ──
            if (attestationVerifier != null)
            {
                Report(progress, "Attestation", 42, "Verifying device attestation...");

                // Request DAC
                var dacResp = await opCreds.CertificateChainRequestAsync(OperationalCredentialsCluster.CertificateChainTypeEnum.DACCertificate, ct);
                if (!dacResp.Success || dacResp.ResponseFields is null)
                {
                    throw new MatterCommissioningException("Attestation", "CertificateChainRequest(DAC) failed");
                }

                var dacFields = CreateResponseFieldsReader(dacResp.ResponseFields);
                var dacCert = dacFields.GetOctetString(0);

                // Request PAI
                var paiResp = await opCreds.CertificateChainRequestAsync(OperationalCredentialsCluster.CertificateChainTypeEnum.PAICertificate, ct);
                if (!paiResp.Success || paiResp.ResponseFields is null)
                {
                    throw new MatterCommissioningException("Attestation", "CertificateChainRequest(PAI) failed");
                }

                var paiFields = CreateResponseFieldsReader(paiResp.ResponseFields);
                var paiCert = paiFields.GetOctetString(0);

                // Request attestation
                var attestationNonce = RandomNumberGenerator.GetBytes(32);
                var attestResp = await opCreds.AttestationRequestAsync(attestationNonce, ct);
                if (!attestResp.Success || attestResp.ResponseFields is null)
                {
                    throw new MatterCommissioningException("Attestation", "AttestationRequest failed");
                }

                var attestationFields = CreateResponseFieldsReader(attestResp.ResponseFields);
                var attestElements = attestationFields.GetOctetString(0);
                var attestSignature = attestationFields.GetOctetString(1);

                // Verify DAC→PAI chain and attestation signature
                attestationVerifier.Verify(dacCert, paiCert, attestElements, attestSignature, paseSession.AttestationChallenge);
                _log.LogInformation("Device attestation verified");
            }

            // ── Generate NOC ──
            Report(progress, "NOC", 45, "Generating node operational certificate...");
            var generatedNoc = GenerateNoc(node, fabric, peerPublicKey, peerKeyId);
            _log.LogInformation("Peer NOC generated");

            // ── AddTrustedRootCertificate ──
            Report(progress, "RootCert", 50, "Installing root certificate...");
            var encodedRootCert = EncodeMatterRootCert(fabric);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Root cert TLV ({Len} bytes): {Hex}", encodedRootCert.Length, Convert.ToHexString(encodedRootCert));
            }

            var rootResp = await opCreds.AddTrustedRootCertificateAsync(encodedRootCert, ct);
            if (!rootResp.Success)
            {
                return Fail(FormatInvokeFailure("AddTrustedRootCertificate", rootResp));
            }

            _log.LogInformation("AddTrustedRootCertificate OK");

            // ── AddNOC ──
            Report(progress, "AddNOC", 58, "Installing node operational certificate...");
            var encodedNoc = EncodeMatterNoc(node, fabric, generatedNoc.Noc, generatedNoc.Serial, peerPublicKeyBytes, peerKeyId);
            var addNocResp = await opCreds.AddNOCAsync(nOCValue: encodedNoc, iPKValue: fabric.IPK, caseAdminSubject: BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned()), adminVendorId: fabric.AdminVendorId, ct: ct);
            try
            {
                EnsureNocCommandSucceeded(addNocResp, "AddNOC");
            }
            catch (MatterCommissioningException ex)
            {
                return Fail(ex.Message);
            }

            _log.LogInformation("AddNOC OK");

            // ── Write ACL entry granting controller Administer privilege ──
            Report(progress, "ACL", 60, "Setting up access control...");
            var controllerNodeId = BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned());
            var adminAclEntry = new AccessControlCluster.AccessControlEntryStruct
            {
                Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer,
                AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
                Subjects = [controllerNodeId],
                Targets = null,
            };

            var aclResult = await WriteAndVerifyCommissioningAclAsync(
                new AccessControlCluster(paseSession, endpointId: 0),
                [adminAclEntry],
                controllerNodeId,
                "PASE",
                ct);
            if (!aclResult.Success)
            {
                _log.LogWarning("ACL write over PASE was not verified ({Reason}); retrying over CASE", aclResult.Error);
                try
                {
                    var caseOverBle = await new CASEClient(node, fabric, new UnsecureSession(connection)).EstablishSessionAsync(ct);
                    aclResult = await WriteAndVerifyCommissioningAclAsync(
                        new AccessControlCluster(caseOverBle, endpointId: 0),
                        [adminAclEntry],
                        controllerNodeId,
                        "CASE",
                        ct);
                }
                catch (Exception ex)
                {
                    aclResult = new CommissioningAclVerification(false, $"CASE ACL verification failed: {ex.Message}");
                }
            }

            if (!aclResult.Success)
            {
                return Fail($"Commissioning ACL write was not verified: {aclResult.Error}");
            }

            _log.LogInformation("ACL write verified — controller granted Administer privilege");

            // ── Network provisioning ──
            IPAddress? operationalAddress = null;
            ushort operationalPort = 5540;

            if (!string.IsNullOrEmpty(wifiSsid))
            {
                // WiFi provisioning
                Report(progress, "WiFi", 65, "Provisioning WiFi network...");
                var ssidBytes = Encoding.UTF8.GetBytes(wifiSsid);
                var passwordBytes = Encoding.UTF8.GetBytes(wifiPassword ?? "");

                var addWifiResponse = await MatterAdministration.AddOrUpdateWiFiNetworkAsync(
                    paseSession,
                    ssidBytes,
                    passwordBytes,
                    breadcrumb: 0,
                    endpointId: 0,
                    ct: ct);
                EnsureNetworkConfigurationSucceeded(addWifiResponse, "AddOrUpdateWiFiNetwork");
                _log.LogInformation("AddOrUpdateWiFiNetwork OK (SSID={Ssid})", wifiSsid);

                Report(progress, "ConnectNetwork", 75, "Connecting device to WiFi...");
                var connectWifiResponse = await MatterAdministration.ConnectNetworkAsync(
                    paseSession,
                    ssidBytes,
                    breadcrumb: 0,
                    endpointId: 0,
                    ct: ct);
                EnsureConnectNetworkSucceeded(connectWifiResponse, "ConnectNetwork(WiFi)");
                _log.LogInformation("ConnectNetwork (WiFi) OK");
            }
            else if (threadDataset is { Length: > 0 })
            {
                // Thread provisioning
                Report(progress, "Thread", 65, "Provisioning Thread network...");
                var extPanId = ExtractExtendedPanId(threadDataset)
                    ?? throw new InvalidOperationException("Could not extract Extended PAN ID from Thread dataset");
                _log.LogInformation("Thread dataset: {Len} bytes, ExtPanId={ExtPan}",
                    threadDataset.Length, BitConverter.ToString(extPanId));

                var addThreadResponse = await MatterAdministration.AddOrUpdateThreadNetworkAsync(
                    paseSession,
                    threadDataset,
                    breadcrumb: 0,
                    endpointId: 0,
                    ct: ct);
                EnsureNetworkConfigurationSucceeded(addThreadResponse, "AddOrUpdateThreadNetwork");
                _log.LogInformation("AddOrUpdateThreadNetwork OK");

                Report(progress, "ConnectNetwork", 75, "Connecting device to Thread network...");
                var connectThreadResponse = await MatterAdministration.ConnectNetworkAsync(
                    paseSession,
                    extPanId,
                    breadcrumb: 0,
                    endpointId: 0,
                    ct: ct);
                EnsureConnectNetworkSucceeded(connectThreadResponse, "ConnectNetwork(Thread)");
                _log.LogInformation("ConnectNetwork (Thread) OK");
            }

            // ── Operational discovery via mDNS ──
            Report(progress, "Discover", 78, "Discovering device on operational network...");

            var compressedFabricIdUlong = Convert.ToUInt64(fabric.CompressedFabricId, 16);
            var nodeIdBytes = node.NodeId.ToByteArrayUnsigned();
            var nodeIdPadded = new byte[8];
            if (nodeIdBytes.Length <= 8)
            {
                Array.Copy(nodeIdBytes, 0, nodeIdPadded, 8 - nodeIdBytes.Length, nodeIdBytes.Length);
            }

            var deviceNodeId = BitConverter.ToUInt64(nodeIdPadded);

            _log.LogInformation("Looking for operational node: CompressedFabricId={CFId:X16}, NodeId={NId:X16}",
                compressedFabricIdUlong, deviceNodeId);

            using (var discovery = new OperationalDiscovery())
            {
                var opNode = await discovery.ResolveNodeAsync(
                    compressedFabricIdUlong, deviceNodeId, TimeSpan.FromSeconds(15));

                if (opNode != null)
                {
                    operationalAddress = opNode.Address;
                    operationalPort = (ushort)opNode.Port;
                    _log.LogInformation("Operational node discovered: {Addr}:{Port}",
                        operationalAddress, operationalPort);
                }
                else
                {
                    _log.LogWarning("Operational mDNS discovery timed out before commissioning CASE.");
                }
            }

            // ── CASE ──
            // Prefer operational network (UDP) for CASE, fall back to BLE if unavailable
            Report(progress, "CASE", 80, "Establishing CASE secure session...");
            ISession caseSession;
            try
            {
                caseSession = await EstablishCommissioningCaseSessionAsync(
                    node,
                    fabric,
                    connection,
                    operationalAddress,
                    operationalPort,
                    ct);
            }
            catch (Exception ex)
            {
                return Fail($"CASE session establishment failed: {ex.Message}");
            }
            _log.LogInformation("CASE session established");

            // ── CommissioningComplete ──
            Report(progress, "Complete", 90, "Sending CommissioningComplete...");
            var caseGc = new GeneralCommissioningCluster(caseSession, endpointId: 0);
            try
            {
                using var completeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                completeCts.CancelAfter(TimeSpan.FromSeconds(15));
                var completeResponse = await caseGc.CommissioningCompleteAsync(completeCts.Token);
                EnsureCommissioningCommandSucceeded(completeResponse, "CommissioningComplete");
            }
            catch (Exception ex)
            {
                return Fail($"CommissioningComplete failed: {ex.Message}");
            }
            _log.LogInformation("CommissioningComplete OK");

            // ── Finalize ──
            Report(progress, "Done", 100, "Commissioning complete!");
            await fabric.AddCommissionedNodeAsync(node.NodeId,
                operationalAddress ?? IPAddress.None, operationalPort);

            return new CommissioningResult(true, node.NodeId.ToString(),
                operationalAddress?.ToString(), null, operationalPort);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Commissioning cancelled");
            return Fail("Commissioning was cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Commissioning failed");
            return Fail($"Commissioning failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  PASE (SPAKE2+) key exchange
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Performs the full PASE (SPAKE2+) key exchange and returns a secure session.
    /// </summary>
    public async Task<PaseSecureSession> EstablishPaseAsync(
        IConnection connection,
        uint passcode,
        Action<CommissioningProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        var progress = onProgress ?? (_ => { });
        var unsecureSession = new UnsecureSession(connection);
        var exchange = unsecureSession.CreateExchange();

        try
        {
            var mySessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
            var initiatorRandom = RandomNumberGenerator.GetBytes(32);

            // PBKDFParamRequest
            var pbkdfReq = new MatterTLV();
            pbkdfReq.AddStructure();
            pbkdfReq.AddOctetString(1, initiatorRandom);
            pbkdfReq.AddUInt16(2, mySessionId);
            pbkdfReq.AddUInt16(3, 0);
            pbkdfReq.AddBool(4, false);
            pbkdfReq.EndContainer();

            var reqPayload = new MessagePayload(pbkdfReq);
            reqPayload.ExchangeFlags |= ExchangeFlags.Initiator;
            reqPayload.ProtocolId = 0x00;
            reqPayload.ProtocolOpCode = 0x20;
            var reqFrame = new MessageFrame(reqPayload);
            reqFrame.MessageFlags |= MessageFlags.S;
            reqFrame.SessionID = 0x00;
            reqFrame.SecurityFlags = 0x00;

            _log.LogInformation("[PASE] Sending PBKDFParamRequest...");
            await exchange.SendAsync(reqFrame);
            var respFrame = await exchange.WaitForNextMessageAsync(ct);

            if (MessageFrame.IsStatusReport(respFrame))
            {
                throw new MatterCommissioningException("PASE", "Device returned StatusReport — may already be commissioned");
            }

            // Parse PBKDFParamResponse
            var pbkdfResp = respFrame.MessagePayload.ApplicationPayload
                ?? throw new InvalidOperationException("PBKDFParamResponse missing payload");
            pbkdfResp.OpenStructure();
            pbkdfResp.GetOctetString(1); // initiator random echo
            pbkdfResp.GetOctetString(2); // responder random
            var peerSessionId = pbkdfResp.GetUnsignedInt16(3);
            pbkdfResp.OpenStructure(4);
            var iterations = pbkdfResp.GetUnsignedInt16(1);
            var salt = pbkdfResp.GetOctetString(2);
            pbkdfResp.CloseContainer();

            _log.LogInformation("[PASE] PBKDFParamResponse: Iterations={Iter}, PeerSession={Peer}",
                iterations, peerSessionId);
            Report(progress, "PASE", 15, $"PBKDF params received (iterations={iterations})");

            // Compute SPAKE2+ context hash
            var contextWriter = new MatterMessageWriter();
            contextWriter.Write(Encoding.ASCII.GetBytes("CHIP PAKE V1 Commissioning"));
            contextWriter.Write(pbkdfReq.AsSpan());
            contextWriter.Write(pbkdfResp.AsSpan());
            var ctxHash = SHA256.HashData(contextWriter.WrittenSpan);

            // PAKE1
            var pakeValues = CryptographyMethods.Crypto_PAKEValues_Initiator(passcode, iterations, salt);

            var pake1 = new MatterTLV();
            pake1.AddStructure();
            pake1.AddOctetString(1, [.. pakeValues.X.GetEncoded(false)]);
            pake1.EndContainer();

            var p1Pay = new MessagePayload(pake1);
            p1Pay.ExchangeFlags |= ExchangeFlags.Initiator;
            p1Pay.ProtocolId = 0x00;
            p1Pay.ProtocolOpCode = 0x22;
            var p1Frame = new MessageFrame(p1Pay);
            p1Frame.MessageFlags |= MessageFlags.S;
            p1Frame.SessionID = 0x00;
            p1Frame.SecurityFlags = 0x00;

            _log.LogInformation("[PASE] Sending PAKE1...");
            await exchange.SendAsync(p1Frame);
            var p2Frame = await exchange.WaitForNextMessageAsync(ct);

            // Parse PAKE2
            var pake2 = p2Frame.MessagePayload.ApplicationPayload
                ?? throw new InvalidOperationException("PAKE2 response missing payload");
            pake2.OpenStructure();
            var Y = pake2.GetOctetString(1);
            var verifier = pake2.GetOctetString(2);
            pake2.CloseContainer();

            Report(progress, "PASE", 20, "Verifying SPAKE2+ proofs...");

            // PAKE3
            var pake2Result = CryptographyMethods.Crypto_P2(
                ctxHash,
                pakeValues.W0,
                pakeValues.W1,
                pakeValues.XScalar,
                pakeValues.X,
                Y);
            if (!pake2Result.HBX.SequenceEqual(verifier))
            {
                throw new CryptographicException("SPAKE2+ verifier mismatch — wrong passcode?");
            }
            _log.LogInformation("[PASE] Verifier OK");

            var pake3 = new MatterTLV();
            pake3.AddStructure();
            pake3.AddOctetString(1, pake2Result.HAY);
            pake3.EndContainer();

            var p3Pay = new MessagePayload(pake3);
            p3Pay.ExchangeFlags |= ExchangeFlags.Initiator;
            p3Pay.ProtocolId = 0x00;
            p3Pay.ProtocolOpCode = 0x24;
            var p3Frame = new MessageFrame(p3Pay);
            p3Frame.MessageFlags |= MessageFlags.S;
            p3Frame.SessionID = 0x00;
            p3Frame.SecurityFlags = 0x00;

            _log.LogInformation("[PASE] Sending PAKE3...");
            await exchange.SendAsync(p3Frame);
            var statusFrame = await exchange.WaitForNextMessageAsync(ct);
            await exchange.AcknowledgeMessageAsync(statusFrame.MessageCounter);

            // Derive session keys via HKDF
            byte[] info = Encoding.ASCII.GetBytes("SessionKeys");
            var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, pake2Result.Ke, 48, [], info);
            var encryptKey = keys.AsSpan()[..16].ToArray();
            var decryptKey = keys.AsSpan().Slice(16, 16).ToArray();
            var attestationChallenge = keys.AsSpan().Slice(32, 16).ToArray();

            _log.LogInformation("[PASE] Session keys derived");
            Report(progress, "PASE", 25, "PASE session established");

            exchange.Close();
            exchange = null!;

            return new PaseSecureSession(connection, mySessionId, peerSessionId, encryptKey, decryptKey, attestationChallenge);
        }
        finally
        {
            try
            {
                exchange?.Close();
            }
            catch
            {
            }
        }
    }

    private async Task<ISession> EstablishCommissioningCaseSessionAsync(
        Node node,
        Fabric fabric,
        IConnection commissioningConnection,
        IPAddress? operationalAddress,
        ushort operationalPort,
        CancellationToken ct)
    {
        if (operationalAddress == null)
        {
            _log.LogInformation("Establishing CASE over BLE (fallback)");
            return await EstablishCaseOverConnectionAsync(node, fabric, commissioningConnection, ct);
        }

        Exception? lastOperationalFailure = null;
        for (var attempt = 1; attempt <= OperationalCaseMaxAttempts; attempt++)
        {
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(_operationalCaseAttemptTimeout);

                _log.LogInformation(
                    "Establishing CASE over UDP to {Addr}:{Port} (attempt {Attempt}/{MaxAttempts})",
                    operationalAddress,
                    operationalPort,
                    attempt,
                    OperationalCaseMaxAttempts);

                return await EstablishCaseOverUdpAsync(
                    node,
                    fabric,
                    operationalAddress,
                    operationalPort,
                    attemptCts.Token);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastOperationalFailure = ex;
                _log.LogWarning(
                    "Operational CASE attempt {Attempt}/{MaxAttempts} timed out waiting for a response.",
                    attempt,
                    OperationalCaseMaxAttempts);
            }

            if (attempt < OperationalCaseMaxAttempts)
            {
                var delay = ComputeOperationalCaseRetryDelay(attempt);
                _log.LogInformation("Retrying operational CASE in {DelayMs} ms.", (int)delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }

        _log.LogWarning(
            lastOperationalFailure,
            "Operational CASE over UDP failed after {MaxAttempts} attempt(s); falling back to BLE.",
            OperationalCaseMaxAttempts);
        _log.LogInformation("Establishing CASE over BLE (fallback after UDP timeout)");
        return await EstablishCaseOverConnectionAsync(node, fabric, commissioningConnection, ct);
    }

    private static async Task<ISession> EstablishCaseOverConnectionAsync(
        Node node,
        Fabric fabric,
        IConnection connection,
        CancellationToken ct)
        => await new CASEClient(node, fabric, new UnsecureSession(connection)).EstablishSessionAsync(ct);

    private static async Task<ISession> EstablishCaseOverUdpAsync(
        Node node,
        Fabric fabric,
        IPAddress operationalAddress,
        ushort operationalPort,
        CancellationToken ct)
    {
        var udpConnection = new UdpConnection(operationalAddress, operationalPort);
        try
        {
            return await EstablishCaseOverConnectionAsync(node, fabric, udpConnection, ct);
        }
        catch
        {
            udpConnection.Dispose();
            throw;
        }
    }

    private static TimeSpan ComputeOperationalCaseRetryDelay(int completedAttempt)
        => completedAttempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(4),
        };

    // ─────────────────────────────────────────────────────────
    //  CSR response parsing
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a CSRResponse's ResponseFields TLV and extracts the device's EC public key.
    /// CSRResponse: struct { tag0: NOCSRElements (octet_string), tag1: AttestationSignature (octet_string) }
    /// NOCSRElements TLV: struct { tag1: csr (octet_string DER PKCS#10), tag2: CSRNonce (octet_string) }
    /// </summary>
    public static ECPublicKeyParameters ParseCsrResponse(MatterTLV responseFields)
    {
        var reader = CreateResponseFieldsReader(responseFields);
        var nocsrBytes = reader.GetOctetString(0);

        var nocPayload = new MatterTLV(nocsrBytes);
        nocPayload.OpenStructure();
        var derBytes = nocPayload.GetOctetString(1);

        var csr = new Pkcs10CertificationRequest(derBytes);
        return (ECPublicKeyParameters)csr.GetPublicKey();
    }

    // ─────────────────────────────────────────────────────────
    //  NOC generation
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Node Operational Certificate (NOC) for the device.
    /// </summary>
    public static GeneratedNoc GenerateNoc(
        Node node, Fabric fabric, ECPublicKeyParameters peerPublicKey, byte[] peerKeyId)
    {
        var certGen = new X509V3CertificateGenerator();
        var rng = new SecureRandom(new CryptoApiRandomGenerator());
        var serial = BigIntegers.CreateRandomInRange(
            BigInteger.One, BigInteger.ValueOf(long.MaxValue), rng);

        certGen.SetSerialNumber(serial);

        var subjOids = new List<DerObjectIdentifier>
        {
            new("1.3.6.1.4.1.37244.1.1"), // NodeId
            new("1.3.6.1.4.1.37244.1.5"), // FabricId
        };
        var nodeIdHex = Convert.ToHexString(node.NodeId.ToByteArrayUnsigned());
        var subjVals = new List<string> { nodeIdHex, "FAB000000000001D" };
        certGen.SetSubjectDN(new X509Name(subjOids, subjVals));

        var issuerOids = new List<DerObjectIdentifier> { new("1.3.6.1.4.1.37244.1.4") };
        certGen.SetIssuerDN(new X509Name(issuerOids, ["CACACACA00000001"]));

        certGen.SetNotBefore(DateTime.UtcNow.AddYears(-10));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(10));
        certGen.SetPublicKey(peerPublicKey);

        certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
        certGen.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
        certGen.AddExtension(X509Extensions.ExtendedKeyUsage, true,
            new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
        certGen.AddExtension(X509Extensions.SubjectKeyIdentifier, false,
            new SubjectKeyIdentifier(peerKeyId));
        certGen.AddExtension(X509Extensions.AuthorityKeyIdentifier, false,
            new AuthorityKeyIdentifier(fabric.RootKeyIdentifier));

        ISignatureFactory sigFactory = new Asn1SignatureFactory(
            "SHA256WITHECDSA", P256KeyInterop.ToBouncyCastlePrivateKey(fabric.RootCAKeyPair));
        var noc = certGen.Generate(sigFactory);
        noc.CheckValidity();

        return new GeneratedNoc(noc, serial);
    }

    // ─────────────────────────────────────────────────────────
    //  Matter TLV certificate encoding
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes the fabric root certificate in Matter TLV format.
    /// </summary>
    public static byte[] EncodeMatterRootCert(Fabric fabric)
    {
        var bcRootCertPubKey = (ECPublicKeyParameters)fabric.RootCACertificate.GetPublicKey();
        var rootPublicKeyBytes = P256KeyInterop.ExportPublicKey(bcRootCertPubKey);
        var nbRoot = new DateTimeOffset(fabric.RootCACertificate.NotBefore).ToEpochTime();
        var naRoot = new DateTimeOffset(fabric.RootCACertificate.NotAfter).ToEpochTime();

        var enc = new MatterTLV();
        enc.AddStructure();
        enc.AddOctetString(1, fabric.RootCACertificate.SerialNumber.ToByteArrayUnsigned());
        enc.AddUInt8(2, 1);
        enc.AddList(3); // Issuer
        enc.AddUInt64(20, ToMatterUInt64Bytes(fabric.RootCACertificateId, nameof(fabric.RootCACertificateId)));
        enc.EndContainer();
        enc.AddUInt32(4, (uint)nbRoot);
        enc.AddUInt32(5, (uint)naRoot);
        enc.AddList(6); // Subject
        enc.AddUInt64(20, ToMatterUInt64Bytes(fabric.RootCACertificateId, nameof(fabric.RootCACertificateId)));
        enc.EndContainer();
        enc.AddUInt8(7, 1);
        enc.AddUInt8(8, 1);
        enc.AddOctetString(9, rootPublicKeyBytes);
        enc.AddList(10); // Extensions
        enc.AddStructure(1);
        enc.AddBool(1, true);
        enc.EndContainer();
        enc.AddUInt8(2, 0x60);
        enc.AddOctetString(4, fabric.RootKeyIdentifier);
        enc.AddOctetString(5, fabric.RootKeyIdentifier);
        enc.EndContainer();

        var rootSigRS = MatterCertSigner.SignRootCert(
            fabric.RootCACertificate.SerialNumber.ToByteArrayUnsigned(),
            fabric.RootCACertificateId,
            (uint)nbRoot, (uint)naRoot,
            rootPublicKeyBytes,
            fabric.RootKeyIdentifier,
            fabric.RootCAKeyPair);
        enc.AddOctetString(11, rootSigRS);
        enc.EndContainer();

        return enc.GetBytes();
    }

    /// <summary>
    /// Encodes a node operational certificate in Matter TLV format.
    /// </summary>
    public static byte[] EncodeMatterNoc(
        Node node, Fabric fabric, X509Certificate noc,
        BigInteger serial, byte[] peerPublicKeyBytes, byte[] peerKeyId)
    {
        var nbNoc = new DateTimeOffset(noc.NotBefore).ToEpochTime();
        var naNoc = new DateTimeOffset(noc.NotAfter).ToEpochTime();

        var enc = new MatterTLV();
        enc.AddStructure();
        enc.AddOctetString(1, noc.SerialNumber.ToByteArrayUnsigned());
        enc.AddUInt8(2, 1);
        enc.AddList(3); // Issuer
        enc.AddUInt64(20, ToMatterUInt64Bytes(fabric.RootCACertificateId, nameof(fabric.RootCACertificateId)));
        enc.EndContainer();
        enc.AddUInt32(4, (uint)nbNoc);
        enc.AddUInt32(5, (uint)naNoc);
        enc.AddList(6); // Subject
        enc.AddUInt64(17, ToMatterUInt64Bytes(node.NodeId, nameof(node.NodeId)));
        enc.AddUInt64(21, ToMatterUInt64Bytes(fabric.FabricId, nameof(fabric.FabricId)));
        enc.EndContainer();
        enc.AddUInt8(7, 1);
        enc.AddUInt8(8, 1);
        enc.AddOctetString(9, peerPublicKeyBytes);
        enc.AddList(10); // Extensions
        enc.AddStructure(1);
        enc.AddBool(1, false);
        enc.EndContainer();
        enc.AddUInt8(2, 0x1);
        enc.AddArray(3);
        enc.AddUInt8(0x02);
        enc.AddUInt8(0x01);
        enc.EndContainer();
        enc.AddOctetString(4, peerKeyId);
        enc.AddOctetString(5, fabric.RootKeyIdentifier);
        enc.EndContainer();

        var nocSigRS = MatterCertSigner.SignNoc(
            serial.ToByteArrayUnsigned(),
            fabric.RootCACertificateId,
            node.NodeId,
            fabric.FabricId,
            (uint)nbNoc, (uint)naNoc,
            peerPublicKeyBytes,
            peerKeyId,
            fabric.RootKeyIdentifier,
            fabric.RootCAKeyPair);
        enc.AddOctetString(11, nocSigRS);
        enc.EndContainer();

        return enc.GetBytes();
    }

    private static byte[] ToMatterUInt64Bytes(BigInteger value, string valueName)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length > sizeof(ulong))
        {
            throw new ArgumentOutOfRangeException(valueName, "Matter UInt64 values must fit in 8 bytes.");
        }

        Span<byte> padded = stackalloc byte[sizeof(ulong)];
        bytes.CopyTo(padded[(sizeof(ulong) - bytes.Length)..]);
        return padded.ToArray();
    }

    // ─────────────────────────────────────────────────────────
    //  Thread helpers
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the Extended PAN ID (type 0x02, 8 bytes) from a Thread operational dataset.
    /// </summary>
    public static byte[]? ExtractExtendedPanId(byte[] dataset)
    {
        for (var i = 0; i + 2 <= dataset.Length;)
        {
            var ttype = dataset[i];
            var tlen = dataset[i + 1];
            var valueStart = i + 2;
            var next = valueStart + tlen;
            if (next > dataset.Length)
            {
                return null;
            }

            if (ttype == 0x02 && tlen == 8)
            {
                return dataset[valueStart..next];
            }

            i = next;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private static void ValidateRegulatoryConfiguration(string regulatoryCountryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regulatoryCountryCode);
        if (regulatoryCountryCode.Length != 2 || regulatoryCountryCode.Any(static c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Regulatory country code must be exactly two uppercase ASCII letters.", nameof(regulatoryCountryCode));
        }
    }

    private static MatterTLV CreateResponseFieldsReader(MatterTLV responseFields)
    {
        var reader = new MatterTLV(responseFields.GetBytes());
        reader.OpenStructure(1);
        return reader;
    }

    private static void Report(Action<CommissioningProgress> progress, string step, int percent, string message)
    {
        progress(new CommissioningProgress(step, percent, message));
    }

    private static CommissioningResult Fail(string error) =>
        new(false, null, null, error);

    private static void EnsureCommissioningCommandSucceeded(InvokeResponse response, string commandName)
    {
        if (!response.Success)
        {
            throw new MatterCommissioningException(commandName, FormatInvokeFailure(commandName, response));
        }

        if (response.ResponseFields is null)
        {
            throw new MatterCommissioningException(commandName, $"{commandName} succeeded without response fields.");
        }

        var status = ParseCommissioningCommandResponse(response.ResponseFields);
        if (status.ErrorCode != (byte)GeneralCommissioningCluster.CommissioningErrorEnum.OK)
        {
            throw new MatterCommissioningException(
                commandName,
                $"{commandName} rejected: error=0x{status.ErrorCode:X2}{FormatDebugText(status.DebugText)}");
        }
    }

    private static void EnsureNetworkConfigurationSucceeded(MatterNetworkConfigCommandResult response, string commandName)
    {
        if (!response.Accepted || response.NetworkingStatus is null)
        {
            throw new MatterCommissioningException(
                commandName,
                $"{commandName} rejected: networkingStatus={(response.NetworkingStatus is null ? "unknown" : $"0x{(byte)response.NetworkingStatus.Value:X2}")}{FormatDebugText(response.DebugText)}{FormatCommandError(response.Error)}");
        }
    }

    private static void EnsureConnectNetworkSucceeded(MatterConnectNetworkCommandResult response, string commandName)
    {
        if (!response.Accepted || response.NetworkingStatus is null)
        {
            throw new MatterCommissioningException(
                commandName,
                $"{commandName} rejected: networkingStatus={(response.NetworkingStatus is null ? "unknown" : $"0x{(byte)response.NetworkingStatus.Value:X2}")}{FormatDebugText(response.DebugText)}{FormatCommandError(response.Error)}");
        }
    }

    private static void EnsureNocCommandSucceeded(InvokeResponse response, string commandName)
    {
        if (!response.Success)
        {
            throw new MatterCommissioningException(commandName, FormatInvokeFailure(commandName, response));
        }

        if (response.ResponseFields is null)
        {
            throw new MatterCommissioningException(commandName, $"{commandName} succeeded without response fields.");
        }

        var status = ParseNocResponse(response.ResponseFields);
        if (status.StatusCode != (byte)OperationalCredentialsCluster.NodeOperationalCertStatusEnum.OK)
        {
            throw new MatterCommissioningException(
                commandName,
                $"{commandName} rejected: status=0x{status.StatusCode:X2}{FormatDebugText(status.DebugText)}");
        }
    }

    private sealed record CommissioningCommandStatus(byte ErrorCode, string? DebugText);

    private sealed record NocCommandStatus(byte StatusCode, string? DebugText);

    private sealed record CommissioningAclVerification(bool Success, string? Error);

    private static CommissioningCommandStatus ParseCommissioningCommandResponse(MatterTLV responseFields)
    {
        var reader = CreateResponseFieldsReader(responseFields);
        var errorCode = reader.GetUnsignedInt8(0);
        string? debugText = reader.IsNextTag(1) ? reader.GetUTF8String(1) : null;
        return new CommissioningCommandStatus(errorCode, debugText);
    }

    private static NocCommandStatus ParseNocResponse(MatterTLV responseFields)
    {
        var reader = CreateResponseFieldsReader(responseFields);
        var statusCode = reader.GetUnsignedInt8(0);
        if (reader.IsNextTag(1))
        {
            reader.SkipElement();
        }

        string? debugText = reader.IsNextTag(2) ? reader.GetUTF8String(2) : null;
        return new NocCommandStatus(statusCode, debugText);
    }

    private static string FormatInvokeFailure(string commandName, InvokeResponse response)
        => $"{commandName} failed: status=0x{response.StatusCode:X2}"
            + (string.IsNullOrWhiteSpace(response.Error) ? string.Empty : $" ({response.Error})");

    private static string FormatDebugText(string? debugText)
        => string.IsNullOrWhiteSpace(debugText) ? string.Empty : $", debugText=\"{debugText}\"";

    private static string FormatCommandError(string? error)
        => string.IsNullOrWhiteSpace(error) ? string.Empty : $", error=\"{error}\"";

    private async Task<CommissioningAclVerification> WriteAndVerifyCommissioningAclAsync(
        AccessControlCluster accessControl,
        AccessControlCluster.AccessControlEntryStruct[] acl,
        ulong controllerNodeId,
        string sessionType,
        CancellationToken ct)
    {
        var aclWrite = await accessControl.WriteACLAsync(acl, ct: ct);
        if (!aclWrite.Success)
        {
            var existingAcl = await accessControl.ReadACLAsync(ct);
            if (HasControllerAdminAclEntry(existingAcl, controllerNodeId))
            {
                _log.LogInformation(
                    "ACL write over {SessionType} was rejected ({Reason}), but controller Administer privilege is present",
                    sessionType,
                    FormatWriteResponse(aclWrite));
                return new CommissioningAclVerification(true, null);
            }

            return new CommissioningAclVerification(false, $"{sessionType} write rejected: {FormatWriteResponse(aclWrite)}");
        }

        var aclReadback = await accessControl.ReadACLAsync(ct);
        if (!HasControllerAdminAclEntry(aclReadback, controllerNodeId))
        {
            return new CommissioningAclVerification(false, $"{sessionType} readback did not include controller Administer entry");
        }

        return new CommissioningAclVerification(true, null);
    }

    private static bool HasControllerAdminAclEntry(
        AccessControlCluster.AccessControlEntryStruct[]? acl,
        ulong controllerNodeId)
        => acl is not null && acl.Any(entry =>
            entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer
            && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
            && entry.Subjects?.Contains(controllerNodeId) == true);

    private static string FormatWriteResponse(WriteResponse response)
    {
        if (response.StatusCode is { } status)
        {
            return $"status=0x{status:X2}";
        }

        if (response.AttributeStatuses.Count == 0)
        {
            return "no write status returned";
        }

        return string.Join(", ", response.AttributeStatuses.Select(
            status => $"attr=0x{status.AttributeId:X4} status=0x{status.StatusCode:X2}"));
    }
}
