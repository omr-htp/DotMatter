using DotMatter.Core.Certificates;
using DotMatter.Core.Cryptography;
using DotMatter.Core.Fabrics;
using DotMatter.Core.TLV;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DotMatter.Core.Sessions;

/// <summary>CASEClient class.</summary>
/// <summary>CASEClient class.</summary>
public class CASEClient(Node node, Fabric fabric, UnsecureSession unsecureSession)
{
    private readonly Node _node = node;
    private readonly Fabric _fabric = fabric;
    private readonly UnsecureSession _unsecureSession = unsecureSession;

    /// <summary>EstablishSessionAsync.</summary>
    public async Task<ISession> EstablishSessionAsync(CancellationToken ct = default)
    {
        MatterLog.Info("┌───────────────────────┐");
        MatterLog.Debug("| SENDING CASE - Sigma1 |");
        MatterLog.Info("└───────────────────────┘");

        var caseExchange = _unsecureSession.CreateExchange();

        // Exchange CASE Messages, starting with Sigma1
        //
        var spake1InitiatorRandomBytes = RandomNumberGenerator.GetBytes(32);
        var spake1SessionId = RandomNumberGenerator.GetBytes(16);

        //MatterLog.Debug("Spake1InitiatorRandomBytes: {0}", BitConverter.ToString(spake1InitiatorRandomBytes).Replace("-", ""));

        using var ephermeralKey = CertificateAuthority.GenerateECDiffieHellman();
        var ephermeralPublicKeysBytes = P256KeyInterop.ExportPublicKey(ephermeralKey);

        MatterLog.Debug("spake1InitiatorRandomBytes: {0}", MatterLog.FormatSecret(spake1InitiatorRandomBytes));
        MatterLog.Debug("RootPublicKeyBytes: {0}", MatterLog.FormatBytes(_fabric.RootPublicKeyBytes));
        MatterLog.Info("FabricId: {0}", BitConverter.ToUInt64(_fabric.FabricId.ToByteArrayUnsigned()));
        MatterLog.Info("NodeId: {0}", BitConverter.ToUInt64(_node.NodeId.ToByteArrayUnsigned()));

        // Destination identifier is a composite (113 bytes total).
        // FabricId and NodeId must be padded to 8 bytes (big-endian).
        // Although TLV stores our BE bytes as-is (wrong LE), the device reads
        // them as LE(swapped) and then writes LE(swapped) for DestinationId,
        // which equals our original BE bytes. So we use BE bytes here.
        //
        MemoryStream ms = new();
        BinaryWriter writer = new(ms);
        writer.Write(spake1InitiatorRandomBytes);
        writer.Write(_fabric.RootPublicKeyBytes);

        var fabricIdBytes = _fabric.FabricId.ToByteArrayUnsigned();
        var fabricIdPadded = new byte[8];
        if (fabricIdBytes.Length <= 8)
        {
            Array.Copy(fabricIdBytes, 0, fabricIdPadded, 8 - fabricIdBytes.Length, fabricIdBytes.Length);
        }
        else
        {
            Array.Copy(fabricIdBytes, fabricIdBytes.Length - 8, fabricIdPadded, 0, 8);
        }

        writer.Write(fabricIdPadded);

        var nodeIdBytes = _node.NodeId.ToByteArrayUnsigned();
        var nodeIdPadded = new byte[8];
        if (nodeIdBytes.Length <= 8)
        {
            Array.Copy(nodeIdBytes, 0, nodeIdPadded, 8 - nodeIdBytes.Length, nodeIdBytes.Length);
        }
        else
        {
            Array.Copy(nodeIdBytes, nodeIdBytes.Length - 8, nodeIdPadded, 0, 8);
        }

        writer.Write(nodeIdPadded);

        var destinationId = ms.ToArray();

        MatterLog.Debug("DestinationId msg ({0} bytes): {1}", destinationId.Length, MatterLog.FormatBytes(destinationId));
        MatterLog.Debug("OperationalIPK ({0} bytes): {1}", _fabric.OperationalIPK.Length, MatterLog.FormatSecret(_fabric.OperationalIPK));
        MatterLog.Debug("FabricId BE: {0}", MatterLog.FormatBytes(fabricIdPadded));
        MatterLog.Debug("NodeId BE: {0}", MatterLog.FormatBytes(nodeIdPadded));

        var hmac = new HMACSHA256(_fabric.OperationalIPK);
        byte[] hashedDestinationId = hmac.ComputeHash(destinationId);

        MatterLog.Debug("Hashed DestinationId: {0}", MatterLog.FormatBytes(hashedDestinationId));

        var sigma1Payload = new MatterTLV();
        sigma1Payload.AddStructure();

        sigma1Payload.AddOctetString(1, spake1InitiatorRandomBytes); // initiatorRandom
        sigma1Payload.AddUInt16(2, BitConverter.ToUInt16(spake1SessionId)); // initiatorSessionId 
        sigma1Payload.AddOctetString(3, hashedDestinationId); // destinationId
        sigma1Payload.AddOctetString(4, ephermeralPublicKeysBytes); // initiatorEphPubKey

        sigma1Payload.EndContainer();

        var sigma1MessagePayload = new MessagePayload(sigma1Payload);

        sigma1MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

        sigma1MessagePayload.ProtocolId = 0x00;
        sigma1MessagePayload.ProtocolOpCode = 0x30; // Sigma1

        var sigma1MessageFrame = new MessageFrame(sigma1MessagePayload);

        sigma1MessageFrame.MessageFlags |= MessageFlags.S;
        sigma1MessageFrame.SecurityFlags = 0x00;
        sigma1MessageFrame.SourceNodeID = 0x00;

        await caseExchange.SendAsync(sigma1MessageFrame);

        var sigma2MessageFrame = await caseExchange.WaitForNextMessageAsync(ct);

        // Check if we got a StatusReport instead of Sigma2
        if (sigma2MessageFrame.MessagePayload.ProtocolOpCode == 0x40)
        {
            var srPayload = sigma2MessageFrame.MessagePayload.ApplicationPayload;
            if (srPayload != null)
            {
                var srBytes = srPayload.AsSpan();
                if (srBytes.Length >= 8)
                {
                    var gc = BinaryPrimitives.ReadUInt16LittleEndian(srBytes);
                    var pid = BinaryPrimitives.ReadUInt32LittleEndian(srBytes[2..]);
                    var pc = BinaryPrimitives.ReadUInt16LittleEndian(srBytes[6..]);
                    throw new MatterSessionException(string.Format("CASE rejected: General={0}, Proto=0x{1:X}, Code={2}", gc, pid, pc));
                }
            }
            throw new MatterSessionException("CASE rejected with StatusReport");
        }


        MatterLog.Info("┌───────────────────────┐");
        MatterLog.Debug("| SENDING CASE - Sigma2 |");
        MatterLog.Info("└───────────────────────┘");

        var sigma2Payload = sigma2MessageFrame.MessagePayload.ApplicationPayload!;

        sigma2Payload.OpenStructure();

        var sigma2ResponderRandom = sigma2Payload.GetOctetString(1);
        var sigma2ResponderSessionId = sigma2Payload.GetUnsignedInt16(2);
        var sigma2ResponderEphPublicKey = sigma2Payload.GetOctetString(3);
        var sigma2EncryptedPayload = sigma2Payload.GetOctetString(4);

        // Parse optional MRP parameters (tag 5) from Sigma2
        TimeSpan? peerMrpIdle = null, peerMrpActive = null;
        if (sigma2Payload.IsNextTag(5))
        {
            sigma2Payload.OpenStructure(5);
            if (sigma2Payload.IsNextTag(1))
            {
                peerMrpIdle = TimeSpan.FromMilliseconds(sigma2Payload.GetUnsignedIntAny(1));
            }

            if (sigma2Payload.IsNextTag(2))
            {
                peerMrpActive = TimeSpan.FromMilliseconds(sigma2Payload.GetUnsignedIntAny(2));
            }

            sigma2Payload.CloseContainer();
            MatterLog.Info("Sigma2 MRP params: idle={0}ms, active={1}ms",
                peerMrpIdle?.TotalMilliseconds, peerMrpActive?.TotalMilliseconds);
        }

        // Generate the shared secret.
        //
        using var peerPublicKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = sigma2ResponderEphPublicKey.AsSpan(1, 32).ToArray(),
                Y = sigma2ResponderEphPublicKey.AsSpan(33, 32).ToArray()
            }
        });

        var sharedSecret = ephermeralKey.DeriveRawSecretAgreement(peerPublicKey.PublicKey);

        MatterLog.Debug("CASE SharedSecret: {0}", MatterLog.FormatSecret(sharedSecret));

        // Generate the shared key using HKDF
        //
        // Step 1 - the TranscriptHash
        //
        var transcriptHash = SHA256.HashData(sigma1Payload.AsSpan());

        // Step 2 - SALT
        ms = new MemoryStream();
        BinaryWriter saltWriter = new(ms);
        saltWriter.Write(_fabric.OperationalIPK);
        saltWriter.Write(sigma2ResponderRandom);
        saltWriter.Write(sigma2ResponderEphPublicKey);
        saltWriter.Write(transcriptHash);

        var salt = ms.ToArray();

        // Step 3 - Compute the S2K (the shared key)
        //
        var info = Encoding.ASCII.GetBytes("Sigma2");

        var sigma2Key = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 16, salt, info);

        MatterLog.Debug("S2K: {0}", MatterLog.FormatSecret(sigma2Key));

        // Step 4 - Use the S2K to decrypt the payload
        // 
        var nonce = Encoding.ASCII.GetBytes("NCASE_Sigma2N");

        AesEngine cipher = new();
        int macSize = 8 * cipher.GetBlockSize();

        AeadParameters keyParamAead = new(new KeyParameter(sigma2Key), macSize, nonce);
        CcmBlockCipher cipherMode = new(cipher);
        cipherMode.Init(false, keyParamAead);

        var outputSize = cipherMode.GetOutputSize(sigma2EncryptedPayload.Length);
        var plainTextData = new byte[outputSize];
        var result = cipherMode.ProcessBytes(sigma2EncryptedPayload, 0, sigma2EncryptedPayload.Length, plainTextData, 0);
        cipherMode.DoFinal(plainTextData, result);

        var TBEData2 = new MatterTLV(plainTextData);

        // Sigma2 verification (Matter Core Spec §4.14.2.6):
        // Extract responder NOC, optional ICAC, and TBS2 signature from TBEData2
        TBEData2.OpenStructure();
        var responderNocBytes = TBEData2.GetOctetString(1);
        byte[]? responderIcacBytes = null;
        if (TBEData2.PeekTag() == 2)
        {
            responderIcacBytes = TBEData2.GetOctetString(2);
        }

        var tbs2Signature = TBEData2.GetOctetString(3);

        // Verify NOC → (ICAC →) Root CA chain and check fabric ID
        var rootCaPubKey = _fabric.RootCAKeyPair;
        var responderNocPubKey = CertificateChainVerifier.VerifySigma2CertChain(
            responderNocBytes, responderIcacBytes, rootCaPubKey, _fabric.FabricId, _node.NodeId);

        // Verify TBS2 signature (proves responder holds NOC private key)
        CertificateChainVerifier.VerifyTbs2Signature(
            responderNocBytes, responderIcacBytes,
            sigma2ResponderEphPublicKey, ephermeralPublicKeysBytes,
            tbs2Signature, responderNocPubKey);

        MatterLog.Info("CASE Sigma2: certificate chain verified ✓");

        MatterLog.Info("┌───────────────────────┐");
        MatterLog.Debug("| SENDING CASE - Sigma3 |");
        MatterLog.Info("└───────────────────────┘");

        // Compute NOC parameters needed for both TLV encoding and re-signing
        var nocPublicKey = _fabric.OperationalCertificateKeyPair;
        var nocPublicKeyBytes = P256KeyInterop.ExportPublicKey(nocPublicKey);
        var nocKeyIdentifier = SHA1.HashData(nocPublicKeyBytes).AsSpan()[..20].ToArray();
        var notBefore = new DateTimeOffset(_fabric.OperationalCertificate.NotBefore).ToEpochTime();
        var notAfter = new DateTimeOffset(_fabric.OperationalCertificate.NotAfter).ToEpochTime();

        // Re-sign the controller's NOC over the DER TBS that the CHIP SDK will reconstruct.
        // We cannot use the X.509 DER signature because the CHIP SDK reconstructs DER from
        // TLV fields with specific conventions — the reconstructed TBS differs from BouncyCastle's.
        var encodedNocCertificateSignature = MatterCertSigner.SignNoc(
            _fabric.OperationalCertificate.SerialNumber.ToByteArrayUnsigned(),
            _fabric.RootCACertificateId,
            _fabric.RootNodeId,
            _fabric.FabricId,
            (uint)notBefore, (uint)notAfter,
            nocPublicKeyBytes,
            nocKeyIdentifier,
            _fabric.RootKeyIdentifier,
            _fabric.RootCAKeyPair);

        // Encode the controller's NOC in Matter TLV format
        var encodedNocCertificate = new MatterTLV();
        encodedNocCertificate.AddStructure();

        encodedNocCertificate.AddOctetString(1, _fabric.OperationalCertificate.SerialNumber.ToByteArrayUnsigned());
        encodedNocCertificate.AddUInt8(2, 1); // signature-algorithm

        encodedNocCertificate.AddList(3); // Issuer
        encodedNocCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
        encodedNocCertificate.EndContainer();

        encodedNocCertificate.AddUInt32(4, (uint)notBefore);
        encodedNocCertificate.AddUInt32(5, (uint)notAfter);

        encodedNocCertificate.AddList(6); // Subject
        encodedNocCertificate.AddUInt64(17, _fabric.RootNodeId.ToByteArrayUnsigned());
        encodedNocCertificate.AddUInt64(21, _fabric.FabricId.ToByteArrayUnsigned());
        encodedNocCertificate.EndContainer();

        encodedNocCertificate.AddUInt8(7, 1); // public-key-algorithm
        encodedNocCertificate.AddUInt8(8, 1); // elliptic-curve-id
        encodedNocCertificate.AddOctetString(9, nocPublicKeyBytes);

        encodedNocCertificate.AddList(10); // Extensions
        encodedNocCertificate.AddStructure(1);
        encodedNocCertificate.AddBool(1, false); // is-ca = false
        encodedNocCertificate.EndContainer();
        encodedNocCertificate.AddUInt8(2, 0x1); // key-usage: digitalSignature
        encodedNocCertificate.AddArray(3); // Extended Key Usage
        encodedNocCertificate.AddUInt8(0x02); // clientAuth
        encodedNocCertificate.AddUInt8(0x01); // serverAuth
        encodedNocCertificate.EndContainer();
        encodedNocCertificate.AddOctetString(4, nocKeyIdentifier);
        encodedNocCertificate.AddOctetString(5, _fabric.RootKeyIdentifier);
        encodedNocCertificate.EndContainer(); // Close Extensions

        encodedNocCertificate.AddOctetString(11, encodedNocCertificateSignature);
        encodedNocCertificate.EndContainer(); // Close Structure

        //MatterLog.Info("───────────────────────────────────────────────────");
        //MatterLog.Debug(encodedNocCertificate);
        //MatterLog.Info("───────────────────────────────────────────────────");

        // Build sigma-3-tbsdata
        //
        var sigma3tbs = new MatterTLV();

        sigma3tbs.AddStructure();

        sigma3tbs.AddOctetString(1, encodedNocCertificate.AsSpan()); // initiatorNOC
        sigma3tbs.AddOctetString(3, ephermeralPublicKeysBytes); // initiatorEphPubKey
        sigma3tbs.AddOctetString(4, sigma2ResponderEphPublicKey); // responderEphPubKey

        sigma3tbs.EndContainer();

        //MatterLog.Debug("sigma3tbsBytes {0}", BitConverter.ToString(sigma3tbsBytes).Replace("-", ""));

        // Sign this tbsData3.
        //
        byte[] encodedSigma3TbsSignature = _fabric.OperationalCertificateKeyPair.SignData(sigma3tbs.AsSpan(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        // Construct the sigma-3-tbe payload, which will be encrypted.
        //
        var sigma3tbe = new MatterTLV();
        sigma3tbe.AddStructure();
        sigma3tbe.AddOctetString(1, encodedNocCertificate.AsSpan());
        sigma3tbe.AddOctetString(3, encodedSigma3TbsSignature);
        sigma3tbe.EndContainer();

        //MatterLog.Debug("sigma1Bytes {0}", BitConverter.ToString(sigma1Payload.GetBytes()).Replace("-", ""));
        //MatterLog.Debug("sigma2Bytes {0}", BitConverter.ToString(sigma2Payload.GetBytes()).Replace("-", ""));

        var sigma3TranscriptWriter = new MatterMessageWriter();
        sigma3TranscriptWriter.Write(sigma1Payload.AsSpan());
        sigma3TranscriptWriter.Write(sigma2Payload.AsSpan());
        var sigma3tbeTranscriptHash = SHA256.HashData(sigma3TranscriptWriter.WrittenSpan);

        MatterLog.Debug("S3 TranscriptHash {0}", MatterLog.FormatBytes(sigma3tbeTranscriptHash));

        ms = new MemoryStream();
        saltWriter = new BinaryWriter(ms);
        saltWriter.Write(_fabric.OperationalIPK);
        saltWriter.Write(sigma3tbeTranscriptHash);

        salt = ms.ToArray();

        MatterLog.Debug("S3 Salt {0}", MatterLog.FormatSecret(salt));

        // Step 3 - Compute the S3K (the shared key)
        //
        info = Encoding.ASCII.GetBytes("Sigma3");

        var sigma3Key = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 16, salt, info);

        MatterLog.Debug("S3K: {0}", MatterLog.FormatSecret(sigma3Key));

        nonce = Encoding.ASCII.GetBytes("NCASE_Sigma3N");

        keyParamAead = new AeadParameters(new KeyParameter(sigma3Key), macSize, nonce);
        cipherMode = new CcmBlockCipher(cipher);
        cipherMode.Init(true, keyParamAead);

        var sigma3tbeBytes = sigma3tbe.GetBytes();

        outputSize = cipherMode.GetOutputSize(sigma3tbeBytes.Length);
        var encryptedData = new byte[outputSize];
        result = cipherMode.ProcessBytes(sigma3tbeBytes, 0, sigma3tbeBytes.Length, encryptedData, 0);
        cipherMode.DoFinal(encryptedData, result);

        MatterLog.Debug(
            "Sigma3 artifacts: nocPubKey={0}B noc={1}B signatureData={2}B signature={3}B payload={4}B encrypted={5}B",
            nocPublicKeyBytes.Length,
            encodedNocCertificate.AsSpan().Length,
            sigma3tbs.AsSpan().Length,
            encodedNocCertificateSignature.Length,
            sigma3tbeBytes.Length,
            encryptedData.Length);

        var sigma3Payload = new MatterTLV();
        sigma3Payload.AddStructure();
        sigma3Payload.AddOctetString(1, encryptedData); // sigma3EncryptedPayload
        sigma3Payload.EndContainer();

        var sigma3MessagePayload = new MessagePayload(sigma3Payload);

        sigma3MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

        sigma3MessagePayload.ProtocolId = 0x00;
        sigma3MessagePayload.ProtocolOpCode = 0x32; // Sigma3

        var sigma3MessageFrame = new MessageFrame(sigma3MessagePayload);

        sigma3MessageFrame.MessageFlags |= MessageFlags.S;
        sigma3MessageFrame.SecurityFlags = 0x00;
        sigma3MessageFrame.SourceNodeID = 0x00;

        await caseExchange.SendAsync(sigma3MessageFrame);

        var successMessageFrame = await caseExchange.WaitForNextMessageAsync(ct);

        await caseExchange.AcknowledgeMessageAsync(successMessageFrame.MessageCounter);

        caseExchange.Close();

        //MatterLog.Info("operationalIdentityProtectionKey: {0}", BitConverter.ToString(fabric.OperationalIPK).Replace("-", ""));
        //MatterLog.Debug("sigma1Bytes: {0}", BitConverter.ToString(sigma1Payload.GetBytes()).Replace("-", ""));
        //MatterLog.Debug("sigma2Bytes: {0}", BitConverter.ToString(sigma2Payload.GetBytes()).Replace("-", ""));
        //MatterLog.Debug("sigma3Bytes: {0}", BitConverter.ToString(sigma3Payload.GetBytes()).Replace("-", ""));

        byte[] caseInfo = Encoding.ASCII.GetBytes("SessionKeys");

        var transcriptWriter = new MatterMessageWriter();
        transcriptWriter.Write(sigma1Payload.AsSpan());
        transcriptWriter.Write(sigma2Payload.AsSpan());
        transcriptWriter.Write(sigma3Payload.AsSpan());
        transcriptHash = SHA256.HashData(transcriptWriter.WrittenSpan);

        //MatterLog.Debug("hash: {0}", BitConverter.ToString(transcriptHash).Replace("-", ""));

        ms = new MemoryStream();
        saltWriter = new BinaryWriter(ms);
        saltWriter.Write(_fabric.OperationalIPK);
        saltWriter.Write(transcriptHash);

        var secureSessionSalt = ms.ToArray();

        //MatterLog.Debug("sharedSecret: {0}", BitConverter.ToString(sharedSecret).Replace("-", ""));
        //MatterLog.Debug("salt: {0}", BitConverter.ToString(secureSessionSalt).Replace("-", ""));

        var caseKeys = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 48, secureSessionSalt, caseInfo);

        var encryptKey = caseKeys.AsSpan()[..16].ToArray();
        var decryptKey = caseKeys.AsSpan().Slice(16, 16).ToArray();
        var attestationKey = caseKeys.AsSpan().Slice(32, 16).ToArray();

        MatterLog.Debug("decryptKey: {0}", MatterLog.FormatSecret(decryptKey));
        MatterLog.Debug("encryptKey: {0}", MatterLog.FormatSecret(encryptKey));
        MatterLog.Debug("attestationKey: {0}", MatterLog.FormatSecret(attestationKey));

        var udpConnection = _unsecureSession.CreateNewConnection();

        // Convert node IDs from BigInteger to ulong for nonce construction.
        // RootNodeId: created with LE flag, ToByteArrayUnsigned() gives correct LE bytes by accident.
        var srcNodeId = BitConverter.ToUInt64(_fabric.RootNodeId.ToByteArrayUnsigned());
        // NodeId: ToByteArrayUnsigned() gives BE bytes, same as TLV cert stores.
        // BitConverter.ToUInt64 on LE system interprets as LE, giving same uint64 the device has.
        var destIdBytes = _node.NodeId.ToByteArrayUnsigned();
        var destPadded = new byte[8];
        if (destIdBytes.Length <= 8)
        {
            Array.Copy(destIdBytes, 0, destPadded, 8 - destIdBytes.Length, destIdBytes.Length);
        }
        else
        {
            Array.Copy(destIdBytes, destIdBytes.Length - 8, destPadded, 0, 8);
        }

        var destNodeId = BitConverter.ToUInt64(destPadded);

        var caseSession = new CaseSecureSession(udpConnection,
                                                srcNodeId,
                                                destNodeId,
                                                BitConverter.ToUInt16(spake1SessionId),
                                                sigma2ResponderSessionId,
                                                encryptKey,
                                                decryptKey);

        // Apply peer's MRP parameters if provided in Sigma2
        if (peerMrpIdle.HasValue)
        {
            caseSession.PeerMrpIdleRetransTimeout = peerMrpIdle.Value;
        }

        if (peerMrpActive.HasValue)
        {
            caseSession.PeerMrpActiveRetransTimeout = peerMrpActive.Value;
        }

        return caseSession;
    }
}
