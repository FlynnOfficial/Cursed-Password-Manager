using System.Text.Json;

namespace CursedPasswordManager.Storage;

public sealed class AppSettings
{
    public string Theme { get; set; } = "XpClassic";
    public string PrimaryFolder { get; set; } = "Data/Vault";
    public string SecondaryFolder { get; set; } = "Data/Backup";
    public bool SecondaryEnabled { get; set; } = true;
    public int AutoLockMinutes { get; set; } = 10;
    public int ClipboardSeconds { get; set; } = 30;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public void Validate()
    {
        if (Theme is not ("XpClassic" or "CursedXp" or "CursedModern") || AutoLockMinutes is < 1 or > 120 || ClipboardSeconds is < 5 or > 120
            || string.IsNullOrWhiteSpace(PrimaryFolder) || string.IsNullOrWhiteSpace(SecondaryFolder) || PrimaryFolder.Length > 1000 || SecondaryFolder.Length > 1000)
            throw new InvalidDataException("Configurações inválidas.");
    }
}

public sealed class AppPaths
{
    public bool Portable { get; }
    public string Root { get; }
    public string Recovery => Path.Combine(Root, "Recovery");
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public AppPaths(bool portable, string? portableRoot = null)
    {
        Portable = portable;
        Root = portable ? Path.GetFullPath(portableRoot ?? Path.Combine(AppContext.BaseDirectory, "Data"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursedPasswordManager");
    }
    public string Resolve(string path) => Path.GetFullPath(path, Root);
    public string StorePath(string path) => Portable ? Path.GetRelativePath(Root, Path.GetFullPath(path)) : Path.GetFullPath(path);
    public IReadOnlyList<string> Replicas(AppSettings settings)
    {
        List<string> paths = [Path.Combine(Recovery, "passwords.cpmvault"), Path.Combine(Resolve(settings.PrimaryFolder), "passwords.cpmvault")];
        if (settings.SecondaryEnabled) paths.Add(Path.Combine(Resolve(settings.SecondaryFolder), "passwords.cpmvault"));
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count) throw new InvalidOperationException("Escolha pastas distintas para cada cópia.");
        return paths;
    }
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath)) return new();
        var bytes = await AtomicFile.ReadBoundedAsync(SettingsPath, 16384);
        var settings = JsonSerializer.Deserialize<AppSettings>(bytes, new JsonSerializerOptions { MaxDepth = 8 }) ?? throw new InvalidDataException("Configurações vazias.");
        settings.Validate(); return settings;
    }
    public Task SaveAsync(AppSettings settings)
    {
        settings.Validate(); _ = Replicas(settings);
        return AtomicFile.WriteAsync(SettingsPath, JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
