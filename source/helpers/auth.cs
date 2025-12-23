// helpers/auth.cs

namespace ASP.NETCoreWebApi.helpers;

public static class AuthHelpers {
    private const System.Int32 SaltSize = 16; // 128 bits
    private const System.Int32 KeySize = 32;  // 256 bits
    private const System.Int32 Iterations = 100_000; // Higher is slower, but more secure

    public static string HashPassword(string password) {
        // Generate a salt
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltSize);

        // Derive a key (hash) from password and salt
        byte[] hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, KeySize);

        // Store as base64: {salt}.{hash}
        return $"{System.Convert.ToBase64String(salt)}.{System.Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash) {
        try {
            string[] parts = storedHash.Split('.');
            if (parts.Length != 2)
                return false;

            byte[] salt = System.Convert.FromBase64String(parts[0]);
            byte[] expectedHash = System.Convert.FromBase64String(parts[1]);

            byte[] actualHash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, KeySize);

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        } catch {
            return false;
        }
    }

}
