using CursedPasswordManager.Security;

namespace CursedPasswordManager.Storage;

public sealed record ReplicaCandidate(string Path, byte[] Bytes, VaultHeader Header)
{
    public string Hash => VaultCipher.Hash(Bytes);
    public string Label => $"Revisão {Header.Revision} • {System.IO.Path.GetDirectoryName(Path)} • {Hash[..12]}";
}
public sealed record ReplicaState(string Path, string Status, DateTimeOffset? LastSynchronized = null);
public sealed record SaveResult(IReadOnlyList<ReplicaState> States)
{
    public bool Complete => States.All(s => s.Status == "Íntegra");
}
public sealed record ScanResult(IReadOnlyList<ReplicaCandidate> Candidates, IReadOnlyList<ReplicaState> States);

public sealed class ReplicaStore
{
    private readonly string[] _paths;
    private readonly Dictionary<string, string?> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public IReadOnlyList<string> Paths => _paths;
    public ReplicaStore(IEnumerable<string> paths)
    {
        _paths = paths.Select(Path.GetFullPath).ToArray();
        if (_paths.Length < 2 || _paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _paths.Length) throw new ArgumentException("As pastas de cópias precisam ser distintas.");
    }
    public async Task<ScanResult> ScanAsync()
    {
        List<ReplicaCandidate> candidates = []; List<ReplicaState> states = [];
        foreach (string path in _paths)
        {
            try
            {
                var bytes = await AtomicFile.ReadBoundedAsync(path);
                _observed[path] = VaultCipher.Hash(bytes);
                var header = VaultCipher.ReadHeader(bytes);
                candidates.Add(new(path, bytes, header)); states.Add(new(path, "Aguardando autenticação"));
            }
            catch (FileNotFoundException) { _observed[path] = null; states.Add(new(path, "Ausente")); }
            catch (DirectoryNotFoundException) { _observed[path] = null; states.Add(new(path, "Ausente")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { states.Add(new(path, "Indisponível ou formato inválido")); }
        }
        return new(candidates, states);
    }
    public async Task<IReadOnlyList<ReplicaState>> VerifyAsync(VaultSession session, string currentHash)
    {
        List<ReplicaState> result = [];
        foreach (var path in _paths)
        {
            try
            {
                var bytes = await AtomicFile.ReadBoundedAsync(path); _ = VaultCipher.Decrypt(bytes, session);
                result.Add(new(path, VaultCipher.Hash(bytes) == currentHash ? "Íntegra" : "Divergente"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            { result.Add(new(path, "Indisponível ou não autenticada")); }
        }
        return result;
    }
    public async Task<SaveResult> CommitAsync(byte[] bytes, VaultSession session)
    {
        _ = VaultCipher.Decrypt(bytes, session);
        await _gate.WaitAsync();
        List<FileStream> locks = []; List<ReplicaState> states = [];
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in _paths.Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    locks.Add(new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                    available.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { states.Add(new(path, "Indisponível: não foi possível obter acesso exclusivo")); }
            }
            if (!available.Contains(_paths[0])) throw new IOException("A recuperação interna está indisponível. Nenhuma nova cópia foi gravada.");
            // Preflight all reachable replicas before changing any of them.
            foreach (var path in available) await CheckUnchangedAsync(path);
            foreach (var path in _paths.Where(available.Contains))
            {
                try
                {
                    if (path == _paths[0] && File.Exists(path))
                    {
                        var previous = await AtomicFile.ReadBoundedAsync(path);
                        string snapshot = Path.Combine(Path.GetDirectoryName(path)!, "snapshot-" + VaultCipher.Hash(previous) + ".cpmvault");
                        if (!File.Exists(snapshot)) await AtomicFile.WriteAsync(snapshot, previous);
                    }
                    await AtomicFile.WriteAsync(path, bytes);
                    _observed[path] = VaultCipher.Hash(bytes);
                    states.Add(new(path, "Íntegra", DateTimeOffset.Now));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (path == _paths[0]) throw new IOException("Falha ao gravar a recuperação interna. A operação não foi confirmada.", ex);
                    states.Add(new(path, "Pendente: falha na gravação"));
                }
            }
            // Keep five previous encrypted checkpoints; cleanup failure never undoes a committed save.
            try
            {
                foreach (var old in new DirectoryInfo(Path.GetDirectoryName(_paths[0])!).GetFiles("snapshot-*.cpmvault").OrderByDescending(f => f.LastWriteTimeUtc).Skip(5)) old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { states.Add(new(_paths[0], "Limpeza de snapshots pendente")); }
            return new(states);
        }
        finally { foreach (var handle in locks) handle.Dispose(); _gate.Release(); }
    }
    private async Task CheckUnchangedAsync(string path)
    {
        string? current = File.Exists(path) ? VaultCipher.Hash(await AtomicFile.ReadBoundedAsync(path)) : null;
        if (!_observed.TryGetValue(path, out var expected))
        {
            if (current is not null) throw new IOException("O destino já contém um cofre não revisado. Desbloqueie e resolva a divergência antes de salvar.");
            _observed[path] = null;
        }
        else if (current != expected && current is not null)
            throw new IOException("Uma cópia foi alterada por outro processo. Bloqueie e abra novamente para resolver o conflito.");
    }
    public IEnumerable<string> RecoveryFiles() => _paths.SelectMany(p => new[] { p, p + ".prev" }).Where(File.Exists)
        .Concat(Directory.Exists(Path.GetDirectoryName(_paths[0])) ? Directory.EnumerateFiles(Path.GetDirectoryName(_paths[0])!, "snapshot-*.cpmvault") : []);
}
