using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CursedPasswordManager.Core;
using Konscious.Security.Cryptography;

namespace CursedPasswordManager.Security;

public sealed record KdfParameters(int MemoryKiB = 65536, int Iterations = 3, int Parallelism = 1)
{
    public void Validate()
    {
        if (MemoryKiB is < 19456 or > 262144 || Iterations is < 2 or > 8 || Parallelism is < 1 or > 4)
            throw new InvalidDataException("Parâmetros Argon2id fora dos limites seguros.");
    }
}

public sealed class VaultSession : IDisposable
{
    internal byte[] Key { get; }
    public byte[] Salt { get; }
    public KdfParameters Parameters { get; }
    public Guid VaultId { get; }
    public bool IsDisposed { get; private set; }
    internal VaultSession(byte[] key, byte[] salt, KdfParameters parameters, Guid vaultId)
        => (Key, Salt, Parameters, VaultId) = (key, salt, parameters, vaultId);
    public void Dispose() { CryptographicOperations.ZeroMemory(Key); IsDisposed = true; }
}
public sealed record VaultHeader(Guid VaultId, long Revision, Guid CommitId, byte[] ParentHash, byte[] Salt, KdfParameters Parameters);
public sealed record OpenedVault(VaultSession Session, VaultData Data, VaultHeader Header);

public static class VaultCipher
{
    public const int MaxFileBytes = 32 * 1024 * 1024;
    private const int HeaderLength = 112;
    private const int EnvelopeLength = HeaderLength + 12 + 32 + 16;
    private const int PayloadOffset = EnvelopeLength + 12 + 4;
    private static readonly byte[] Magic = "CPMVLT01"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 24, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static async Task<VaultSession> CreateSessionAsync(string password, Guid? id = null, KdfParameters? parameters = null)
    {
        ValidatePassword(password);
        bool calibrate = parameters is null;
        parameters ??= new(); parameters.Validate();
        var salt = RandomNumberGenerator.GetBytes(16);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var key = await DeriveAsync(password, salt, parameters);
        timer.Stop();
        if (calibrate)
        {
            int iterations = Math.Clamp((int)Math.Ceiling(3 * 500.0 / Math.Max(1, timer.Elapsed.TotalMilliseconds)), 3, 8);
            if (iterations != parameters.Iterations)
            {
                CryptographicOperations.ZeroMemory(key);
                parameters = parameters with { Iterations = iterations };
                key = await DeriveAsync(password, salt, parameters);
            }
        }
        return new(key, salt, parameters, id ?? Guid.NewGuid());
    }
    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 1024)
            throw new ArgumentException("Use uma senha mestra de 12 a 1024 caracteres. Prefira uma frase longa e exclusiva.");
    }
    private static async Task<byte[]> DeriveAsync(string password, byte[] salt, KdfParameters parameters)
    {
        if (password.Length > 1024) throw new ArgumentException("Senha maior que o limite permitido.");
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = parameters.MemoryKiB, Iterations = parameters.Iterations, DegreeOfParallelism = parameters.Parallelism };
            return await argon.GetBytesAsync(32).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static VaultHeader ReadHeader(byte[] bytes)
    {
        if (bytes.Length < PayloadOffset + 16 || bytes.Length > MaxFileBytes) throw new InvalidDataException("Tamanho do cofre inválido.");
        using var r = new BinaryReader(new MemoryStream(bytes, false));
        if (!r.ReadBytes(8).SequenceEqual(Magic) || r.ReadInt32() != 1) throw new InvalidDataException("Formato ou versão de cofre não suportado.");
        var id = new Guid(r.ReadBytes(16)); long revision = r.ReadInt64(); var commit = new Guid(r.ReadBytes(16));
        var parent = r.ReadBytes(32); var salt = r.ReadBytes(16);
        var kdf = new KdfParameters(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()); kdf.Validate();
        if (id == Guid.Empty || revision < 1 || commit == Guid.Empty) throw new InvalidDataException("Metadados inválidos.");
        r.BaseStream.Position = PayloadOffset - 4;
        int length = r.ReadInt32();
        if (length < 2 || length != bytes.Length - PayloadOffset - 16) throw new InvalidDataException("Conteúdo incompleto ou inválido.");
        return new(id, revision, commit, parent, salt, kdf);
    }
    public static byte[] Seal(VaultSession session, VaultData data, long revision, byte[]? parentHash = null)
    {
        ObjectDisposedException.ThrowIf(session.IsDisposed, session); data.Validate();
        if (revision < 1 || (parentHash is not null && parentHash.Length != 32)) throw new ArgumentException("Revisão inválida.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            if (plain.Length > MaxFileBytes - PayloadOffset - 16) throw new InvalidDataException("O cofre excedeu o limite de 32 MiB.");
            byte[] output = new byte[PayloadOffset + plain.Length + 16];
            using (var w = new BinaryWriter(new MemoryStream(output, true)))
            {
                w.Write(Magic); w.Write(1); w.Write(session.VaultId.ToByteArray()); w.Write(revision); w.Write(Guid.NewGuid().ToByteArray());
                w.Write(parentHash ?? new byte[32]); w.Write(session.Salt); w.Write(session.Parameters.MemoryKiB); w.Write(session.Parameters.Iterations); w.Write(session.Parameters.Parallelism);
            }
            RandomNumberGenerator.Fill(output.AsSpan(HeaderLength, 12));
            using (var aes = new AesGcm(session.Key, 16))
                aes.Encrypt(output.AsSpan(HeaderLength, 12), dek, output.AsSpan(HeaderLength + 12, 32), output.AsSpan(HeaderLength + 44, 16), output.AsSpan(0, HeaderLength));
            RandomNumberGenerator.Fill(output.AsSpan(EnvelopeLength, 12));
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(PayloadOffset - 4, 4), plain.Length);
            using (var aes = new AesGcm(dek, 16))
                aes.Encrypt(output.AsSpan(EnvelopeLength, 12), plain, output.AsSpan(PayloadOffset, plain.Length), output.AsSpan(PayloadOffset + plain.Length, 16), output.AsSpan(0, PayloadOffset));
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(dek); }
    }
    public static async Task<OpenedVault> OpenAsync(byte[] bytes, string password)
    {
        var h = ReadHeader(bytes);
        var key = await DeriveAsync(password, h.Salt, h.Parameters).ConfigureAwait(false);
        var session = new VaultSession(key, h.Salt, h.Parameters, h.VaultId);
        try { return new(session, Decrypt(bytes, session), h); }
        catch { session.Dispose(); throw; }
    }
    public static VaultData Decrypt(byte[] bytes, VaultSession session)
    {
        ObjectDisposedException.ThrowIf(session.IsDisposed, session);
        var h = ReadHeader(bytes);
        if (h.VaultId != session.VaultId || !h.Salt.SequenceEqual(session.Salt) || h.Parameters != session.Parameters)
            throw new CryptographicException("Este arquivo exige outra sessão ou senha mestra.");
        var dek = new byte[32]; var plain = new byte[bytes.Length - PayloadOffset - 16];
        try
        {
            using (var aes = new AesGcm(session.Key, 16))
                aes.Decrypt(bytes.AsSpan(HeaderLength, 12), bytes.AsSpan(HeaderLength + 12, 32), bytes.AsSpan(HeaderLength + 44, 16), dek, bytes.AsSpan(0, HeaderLength));
            using (var aes = new AesGcm(dek, 16))
                aes.Decrypt(bytes.AsSpan(EnvelopeLength, 12), bytes.AsSpan(PayloadOffset, plain.Length), bytes.AsSpan(PayloadOffset + plain.Length, 16), plain, bytes.AsSpan(0, PayloadOffset));
            var data = JsonSerializer.Deserialize<VaultData>(plain, JsonOptions) ?? throw new InvalidDataException("Conteúdo ausente.");
            data.Validate(); return data;
        }
        finally { CryptographicOperations.ZeroMemory(dek); CryptographicOperations.ZeroMemory(plain); }
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static byte[] ProtectSessionKey(VaultSession session, Func<byte[], byte[]> protect)
    {
        ObjectDisposedException.ThrowIf(session.IsDisposed, session);
        var material = new byte[64];
        session.Key.CopyTo(material, 0); session.VaultId.ToByteArray().CopyTo(material, 32); session.Salt.CopyTo(material, 48);
        try { return protect(material); } finally { CryptographicOperations.ZeroMemory(material); }
    }
    public static OpenedVault OpenWithDeviceMaterial(byte[] bytes, byte[] material)
    {
        var header = ReadHeader(bytes);
        if (material.Length != 64 || new Guid(material.AsSpan(32,16)) != header.VaultId || !CryptographicOperations.FixedTimeEquals(material.AsSpan(48,16),header.Salt))
            throw new CryptographicException("A proteção do dispositivo não corresponde a este cofre.");
        var session = new VaultSession(material.AsSpan(0,32).ToArray(),header.Salt,header.Parameters,header.VaultId);
        try { return new(session,Decrypt(bytes,session),header); } catch { session.Dispose();throw; }
    }
}
