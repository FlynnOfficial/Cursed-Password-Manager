using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CursedPasswordManager.App.Services;

public static class ThemeService
{
    public static void Apply(string name)
    {
        if (name is not ("XpClassic" or "CursedXp" or "CursedModern")) throw new ArgumentException("Tema inválido.");
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/CursedPasswordManager;component/Themes/{name}.xaml", UriKind.Relative) };
    }
}
public static class WindowsIntegration
{
    public static void ProtectDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Usuário Windows não identificado.");
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
    public static void SetStartup(bool enabled, bool portable)
    {
        if (portable) { if (enabled) throw new InvalidOperationException("Inicialização automática não está disponível no modo portátil."); return; }
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("CursedPasswordManager", $"\"{Environment.ProcessPath}\" --minimized");
        else key.DeleteValue("CursedPasswordManager", false);
    }
}

public sealed class SecureClipboard : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private byte[]? _digest;
    private uint _sequence;
    private DateTimeOffset _expires;
    public SecureClipboard() { _timer.Tick += (_, _) => { if (DateTimeOffset.UtcNow >= _expires) ClearOwned(); }; }
    public void Copy(string value, int seconds)
    {
        // CF_UNICODETEXT is NUL-terminated; accepting embedded NUL would copy only
        // a prefix and make the ownership digest disagree with the native content.
        if (value.Contains('\0')) throw new ArgumentException("Não é possível copiar texto que contém o caractere NUL. Edite o valor para removê-lo.", nameof(value));
        var data = new DataObject(); data.SetText(value, TextDataFormat.UnicodeText);
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        Clipboard.SetDataObject(data, true);
        if (_digest is not null) CryptographicOperations.ZeroMemory(_digest);
        var bytes = Encoding.Unicode.GetBytes(value);
        try { _digest = SHA256.HashData(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
        _sequence = GetClipboardSequenceNumber(); _expires = DateTimeOffset.UtcNow.AddSeconds(seconds); _timer.Start();
    }
    public void ClearOwned()
    {
        if (_digest is null) { _timer.Stop(); return; }
        if (!OpenClipboard(IntPtr.Zero)) return; // Retry on the next tick if another application holds it.
        bool done = false;
        try
        {
            if (GetClipboardSequenceNumber() != _sequence) { done = true; return; }
            IntPtr handle = GetClipboardData(13);
            if (handle == IntPtr.Zero) { done = true; return; }
            ulong size = GlobalSize(handle).ToUInt64();
            if (size > 1048576) { done = true; return; }
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return;
            byte[] bytes = new byte[(int)size];
            bool matches = false;
            try
            {
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                int length = 0;
                while (length + 1 < bytes.Length && (bytes[length] != 0 || bytes[length + 1] != 0)) length += 2;
                var hash = SHA256.HashData(bytes.AsSpan(0, length));
                try { matches = CryptographicOperations.FixedTimeEquals(hash, _digest); }
                finally { CryptographicOperations.ZeroMemory(hash); }
            }
            finally { CryptographicOperations.ZeroMemory(bytes); GlobalUnlock(handle); }
            done = !matches || EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
            if (done) { CryptographicOperations.ZeroMemory(_digest); _digest = null; _timer.Stop(); }
        }
    }
    public void Dispose() { ClearOwned(); _timer.Stop(); if (_digest is not null) CryptographicOperations.ZeroMemory(_digest); }
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
}
