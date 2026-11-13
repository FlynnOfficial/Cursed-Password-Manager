using System.Security.Cryptography;
using System.Text;
using CursedPasswordManager.Core;
using CursedPasswordManager.Security;
using Konscious.Security.Cryptography;

namespace CursedPasswordManager.Tests;

public sealed class CipherTests
{
    internal const string Password="fictional master phrase 123!";
    internal static readonly KdfParameters TestKdf=new(19456,2,1);
    internal static Task<VaultSession> Session()=>VaultCipher.CreateSessionAsync(Password,parameters:TestKdf);
    internal static VaultData Data()=>new(){Credentials=[new(){Service="Fictional Service",Account="fiction@example.invalid",Password="Private test password!",Favorite=true,Category="Jogos",History=[new(){Password="old fictional password"}]}]};
    [Fact]public async Task Argon2idMatchesRfc9106Vector()
    {
        using var kdf=new Argon2id(Enumerable.Repeat((byte)1,32).ToArray()){Salt=Enumerable.Repeat((byte)2,16).ToArray(),KnownSecret=Enumerable.Repeat((byte)3,8).ToArray(),AssociatedData=Enumerable.Repeat((byte)4,12).ToArray(),MemorySize=32,Iterations=3,DegreeOfParallelism=4};
        Assert.Equal("0D640DF58D78766C08C037A34A8B53C9D01EF0452D75B65EB52520E96B01E659",Convert.ToHexString(await kdf.GetBytesAsync(32)));
    }
    [Fact]public async Task RoundTripPreservesAllSecretFields()
    {
        using var session=await Session();var source=Data();var bytes=VaultCipher.Seal(session,source,1);
        var opened=await VaultCipher.OpenAsync(bytes,Password);using(opened.Session){Assert.Equal(source.Credentials[0].Password,opened.Data.Credentials[0].Password);Assert.Equal(source.Credentials[0].History[0].Password,opened.Data.Credentials[0].History[0].Password);Assert.True(opened.Data.Credentials[0].Favorite);Assert.Equal(session.VaultId,opened.Header.VaultId);}
        Assert.DoesNotContain("Fictional",Encoding.UTF8.GetString(bytes));Assert.DoesNotContain("example.invalid",Encoding.UTF8.GetString(bytes));
    }
    [Fact]public async Task WrongPasswordIsRejected()
    {using var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);await Assert.ThrowsAnyAsync<CryptographicException>(()=>VaultCipher.OpenAsync(bytes,"wrong password"));}
    [Theory][InlineData(12)][InlineData(30)][InlineData(40)][InlineData(60)][InlineData(90)][InlineData(112)][InlineData(130)][InlineData(160)][InlineData(175)][InlineData(200)]
    public async Task TamperIsRejected(int offset)
    {using var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);bytes[offset]^=0x01;Assert.ThrowsAny<CryptographicException>(()=>VaultCipher.Decrypt(bytes,s));}
    [Fact]public async Task HeaderBoundsRejectBeforeKdf()
    {using var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(100),int.MaxValue);Assert.Throws<InvalidDataException>(()=>VaultCipher.ReadHeader(bytes));}
    [Fact]public async Task TruncationAndTrailingBytesRejected()
    {using var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);Assert.Throws<InvalidDataException>(()=>VaultCipher.ReadHeader(bytes[..^1]));Assert.Throws<InvalidDataException>(()=>VaultCipher.ReadHeader([..bytes,0]));}
    [Fact]public async Task NewSaveUsesFreshEnvelopeAndNonce()
    {using var s=await Session();var a=VaultCipher.Seal(s,Data(),1);var b=VaultCipher.Seal(s,Data(),1);Assert.NotEqual(a,b);Assert.False(a.AsSpan(112,12).SequenceEqual(b.AsSpan(112,12)));Assert.False(a.AsSpan(172,12).SequenceEqual(b.AsSpan(172,12)));}
    [Fact]public async Task MasterChangeRequiresNewPasswordAndPreservesOldBackup()
    {
        using var old=await Session();var a=VaultCipher.Seal(old,Data(),1);using var changed=await VaultCipher.CreateSessionAsync("new fictional master password",old.VaultId,TestKdf);
        var b=VaultCipher.Seal(changed,Data(),2,SHA256.HashData(a));await Assert.ThrowsAnyAsync<CryptographicException>(()=>VaultCipher.OpenAsync(b,Password));
        var opened=await VaultCipher.OpenAsync(b,"new fictional master password");opened.Session.Dispose();Assert.NotEmpty(VaultCipher.Decrypt(a,old).Credentials);
    }
    [Fact]public async Task DisposedSessionCannotDecryptOrSeal()
    {var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);s.Dispose();Assert.Throws<ObjectDisposedException>(()=>VaultCipher.Decrypt(bytes,s));Assert.Throws<ObjectDisposedException>(()=>VaultCipher.Seal(s,Data(),2));}
    [Fact]public async Task InvalidPayloadNeverSealed()
    {using var s=await Session();var data=Data();data.Credentials.Add(data.Credentials[0]);Assert.Throws<InvalidDataException>(()=>VaultCipher.Seal(s,data,1));}
    [Fact]public async Task DeviceMaterialIsBoundToVaultAndSalt()
    {
        using var s=await Session();var bytes=VaultCipher.Seal(s,Data(),1);byte[]? material=null;
        VaultCipher.ProtectSessionKey(s,m=>{material=m.ToArray();return [];});
        var opened=VaultCipher.OpenWithDeviceMaterial(bytes,material!);opened.Session.Dispose();material![48]^=1;Assert.Throws<CryptographicException>(()=>VaultCipher.OpenWithDeviceMaterial(bytes,material));CryptographicOperations.ZeroMemory(material);
    }
}
