using System.Security.Cryptography;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public PasswordHashResult Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return new PasswordHashResult(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            DefaultIterations);
    }

    public bool Verify(string password, string hash, string salt, int iterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Convert.FromBase64String(salt),
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
