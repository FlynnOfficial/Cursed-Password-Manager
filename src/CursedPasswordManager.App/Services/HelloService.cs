using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;
using Microsoft.Win32.SafeHandles;

namespace CursedPasswordManager.App.Services;

public static class HelloService
{
    private static readonly CngProvider Provider = new("Microsoft Passport Key Storage Provider");
    private sealed record Enrollment(Guid VaultId, string KeySuffix, byte[] Ciphertext);
    private static string FilePath(AppPaths paths)=>Path.Combine(paths.Root,"device-unlock.json");
    private static string KeyName(string suffix)=>$"{WindowsIdentity.GetCurrent().User!.Value}//CursedPasswordManager//{suffix}";
    public static bool IsEnrolled(AppPaths paths)=>!paths.Portable && File.Exists(FilePath(paths));
    private static void CheckMode(AppPaths paths) { if(paths.Portable)throw new InvalidOperationException("Windows Hello está desabilitado no modo portátil."); }
    private static CngProperty Integer(string name,int value)=>new(name,BitConverter.GetBytes(value),CngPropertyOptions.None);
    private static void ValidateKey(CngKey key)
    {
        int cache=BitConverter.ToInt32(key.GetProperty("NgcCacheType",CngPropertyOptions.None).GetValue()!);
        int implementation=BitConverter.ToInt32(key.GetProperty("NgcKeyImplType",CngPropertyOptions.None).GetValue()!);
        if(cache!=1 || implementation!=1 || key.ExportPolicy!=CngExportPolicies.None || key.KeyUsage!=CngKeyUsages.Decryption || key.KeySize<2048)
            throw new CryptographicException("O provedor não oferece a política de proteção exigida.");
    }
    private static void Parent(CngKey key,IntPtr window)
    {
        key.SetProperty(new("HWND Handle",BitConverter.GetBytes(window.ToInt64()),CngPropertyOptions.None));
        key.SetProperty(new("Use Context",System.Text.Encoding.Unicode.GetBytes("Desbloquear Cursed Password Manager\0"),CngPropertyOptions.None));
    }
    public static async Task EnrollAsync(AppPaths paths,VaultSession session)
    {
        CheckMode(paths); var window=new WindowInteropHelper(Application.Current.MainWindow).Handle;
        string suffix=Guid.NewGuid().ToString("N");
        try
        {
            var enrollment=await Task.Run(()=>
            {
                var options=new CngKeyCreationParameters { Provider=Provider, ExportPolicy=CngExportPolicies.None, KeyUsage=CngKeyUsages.Decryption, ParentWindowHandle=window };
                options.Parameters.Add(Integer("Length",2048)); options.Parameters.Add(Integer("NgcCacheType",1));
                using var key=CngKey.Create(CngAlgorithm.Rsa,KeyName(suffix),options);
                try
                {
                    ValidateKey(key); Parent(key,window);
                    using var rsa=new RSACng(key);
                    var encrypted=VaultCipher.ProtectSessionKey(session,plain=>rsa.Encrypt(plain,RSAEncryptionPadding.OaepSHA256));
                    RequireSilentRefusal(key,encrypted);
                    key.SetProperty(Integer("PinCacheIsGestureRequired",1));
                    var roundTrip=rsa.Decrypt(encrypted,RSAEncryptionPadding.OaepSHA256);
                    try
                    {
                        bool valid=false;
                        VaultCipher.ProtectSessionKey(session,plain=>{valid=CryptographicOperations.FixedTimeEquals(plain,roundTrip);return [];});
                        if(!valid)throw new CryptographicException();
                    }
                    finally {CryptographicOperations.ZeroMemory(roundTrip);}
                    RequireSilentRefusal(key,encrypted);
                    return new Enrollment(session.VaultId,suffix,encrypted);
                }
                catch { key.Delete();throw; }
            });
            try { await AtomicFile.WriteAsync(FilePath(paths),JsonSerializer.SerializeToUtf8Bytes(enrollment)); }
            catch { using var key=CngKey.Open(KeyName(suffix),Provider);key.Delete();throw; }
        }
        catch(CryptographicException)
        { throw new InvalidOperationException("Windows Hello indisponível ou incompatível com a política exigida: TPM, chave não exportável, RSA-OAEP e autenticação obrigatória em cada uso. Nada foi ativado. Continue usando a senha mestra."); }
    }
    public static async Task<OpenedVault> OpenAsync(AppPaths paths,byte[] vault)
    {
        CheckMode(paths);var record=await ReadAsync(paths);
        if(record.VaultId!=VaultCipher.ReadHeader(vault).VaultId)throw new CryptographicException();
        var window=new WindowInteropHelper(Application.Current.MainWindow).Handle;
        return await Task.Run(()=>
        {
            using var key=CngKey.Open(KeyName(record.KeySuffix),Provider);ValidateKey(key);Parent(key,window);
            RequireSilentRefusal(key,record.Ciphertext);
            key.SetProperty(Integer("PinCacheIsGestureRequired",1));
            using var rsa=new RSACng(key);var plain=rsa.Decrypt(record.Ciphertext,RSAEncryptionPadding.OaepSHA256);
            try { RequireSilentRefusal(key,record.Ciphertext);return VaultCipher.OpenWithDeviceMaterial(vault,plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        });
    }
    public static async Task DisableAsync(AppPaths paths)
    {
        if(!IsEnrolled(paths))return;
        var record=await ReadAsync(paths);
        if(CngKey.Exists(KeyName(record.KeySuffix),Provider)) { using var key=CngKey.Open(KeyName(record.KeySuffix),Provider);key.Delete(); }
        File.Delete(FilePath(paths)); if(File.Exists(FilePath(paths)+".prev"))File.Delete(FilePath(paths)+".prev");
    }
    private static async Task<Enrollment> ReadAsync(AppPaths paths)
    {
        var record=JsonSerializer.Deserialize<Enrollment>(await AtomicFile.ReadBoundedAsync(FilePath(paths),8192))??throw new InvalidDataException("Configuração Windows Hello inválida.");
        if(!Guid.TryParseExact(record.KeySuffix,"N",out _) || record.Ciphertext is null || record.Ciphertext.Length!=256)throw new InvalidDataException("Configuração Windows Hello inválida.");
        return record;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct OaepInfo { [MarshalAs(UnmanagedType.LPWStr)] public string Algorithm; public IntPtr Label; public int LabelLength; }
    private static void RequireSilentRefusal(CngKey key,byte[] encrypted)
    {
        var info=new OaepInfo {Algorithm="SHA256"};var output=new byte[256];
        try
        {
            int status=NCryptDecrypt(key.Handle,encrypted,encrypted.Length,ref info,output,output.Length,out _,4|0x40);
            if(status!=unchecked((int)0x80090022))throw new CryptographicException("O provedor não comprovou a exigência de autenticação interativa.");
        }
        finally { CryptographicOperations.ZeroMemory(output); }
    }
    [DllImport("ncrypt.dll",CharSet=CharSet.Unicode)]
    private static extern int NCryptDecrypt(SafeNCryptKeyHandle key,byte[] input,int inputLength,ref OaepInfo padding,byte[] output,int outputLength,out int written,int flags);
}
