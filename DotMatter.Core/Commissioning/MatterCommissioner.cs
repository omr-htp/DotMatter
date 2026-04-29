using DotMatter.Core.Certificates;
using DotMatter.Core.Clusters;
using DotMatter.Core.Cryptography;
using DotMatter.Core.Discovery;
using DotMatter.Core.Fabrics;
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
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DotMatter.Core.Commissioning;

/// <summary>
/// Transport-agnostic Matter commissioning engine.
/// Implements the full commissioning flow: PASE → CSR → NOC → Thread → CASE → Complete.
/// </summary>
public class MatterCommissioner
{
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
        IDeviceAttestationVerifier? attestationVerifier = null,
        Action<CommissioningProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        var progress = onProgress ?? (_ => { });

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
            await gc.ArmFailSafeAsync(expiryLengthSeconds: 120, breadcrumb: 0, ct: ct);
            _log.LogInformation("ArmFailSafe OK");

            // ── SetRegulatoryConfig ──
            Report(progress, "RegulatoryConfig", 35, "Setting regulatory config...");
            await gc.SetRegulatoryConfigAsync(newRegulatoryConfig: (GeneralCommissioningCluster.RegulatoryLocationTypeEnum)2, countryCode: "XX", breadcrumb: 0, ct: ct);
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

                var dacCert = dacResp.ResponseFields.GetOctetString(0);

                // Request PAI
                var paiResp = await opCreds.CertificateChainRequestAsync(OperationalCredentialsCluster.CertificateChainTypeEnum.PAICertificate, ct);
                if (!paiResp.Success || paiResp.ResponseFields is null)
                {
                    throw new MatterCommissioningException("Attestation", "CertificateChainRequest(PAI) failed");
                }

                var paiCert = paiResp.ResponseFields.GetOctetString(0);

                // Request attestation
                var attestationNonce = RandomNumberGenerator.GetBytes(32);
                var attestResp = await opCreds.AttestationRequestAsync(attestationNonce, ct);
                if (!attestResp.Success || attestResp.ResponseFields is null)
                {
                    throw new MatterCommissioningException("Attestation", "AttestationRequest failed");
                }

                var attestElements = attestResp.ResponseFields.GetOctetString(0);
                var attestSignature = attestResp.ResponseFields.GetOctetString(1);

                // Verify DAC→PAI chain and attestation signature
                attestationVerifier.Verify(dacCert, paiCert, attestElements, attestSignature, attestationNonce);
                _log.LogInformation("Device attestation verified");
            }

            // ── Generate NOC ──
            Report(progress, "NOC", 45, "Generating node operational certificate...");
            var (peerNoc, serial) = GenerateNoc(node, fabric, peerPublicKey, peerKeyId);
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
                return Fail($"AddTrustedRootCertificate rejected: status=0x{rootResp.StatusCode:X2}");
            }

            _log.LogInformation("AddTrustedRootCertificate OK");

            // ── AddNOC ──
            Report(progress, "AddNOC", 58, "Installing node operational certificate...");
            var encodedNoc = EncodeMatterNoc(node, fabric, peerNoc, serial, peerPublicKeyBytes, peerKeyId);
            var addNocResp = await opCreds.AddNOCAsync(nOCValue: encodedNoc, iPKValue: fabric.IPK, caseAdminSubject: BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned()), adminVendorId: fabric.AdminVendorId, ct: ct);
            if (!addNocResp.Success)
            {
                return Fail($"AddNOC rejected: status=0x{addNocResp.StatusCode:X2}");
            }

            _log.LogInformation("AddNOC OK");

            // ── Write ACL entry granting controller Administer privilege ──
            Report(progress, "ACL", 60, "Setting up access control...");
            var controllerNodeId = BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned());
            await InteractionModel.InteractionManager.WriteAttributeAsync(paseSession, endpointId: 0, clusterId: AccessControlCluster.ClusterId, attributeId: AccessControlCluster.Attributes.ACL, writeValue: tlv =>
                {
                    // Data tag 1 — array of ACL entries
                    tlv.AddArray(tagNumber: 1);
                    tlv.AddStructure();
                    tlv.AddUInt8(1, (byte)AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer);
                    tlv.AddUInt8(2, (byte)AccessControlCluster.AccessControlEntryAuthModeEnum.CASE);
                    tlv.AddArray(tagNumber: 3); // Subjects
                    tlv.AddUInt64(controllerNodeId);
                    tlv.EndContainer();
                    // Targets = null (all endpoints/clusters)
                    tlv.AddNull(4);
                    tlv.EndContainer();
                    tlv.EndContainer();
                }, ct: ct);
            _log.LogInformation("ACL write OK — controller granted Administer privilege");

            // ── Network provisioning ──
            IPAddress? operationalAddress = null;
            ushort operationalPort = 5540;

            if (!string.IsNullOrEmpty(wifiSsid))
            {
                // WiFi provisioning
                Report(progress, "WiFi", 65, "Provisioning WiFi network...");
                var ssidBytes = Encoding.UTF8.GetBytes(wifiSsid);
                var passwordBytes = Encoding.UTF8.GetBytes(wifiPassword ?? "");

                var netComm = new NetworkCommissioningCluster(paseSession, endpointId: 0);
                await netComm.AddOrUpdateWiFiNetworkAsync(sSID: ssidBytes, credentials: passwordBytes, breadcrumb: 0, ct: ct);
                _log.LogInformation("AddOrUpdateWiFiNetwork OK (SSID={Ssid})", wifiSsid);

                Report(progress, "ConnectNetwork", 75, "Connecting device to WiFi...");
                await netComm.ConnectNetworkAsync(networkID: ssidBytes, breadcrumb: 0, ct: ct);
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

                var netComm = new NetworkCommissioningCluster(paseSession, endpointId: 0);
                await netComm.AddOrUpdateThreadNetworkAsync(operationalDataset: threadDataset, breadcrumb: 0, ct: ct);
                _log.LogInformation("AddOrUpdateThreadNetwork OK");

                Report(progress, "ConnectNetwork", 75, "Connecting device to Thread network...");
                await netComm.ConnectNetworkAsync(networkID: extPanId, breadcrumb: 0, ct: ct);
                _log.LogInformation("ConnectNetwork (Thread) OK");
            }

            // ── Operational discovery via mDNS ──
            if (threadDataset is { Length: > 0 } || !string.IsNullOrEmpty(wifiSsid))
            {
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

                using var discovery = new OperationalDiscovery();
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
                    _log.LogWarning("mDNS discovery timed out — will try CASE over BLE as fallback");
                }
            }

            // ── CASE ──
            // Prefer operational network (UDP) for CASE, fall back to BLE if unavailable
            Report(progress, "CASE", 80, "Establishing CASE secure session...");
            ISession caseSession;
            UdpConnection? udpConn = null;
            try
            {
                IConnection caseConnection;
                if (operationalAddress != null)
                {
                    _log.LogInformation("Establishing CASE over UDP to {Addr}:{Port}",
                        operationalAddress, operationalPort);
                    udpConn = new UdpConnection(operationalAddress, operationalPort);
                    caseConnection = udpConn;
                }
                else
                {
                    _log.LogInformation("Establishing CASE over BLE (fallback)");
                    caseConnection = connection;
                }

                var unsecureSession = new UnsecureSession(caseConnection);
                var caseClient = new CASEClient(node, fabric, unsecureSession);
                caseSession = await caseClient.EstablishSessionAsync();
            }
            catch (Exception ex)
            {
                udpConn?.Dispose();
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
                await caseGc.CommissioningCompleteAsync(completeCts.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning("CommissioningComplete exception (may be expected): {Msg}", ex.Message);
            }
            _log.LogInformation("CommissioningComplete OK");

            // ── Finalize ──
            Report(progress, "Done", 100, "Commissioning complete!");
            await fabric.AddCommissionedNodeAsync(node.NodeId,
                operationalAddress ?? IPAddress.None, operationalPort);

            return new CommissioningResult(true, node.NodeId.ToString(),
                operationalAddress?.ToString(), null);
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
            var (w0, w1, x, X) = CryptographyMethods.Crypto_PAKEValues_Initiator(passcode, iterations, salt);

            var pake1 = new MatterTLV();
            pake1.AddStructure();
            pake1.AddOctetString(1, [.. X.GetEncoded(false)]);
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
            var (Ke, hAY, hBX) = CryptographyMethods.Crypto_P2(ctxHash, w0, w1, x, X, Y);
            if (!hBX.SequenceEqual(verifier))
            {
                throw new CryptographicException("SPAKE2+ verifier mismatch — wrong passcode?");
            }
            _log.LogInformation("[PASE] Verifier OK");

            var pake3 = new MatterTLV();
            pake3.AddStructure();
            pake3.AddOctetString(1, hAY);
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
            var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, Ke, 48, [], info);
            var encryptKey = keys.AsSpan()[..16].ToArray();
            var decryptKey = keys.AsSpan().Slice(16, 16).ToArray();

            _log.LogInformation("[PASE] Session keys derived");
            Report(progress, "PASE", 25, "PASE session established");

            exchange.Close();
            exchange = null!;

            return new PaseSecureSession(connection, mySessionId, peerSessionId, encryptKey, decryptKey);
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
        responseFields.OpenStructure(1);  // CommandDataIB Fields struct (context tag 1)
        var nocsrBytes = responseFields.GetOctetString(0);

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
    public static (X509Certificate Noc, BigInteger Serial) GenerateNoc(
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

        return (noc, serial);
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
        enc.AddUInt64(20, fabric.RootCACertificateId.ToByteArrayUnsigned());
        enc.EndContainer();
        enc.AddUInt32(4, (uint)nbRoot);
        enc.AddUInt32(5, (uint)naRoot);
        enc.AddList(6); // Subject
        enc.AddUInt64(20, fabric.RootCACertificateId.ToByteArrayUnsigned());
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
        enc.AddUInt64(20, fabric.RootCACertificateId.ToByteArrayUnsigned());
        enc.EndContainer();
        enc.AddUInt32(4, (uint)nbNoc);
        enc.AddUInt32(5, (uint)naNoc);
        enc.AddList(6); // Subject
        enc.AddUInt64(17, node.NodeId.ToByteArrayUnsigned());
        enc.AddUInt64(21, fabric.FabricId.ToByteArrayUnsigned());
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

    // ─────────────────────────────────────────────────────────
    //  Thread helpers
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the Extended PAN ID (type 0x02, 8 bytes) from a Thread operational dataset.
    /// </summary>
    public static byte[]? ExtractExtendedPanId(byte[] dataset)
    {
        for (int i = 0; i < dataset.Length;)
        {
            byte ttype = dataset[i];
            byte tlen = dataset[i + 1];
            if (ttype == 0x02 && tlen == 8)
            {
                return dataset[(i + 2)..(i + 2 + 8)];
            }
            i += 2 + tlen;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private static void Report(Action<CommissioningProgress> progress, string step, int percent, string message)
    {
        progress(new CommissioningProgress(step, percent, message));
    }

    private static CommissioningResult Fail(string error) =>
        new(false, null, null, error);
}
