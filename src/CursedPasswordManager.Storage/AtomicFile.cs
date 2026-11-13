using CursedPasswordManager.Security;

namespace CursedPasswordManager.Storage;

public static class AtomicFile
{
    public static async Task<byte[]> ReadBoundedAsync(string path, int maxBytes = VaultCipher.MaxFileBytes)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > maxBytes) throw new InvalidDataException("Tamanho de arquivo inválido.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes);
        if (stream.ReadByte() != -1) throw new IOException("O arquivo mudou durante a leitura.");
        return bytes;
    }
    public static async Task WriteAsync(string path, byte[] bytes, Action? beforeCommit = null)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, ".cpm-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes); await stream.FlushAsync(); stream.Flush(true); }
            var check = await ReadBoundedAsync(temp, Math.Max(bytes.Length, 1));
            if (!check.AsSpan().SequenceEqual(bytes)) throw new IOException("Falha na verificação da gravação temporária.");
            beforeCommit?.Invoke();
            if (File.Exists(path)) File.Replace(temp, path, path + ".prev", true);
            else File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
