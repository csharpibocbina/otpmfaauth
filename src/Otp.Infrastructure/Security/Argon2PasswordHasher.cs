using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Otp.Application.Abstractions;

namespace Otp.Infrastructure.Security;

/// <summary>Argon2id. Format: v1$salt$hash (base64).</summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16, HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return $"v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(Derive(password, salt))}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts is not ["v1", var s, var h]) return false;
        try
        {
            var expected = Convert.FromBase64String(h);
            return CryptographicOperations.FixedTimeEquals(Derive(password, Convert.FromBase64String(s)), expected);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt, DegreeOfParallelism = 2, MemorySize = 19 * 1024, Iterations = 2,
        };
        return argon.GetBytes(HashSize);
    }
}
