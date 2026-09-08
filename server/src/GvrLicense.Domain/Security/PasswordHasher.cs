using System.Security.Cryptography;
using System.Text;

namespace GvrLicense.Domain.Security;

/// <summary>
/// PBKDF2-SHA256, nativo (sin NuGet). Compartido entre el login del admin
/// (GvrLicense.Api/Pages/Admin) y las herramientas de sembrado (server/tools/*) para no duplicar el
/// algoritmo. Formato de hash: "iteraciones.saltBase64.hashBase64".
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int HashLength = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashLength);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
