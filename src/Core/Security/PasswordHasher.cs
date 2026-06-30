using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Core.Security;

/// <summary>
/// Argon2id hashing, stored in a self-describing PHC-style string so the cost
/// parameters can be raised later without breaking existing hashes.
/// Format: $argon2id$v=19$m=&lt;kb&gt;,t=&lt;iter&gt;,p=&lt;par&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int HashSize = 32; // 256-bit

    // Tune to the host machine: higher memory/iterations = stronger but slower login.
    private const int Parallelism = 4;
    private const int Iterations = 3;
    private const int MemoryKb = 64 * 1024; // 64 MB

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Compute(password, salt, MemoryKb, Iterations, Parallelism);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        try
        {
            // parts: argon2id | v=19 | m=..,t=..,p=.. | salt | hash
            var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
            var costs = parts[2].Split(',');
            int memoryKb = int.Parse(costs[0][2..]);
            int iterations = int.Parse(costs[1][2..]);
            int parallelism = int.Parse(costs[2][2..]);
            byte[] salt = Convert.FromBase64String(parts[3]);
            byte[] expected = Convert.FromBase64String(parts[4]);

            byte[] actual = Compute(password, salt, memoryKb, iterations, parallelism, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected); // constant-time
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Compute(
        string password,
        byte[] salt,
        int memoryKb,
        int iterations,
        int parallelism,
        int size = HashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(size);
    }
}
