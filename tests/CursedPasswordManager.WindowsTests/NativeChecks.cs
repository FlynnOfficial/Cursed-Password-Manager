using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CursedPasswordManager.App.Services;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;
using Microsoft.Win32;

namespace CursedPasswordManager.WindowsTests;

internal static class NativeChecks
{
    internal static void Check(bool condition,string name)
    { if(!condition)throw new InvalidOperationException(name);Console.WriteLine("PASS: "+name); }

    internal static int Run(string mode,string root)
    {
        try
        {
            switch(mode)
            {
                case "clipboard": return ClipboardChecks(true);
                case "clipboard-session": return ClipboardChecks(false);
                case "registry": RegistryChecks();break;
                case "hardware": HardwareChecks();break;
                case "portable": case "portable-other-user":
                    if(mode=="portable-other-user")
                        Check(File.ReadAllText(Path.Combine(root,"creator-sid.txt"))!=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,"portable vault opened under a different Windows SID");
                    var paths=new AppPaths(true,root);
                    var settings=paths.LoadAsync().GetAwaiter().GetResult();
                    var scan=new ReplicaStore(paths.Replicas(settings)).ScanAsync().GetAwaiter().GetResult();
                    Check(scan.Candidates.Count==3,"relocated portable copies found in fresh process");
                    foreach(var candidate in scan.Candidates)
                    {
                        var opened=VaultCipher.OpenAsync(candidate.Bytes,InteractiveChecks.FixturePassword).GetAwaiter().GetResult();
                        using(opened.Session)Check(opened.Data.Credentials.Single().Account=="fixture@example.invalid","relocated portable copy authenticated");
                    }
                    break;
                case "crash-before": case "crash-after":
                    var bytes=File.ReadAllBytes(Path.Combine(root,"next.cpmvault"));
                    void Ready(){File.WriteAllText(Path.Combine(root,"ready"),mode);Thread.Sleep(Timeout.Infinite);}
                    AtomicFile.WriteAsync(Path.Combine(root,"target.cpmvault"),bytes,mode=="crash-before"?Ready:null).GetAwaiter().GetResult();
                    Ready();break;
                default: throw new ArgumentException("Unknown worker.");
            }
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }

    private static int ClipboardChecks(bool isolated)
    {
        // A window station owns its clipboard. Never access the interactive user's clipboard.
        // The explicit clipboard-session mode requires the user's permission to replace clipboard contents.
        if(isolated)
        {
            var station=CreateWindowStation("CpmValidation"+Guid.NewGuid().ToString("N"),0,0x3f,IntPtr.Zero);
            if(station==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Create isolated window station");
            if(!SetProcessWindowStation(station))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Attach isolated window station");
            var desktop=CreateDesktop("Validation",null,IntPtr.Zero,0,0x1ff,IntPtr.Zero);
            if(desktop==IntPtr.Zero || !SetThreadDesktop(desktop))throw new System.ComponentModel.Win32Exception();
        }
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
        app.Dispatcher.BeginInvoke(async()=>
        {
            try
            {
                using var clipboard=new SecureClipboard();
                const string value="fictional clipboard secret á🔑";
                clipboard.Copy(value,1);Check(Clipboard.GetText()==value,"native Unicode clipboard roundtrip");
                var data=Clipboard.GetDataObject()??throw new InvalidOperationException("Clipboard data unavailable.");
                foreach(string format in new[]{"CanIncludeInClipboardHistory","CanUploadToCloudClipboard","ExcludeClipboardContentFromMonitorProcessing"})
                {
                    Check(data.GetDataPresent(format),"clipboard exclusion format "+format);
                    Check(data.GetData(format) is MemoryStream stream && stream.ToArray().Take(4).SequenceEqual(new byte[4]),"clipboard exclusion value "+format);
                }
                await Task.Delay(2300);Check(!Clipboard.ContainsText(),"actual dispatcher timer clears owned clipboard");
                clipboard.Copy(value,1);Clipboard.SetText("fictional unrelated content");await Task.Delay(2300);
                Check(Clipboard.GetText()=="fictional unrelated content","later unrelated clipboard preserved");
                clipboard.Copy(value,1);Clipboard.SetText(value);await Task.Delay(2300);
                Check(Clipboard.GetText()==value,"same text recopied by another owner preserved");
                clipboard.Copy(value,30);clipboard.ClearOwned();Check(!Clipboard.ContainsText(),"explicit owned clipboard cleanup");
                using(var disposed=new SecureClipboard()){disposed.Copy(value,30);}
                Check(!Clipboard.ContainsText(),"dispose clears owned clipboard");
                clipboard.Copy(value,1);
                using var held=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Exception? threadFailure=null;
                var thread=new Thread(()=>{try{if(!OpenClipboard(IntPtr.Zero))throw new System.ComponentModel.Win32Exception();held.Set();release.Wait();CloseClipboard();}catch(Exception ex){threadFailure=ex;held.Set();}});
                thread.Start();held.Wait();
                try{if(threadFailure is not null)throw threadFailure;clipboard.ClearOwned();}
                finally{release.Set();thread.Join();}
                await Task.Delay(2300);Check(!Clipboard.ContainsText(),"clipboard contention retried after release");
                bool rejected=false;
                try{clipboard.Copy("fictional\0tail",1);}catch(ArgumentException){rejected=true;}
                if(!rejected){await Task.Delay(2300);Console.WriteLine("NUL diagnostic: text remains after expiry="+Clipboard.ContainsText());}
                Check(rejected,"embedded NUL rejected before clipboard truncation");
                Console.WriteLine("PASS: clipboard suite ("+(isolated?"isolated station":"explicit session mode")+")");result=0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);}
            finally{Clipboard.Clear();app.Shutdown(result);}
        });
        app.Run();return result; // Station and desktop handles close with this isolated process.
    }

    private static void RegistryChecks()
    {
        string name=@"Software\CursedPasswordManager.Validation\"+Guid.NewGuid().ToString("N");
        using var sandbox=Registry.CurrentUser.CreateSubKey(name);
        var hkcu=new IntPtr(unchecked((int)0x80000001));
        if(RegOverridePredefKey(hkcu,sandbox.Handle.DangerousGetHandle())!=0)throw new IOException("Cannot isolate HKCU.");
        try
        {
            WindowsIntegration.SetStartup(true,false);
            using var run=sandbox.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            Check((string?)run?.GetValue("CursedPasswordManager")==$"\"{Environment.ProcessPath}\" --minimized","startup registration uses quoted executable and minimized argument");
            bool rejected=false;try{WindowsIntegration.SetStartup(true,true);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"portable startup enable rejected");
            WindowsIntegration.SetStartup(false,true);
            Check(run?.GetValue("CursedPasswordManager") is string,"portable startup disable leaves normal registration alone");
            WindowsIntegration.SetStartup(false,false);
            Check(run?.GetValue("CursedPasswordManager") is null,"startup registration removed");
        }
        finally
        {
            if(RegOverridePredefKey(hkcu,IntPtr.Zero)!=0)throw new IOException("Cannot restore HKCU mapping.");
            Registry.CurrentUser.DeleteSubKeyTree(name,false);
        }
        Console.WriteLine("PASS: process-local HKCU isolation restored and fixture removed");
    }

    private static void HardwareChecks()
    {
        uint status=Tbsi_GetDeviceInfo(16,out var info);
        Console.WriteLine($"TPM status=0x{status:x8}; version={info.Version}; interface={info.Interface}");
        foreach(string name in new[]{"Microsoft Passport Key Storage Provider","Microsoft Platform Crypto Provider"})
        {
            int result=NCryptOpenStorageProvider(out var provider,name,0);
            Console.WriteLine($"Provider {name}: 0x{result:x8}");
            if(result==0){try{Console.WriteLine($"RSA support: 0x{NCryptIsAlgSupported(provider,"RSA",0):x8}");}finally{NCryptFreeObject(provider);}}
        }
    }

    [StructLayout(LayoutKind.Sequential)]private struct TpmInfo{public uint StructureVersion,Version,Interface,Revision;}
    [DllImport("tbs.dll")]private static extern uint Tbsi_GetDeviceInfo(uint size,out TpmInfo info);
    [DllImport("ncrypt.dll",CharSet=CharSet.Unicode)]private static extern int NCryptOpenStorageProvider(out IntPtr provider,string name,int flags);
    [DllImport("ncrypt.dll",CharSet=CharSet.Unicode)]private static extern int NCryptIsAlgSupported(IntPtr provider,string algorithm,int flags);
    [DllImport("ncrypt.dll")]private static extern int NCryptFreeObject(IntPtr value);
    [DllImport("advapi32.dll")]private static extern int RegOverridePredefKey(IntPtr key,IntPtr replacement);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWindowStation(string name,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetProcessWindowStation(IntPtr station);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateDesktop(string name,string? device,IntPtr mode,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
}
