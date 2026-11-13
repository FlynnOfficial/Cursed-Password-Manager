namespace CursedPasswordManager.Core;

public sealed record AuditFinding(Guid Id, string Service, string Account, string Reasons);
public sealed record AuditReport(int Total, int Reused, int Weak, IReadOnlyList<AuditFinding> Findings);
public static class PasswordAudit
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    { "password", "password123", "123456", "12345678", "123456789", "1234567890", "qwerty", "qwerty123", "admin", "letmein", "senha", "senha123", "iloveyou", "welcome", "abc123" };
    public static AuditReport Analyze(VaultData data)
    {
        var reused = data.Credentials.GroupBy(c => c.Password, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g).Select(c => c.Id).ToHashSet();
        List<AuditFinding> findings = []; int weak = 0;
        foreach (var c in data.Credentials)
        {
            List<string> reasons = []; string p = c.Password;
            if (p.Length < 12) reasons.Add("menos de 12 caracteres");
            if (p.All(char.IsAsciiDigit) && p.Length < 20) reasons.Add("numérica curta");
            if (Common.Contains(p)) reasons.Add("senha comum na lista local");
            if (p.Distinct().Count() <= Math.Max(2, p.Length / 4)) reasons.Add("repetição excessiva");
            if (Enumerable.Range(1, p.Length / 2).Any(n => p.Length % n == 0 && string.Concat(Enumerable.Repeat(p[..n], p.Length / n)) == p)) reasons.Add("bloco repetido");
            string lower = p.ToLowerInvariant();
            string[] sequences = ["01234567890123456789", "98765432109876543210", "abcdefghijklmnopqrstuvwxyz", "zyxwvutsrqponmlkjihgfedcba", "qwertyuiop", "asdfghjkl"];
            if (sequences.Any(s => Enumerable.Range(0, Math.Max(0, lower.Length - 4)).Any(i => s.Contains(lower.Substring(i, 5), StringComparison.Ordinal)))) reasons.Add("sequência simples");
            if (reasons.Count > 0) weak++;
            if (reused.Contains(c.Id)) reasons.Add("reutilizada em outra conta");
            if (reasons.Count > 0) findings.Add(new(c.Id, c.Service, c.Account, string.Join("; ", reasons)));
        }
        return new(data.Credentials.Count, reused.Count, weak, findings);
    }
}
