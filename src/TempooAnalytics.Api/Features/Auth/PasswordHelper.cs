using System.Security.Cryptography;
using System.Text;

namespace TempooAnalytics.Api.Auth;

public static class PasswordHelper
{
    public static string HashPassword(string password, int iterations = 100_000)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 3) return false;
            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
