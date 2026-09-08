using System.Security.Cryptography;

// La privada usa PEM (ImportFromPem/ExportECPrivateKeyPem): son métodos de .NET 5+, pero el
// servidor corre en net10.0 exclusivamente, así que no hay restricción de compatibilidad ahí.
//
// La pública, en cambio, se embebe en src/GvrTools.Licensing (net48 + net8.0-windows). .NET
// Framework 4.8 NO tiene ImportSubjectPublicKeyInfo (es de .NET 5+): el único formato común a
// ambos frameworks es ECParameters con el punto (X, Y) crudo, disponible desde .NET Framework
// 4.6.2. Por eso la pública se imprime como 64 bytes crudos (X de 32 + Y de 32) en base64, no como
// SubjectPublicKeyInfo -- ver src/GvrTools.Licensing/Crypto/EcdsaEntitlementSignatureVerifier.cs.

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

var privatePem = key.ExportECPrivateKeyPem();

var parameters = key.ExportParameters(includePrivateParameters: false);
var rawPoint = new byte[64];
parameters.Q.X!.CopyTo(rawPoint, 0);
parameters.Q.Y!.CopyTo(rawPoint, 32);
var publicKeyBase64 = Convert.ToBase64String(rawPoint);

Console.WriteLine("=== Privada -- variable de entorno Signing__PrivateKeyPem en EasyPanel, NUNCA en git ===");
Console.WriteLine(privatePem);
Console.WriteLine();
Console.WriteLine("=== Pública (punto X||Y crudo, 64 bytes) -- EmbeddedPublicKey.Base64 en GvrTools.Licensing/Crypto ===");
Console.WriteLine(publicKeyBase64);
