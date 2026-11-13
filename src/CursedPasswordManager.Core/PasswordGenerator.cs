using System.Numerics;
using System.Security.Cryptography;

namespace CursedPasswordManager.Core;

public sealed record GeneratorOptions(int Length = 30, bool Digits = true, bool Lowercase = false, bool Uppercase = false, bool Symbols = false, bool RequireEach = true);
public sealed record GeneratedPassword(string Value, double EntropyBits);
public static class PasswordGenerator
{
    public const string SymbolAlphabet = "!@#$%^&*()-_=+[]{};:,.?/~";
    public static string[] Groups(GeneratorOptions o)
    {
        if (o.Length is < 10 or > 60) throw new ArgumentOutOfRangeException(nameof(o), "O comprimento deve estar entre 10 e 60.");
        List<string> groups = [];
        if (o.Digits) groups.Add("0123456789");
        if (o.Lowercase) groups.Add("abcdefghijklmnopqrstuvwxyz");
        if (o.Uppercase) groups.Add("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (o.Symbols) groups.Add(SymbolAlphabet);
        if (groups.Count == 0) throw new ArgumentException("Selecione pelo menos um grupo de caracteres.");
        return [.. groups];
    }
    public static GeneratedPassword Generate(GeneratorOptions options)
    {
        var groups = Groups(options);
        string alphabet = string.Concat(groups);
        char[] result = new char[options.Length];
        // Rejection sampling: every full candidate is uniform. Conditioning on the
        // predicate preserves equal probability for every accepted string.
        do
        {
            for (int i = 0; i < result.Length; i++) result[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        } while (options.RequireEach && groups.Any(g => !result.Any(g.Contains)));
        var value = new string(result);
        Array.Clear(result);
        return new(value, Entropy(options));
    }
    public static double Entropy(GeneratorOptions o)
    {
        var groups = Groups(o); int size = groups.Sum(g => g.Length);
        if (!o.RequireEach) return o.Length * Math.Log2(size);
        BigInteger count = 0;
        for (int mask = 0; mask < (1 << groups.Length); mask++)
        {
            int excluded = 0, bits = 0;
            for (int j = 0; j < groups.Length; j++) if ((mask & (1 << j)) != 0) { excluded += groups[j].Length; bits++; }
            var term = BigInteger.Pow(size - excluded, o.Length);
            count += bits % 2 == 0 ? term : -term;
        }
        return BigInteger.Log(count, 2);
    }
}
