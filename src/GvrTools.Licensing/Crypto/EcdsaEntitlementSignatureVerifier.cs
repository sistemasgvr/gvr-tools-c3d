using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using GvrTools.Licensing.Entitlements;

namespace GvrTools.Licensing.Crypto
{
    /// <summary>
    /// Verifica con ECDsa P-256 (System.Security.Cryptography, nativo en net48 y net8.0-windows) y
    /// parsea con DataContractJsonSerializer (System.Runtime.Serialization.Json, también nativo en
    /// ambos) -- cero NuGet, igual que el resto de GvrTools.Licensing (ver
    /// GvrTools.Licensing.csproj). No usar System.Text.Json aquí: no viene con .NET Framework 4.8.
    /// </summary>
    public sealed class EcdsaEntitlementSignatureVerifier : IEntitlementSignatureVerifier
    {
        private readonly ECDsa _publicKey;

        public EcdsaEntitlementSignatureVerifier(string publicKeyRawPointBase64 = null)
        {
            // Punto (X, Y) crudo de 64 bytes, no SubjectPublicKeyInfo: .NET Framework 4.8 no tiene
            // ImportSubjectPublicKeyInfo (es de .NET 5+). ECParameters es el formato común a net48
            // y net8.0-windows (ver GvrLicense.Infrastructure/Signing/EcdsaEntitlementSigner.cs).
            var rawPoint = Convert.FromBase64String(publicKeyRawPointBase64 ?? EmbeddedPublicKey.Base64);
            if (rawPoint.Length != 64)
            {
                throw new ArgumentException("La clave pública debe ser un punto P-256 crudo de 64 bytes (X||Y).", nameof(publicKeyRawPointBase64));
            }

            var x = new byte[32];
            var y = new byte[32];
            Array.Copy(rawPoint, 0, x, 0, 32);
            Array.Copy(rawPoint, 32, y, 0, 32);

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            };

            _publicKey = ECDsa.Create();
            _publicKey.ImportParameters(parameters);
        }

        public bool TryVerify(string rawJson, byte[] signature, out EntitlementBlob blob)
        {
            blob = null;

            if (string.IsNullOrEmpty(rawJson) || signature == null || signature.Length == 0)
            {
                return false;
            }

            var data = Encoding.UTF8.GetBytes(rawJson);

            bool signatureValid;
            try
            {
                signatureValid = _publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException)
            {
                return false;
            }

            if (!signatureValid)
            {
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(data))
                {
                    var serializer = new DataContractJsonSerializer(typeof(EntitlementBlob));
                    blob = (EntitlementBlob)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is FormatException)
            {
                blob = null;
                return false;
            }

            return blob != null;
        }
    }
}
