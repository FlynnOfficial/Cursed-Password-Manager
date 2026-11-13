using System.Text.Json;

namespace CursedPasswordManager.Core;

public sealed class PasswordVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Password { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Credential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Service { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
    public string Category { get; set; } = "Outros";
    public bool Favorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PasswordVersion> History { get; set; } = [];
    public string FavoriteMark => Favorite ? "★" : "☆";
    [System.Text.Json.Serialization.JsonIgnore] public DateTime CreatedLocal => CreatedAt.LocalDateTime;
    [System.Text.Json.Serialization.JsonIgnore] public DateTime ModifiedLocal => ModifiedAt.LocalDateTime;
    public void ChangePassword(string password)
    {
        if (Password == password) return;
        History.Insert(0, new() { Password = Password });
        Password = password;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class VaultData
{
    public List<string> Categories { get; set; } = ["Jogos", "Trabalho", "E-mails", "Bancos", "Redes Sociais", "Outros"];
    public List<Credential> Credentials { get; set; } = [];
    public VaultData Clone()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try { return JsonSerializer.Deserialize<VaultData>(bytes)!; }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Validate()
    {
        if (Categories is null || Credentials is null || Categories.Count > 500 || Credentials.Count > 10000)
            throw new InvalidDataException("Quantidade de registros ou categorias inválida.");
        if (!Categories.Contains("Outros") || Categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Categories.Count)
            throw new InvalidDataException("Categorias inválidas.");
        foreach (var category in Categories) Check(category, 100, true);
        if (Credentials.Any(c => c is null) || Credentials.Select(c => c.Id).Distinct().Count() != Credentials.Count)
            throw new InvalidDataException("Identificadores de registros inválidos.");
        foreach (var c in Credentials)
        {
            if (c.Id == Guid.Empty || c.History is null || c.History.Count > 1000 || !Categories.Contains(c.Category))
                throw new InvalidDataException("Registro inválido.");
            Check(c.Service, 500, true); Check(c.Account, 1000); Check(c.Password, 4096, true);
            if (c.History.Any(h => h is null) || c.History.Select(h => h.Id).Distinct().Count() != c.History.Count)
                throw new InvalidDataException("Histórico inválido.");
            foreach (var h in c.History) Check(h.Password, 4096, true);
        }
    }
    private static void Check(string? value, int max, bool required = false)
    {
        if (value is null || value.Length > max || (required && string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException("Campo vazio ou maior que o limite permitido.");
    }
    public IEnumerable<Credential> Search(string query, bool favoritesOnly, string? category, string sort) =>
        Sort(Credentials.Where(c => (!favoritesOnly || c.Favorite) && (string.IsNullOrEmpty(category) || c.Category == category)
            && (c.Service.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Account.Contains(query, StringComparison.OrdinalIgnoreCase))), sort);
    private static IOrderedEnumerable<Credential> Sort(IEnumerable<Credential> items, string sort) => sort switch
    {
        "Criação" => items.OrderByDescending(c => c.CreatedAt),
        "Alteração" => items.OrderByDescending(c => c.ModifiedAt),
        "Favoritos" => items.OrderByDescending(c => c.Favorite).ThenBy(c => c.Service),
        "Categoria" => items.OrderBy(c => c.Category).ThenBy(c => c.Service),
        _ => items.OrderBy(c => c.Service, StringComparer.CurrentCultureIgnoreCase)
    };
    public void RenameCategory(string oldName, string newName)
    {
        Check(newName, 100, true);
        if (oldName == "Outros" || Categories.Contains(newName, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Categoria reservada ou já existente.");
        var i = Categories.IndexOf(oldName);
        if (i < 0) throw new InvalidOperationException("Categoria não encontrada.");
        Categories[i] = newName;
        foreach (var c in Credentials.Where(c => c.Category == oldName)) c.Category = newName;
    }
    public void RemoveCategory(string category)
    {
        if (category == "Outros") throw new InvalidOperationException("A categoria Outros é obrigatória.");
        Categories.Remove(category);
        foreach (var c in Credentials.Where(c => c.Category == category)) c.Category = "Outros";
    }
    // Preserve both sides of divergent IDs or same-name accounts; never silently overwrite.
    public int Merge(VaultData incoming)
    {
        incoming.Validate();
        foreach (var category in incoming.Categories)
            if (!Categories.Contains(category)) Categories.Add(category);
        int added = 0;
        foreach (var source in incoming.Clone().Credentials)
        {
            var existing = Credentials.FirstOrDefault(c => c.Id == source.Id);
            if (existing is not null)
            {
                if (Equivalent(existing, source)) continue;
                source.Id = Guid.NewGuid();
                source.Service = source.Service.Length <= 480 ? source.Service + " (importado)" : source.Service;
            }
            else if (Credentials.Any(c => Equivalent(c, source))) continue;
            Credentials.Add(source); added++;
        }
        Validate(); return added;
    }
    private static bool Equivalent(Credential a, Credential b) => a.Service == b.Service && a.Account == b.Account && a.Password == b.Password
        && a.Category == b.Category && a.Favorite == b.Favorite && a.CreatedAt == b.CreatedAt && a.ModifiedAt == b.ModifiedAt
        && a.History.Count == b.History.Count && a.History.Zip(b.History).All(p => p.First.Password == p.Second.Password && p.First.ChangedAt == p.Second.ChangedAt);
}
