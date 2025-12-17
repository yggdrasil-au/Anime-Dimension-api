// helpers/auth.cs

namespace ASP.NETCoreWebApi;

public static class AuthHelpers {
    private const System.Int32 SaltSize = 16; // 128 bits
    private const System.Int32 KeySize = 32;  // 256 bits
    private const System.Int32 Iterations = 100_000; // Higher is slower, but more secure

    public static System.String HashPassword(System.String password) {
        // Generate a salt
        System.Byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltSize);

        // Derive a key (hash) from password and salt
        using System.Security.Cryptography.Rfc2898DeriveBytes pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
        System.Byte[] hash = pbkdf2.GetBytes(KeySize);

        // Store as base64: {salt}.{hash}
        return $"{System.Convert.ToBase64String(salt)}.{System.Convert.ToBase64String(hash)}";
    }

    public static System.Boolean VerifyPassword(System.String password, System.String storedHash) {
        try {
            System.String[] parts = storedHash.Split('.');
            if (parts.Length != 2)
                return false;

            System.Byte[] salt = System.Convert.FromBase64String(parts[0]);
            System.Byte[] expectedHash = System.Convert.FromBase64String(parts[1]);

            using System.Security.Cryptography.Rfc2898DeriveBytes pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
            System.Byte[] actualHash = pbkdf2.GetBytes(KeySize);

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        } catch {
            return false;
        }
    }

}
