using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using CursedPasswordManager.App.Services;
using CursedPasswordManager.Core;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;

namespace CursedPasswordManager.WindowsTests;

internal static class ExtendedChecks
{
    internal static async Task RunAsync(string root)
    {
        foreach(string mode in new[]{"hardware","registry"})await Worker(mode,Path.Combine(root,mode));
        using var session=await VaultCipher.CreateSessionAsync(InteractiveChecks.FixturePassword,parameters:new(19456,2,1));
        var data=new VaultData{Credentials=[new(){Service="Fixture",Account="fixture@example.invalid",Password="fictional account password"}]};
        var first=VaultCipher.Seal(session,data,1);var next=VaultCipher.Seal(session,data,2);
        foreach(string phase in new[]{"before","after"})
        {
            string crashRoot=Path.Combine(root,"crash-"+phase);Directory.CreateDirectory(crashRoot);
            string target=Path.Combine(crashRoot,"target.cpmvault");
            await File.WriteAllBytesAsync(target,first);await File.WriteAllBytesAsync(Path.Combine(crashRoot,"next.cpmvault"),next);
            using var process=StartWorker("crash-"+phase,crashRoot);
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try
            {
                var timeout=Stopwatch.StartNew();
                while(!File.Exists(Path.Combine(crashRoot,"ready")))
                {
                    if(process.HasExited)throw new Exception(await stderr);
                    if(timeout.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException("Crash worker did not reach boundary.");
                    await Task.Delay(30);
                }
            }
            finally{if(!process.HasExited)process.Kill();await process.WaitForExitAsync();}
            _=await stdout;_=await stderr;
            var actual=await File.ReadAllBytesAsync(target);
            NativeChecks.Check(actual.SequenceEqual(phase=="before"?first:next),"actual process termination "+phase+" replacement preserves committed bytes");
            NativeChecks.Check(VaultCipher.Decrypt(actual,session).Credentials.Count==1,"terminated writer leaves authenticated vault "+phase);
            if(phase=="after")NativeChecks.Check((await File.ReadAllBytesAsync(target+".prev")).SequenceEqual(first),"previous revision survives completed replacement");
            else
            {
                NativeChecks.Check(Directory.GetFiles(crashRoot,".cpm-*.tmp").Length==1,"interrupted temp file retained separately from committed vault");
                var store=new ReplicaStore([target,Path.Combine(crashRoot,"secondary.cpmvault")]);await store.ScanAsync();
                NativeChecks.Check((await store.CommitAsync(next,session)).Complete,"save recovers after killed writer and orphan temp");
            }
        }
        var user=WindowsIdentity.GetCurrent().User!;
        string aclRoot=Path.Combine(root,"acl");
        // The checkout grants authenticated users Modify, unlike a user profile's
        // FullControl. Give only this newly created fixture the profile baseline.
        var fixture=Directory.CreateDirectory(aclRoot);var baseline=fixture.GetAccessControl();
        baseline.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        fixture.SetAccessControl(baseline);WindowsIntegration.ProtectDirectory(aclRoot);
        var security=new DirectoryInfo(aclRoot).GetAccessControl();
        var rules=security.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        NativeChecks.Check(security.AreAccessRulesProtected && rules.Length==2 && rules.All(r=>r.AccessControlType==AccessControlType.Allow && (r.IdentityReference.Equals(user)||r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)))),"real protected directory ACL restricts access to user and SYSTEM");
        string child=Path.Combine(aclRoot,"child.txt");await File.WriteAllTextAsync(child,"fixture");
        NativeChecks.Check(new FileInfo(child).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r=>r.IsInherited && (r.IdentityReference.Equals(user)||r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)))),"child file inherits restricted ACL");
        foreach(bool internalFailure in new[]{false,true})
        {
            string scenario=Path.Combine(aclRoot,internalFailure?"internal":"secondary");
            var paths=Enumerable.Range(0,3).Select(i=>Path.Combine(scenario,i.ToString(),"passwords.cpmvault")).ToArray();
            var store=new ReplicaStore(paths);await store.CommitAsync(first,session);
            string blocked=Path.GetDirectoryName(paths[internalFailure?0:2])!;
            var directory=new DirectoryInfo(blocked);var original=directory.GetAccessControl();var denied=directory.GetAccessControl();
            denied.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.WriteData|FileSystemRights.AppendData,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
            directory.SetAccessControl(denied);
            try
            {
                bool deniedWrite=false;try{await File.WriteAllTextAsync(Path.Combine(blocked,"probe"),"fixture");}catch(UnauthorizedAccessException){deniedWrite=true;}
                NativeChecks.Check(deniedWrite,"real ACL rejects write "+(internalFailure?"internal":"secondary"));
                if(internalFailure)
                {
                    bool rejected=false;try{await store.CommitAsync(next,session);}catch(IOException){rejected=true;}
                    NativeChecks.Check(rejected,"internal ACL failure prevents commit");
                    foreach(string path in paths)NativeChecks.Check((await File.ReadAllBytesAsync(path)).SequenceEqual(first),"internal ACL failure preserves existing replica");
                }
                else
                {
                    var result=await store.CommitAsync(next,session);
                    NativeChecks.Check(!result.Complete && result.States.Count(s=>s.Status=="Íntegra")==2,"secondary ACL failure reports partial save");
                    NativeChecks.Check((await File.ReadAllBytesAsync(paths[0])).SequenceEqual(next),"secondary ACL failure preserves new internal recovery");
                }
            }
            finally{directory.SetAccessControl(original);}
        }
        var originalPaths=new AppPaths(true,Path.Combine(root,"portable-original"));var settings=new AppSettings();
        await originalPaths.SaveAsync(settings);await new ReplicaStore(originalPaths.Replicas(settings)).CommitAsync(first,session);
        File.WriteAllText(Path.Combine(originalPaths.Root,"creator-sid.txt"),user.Value);
        string relocated=Path.Combine(root,"portable-relocated");Directory.CreateDirectory(relocated);
        foreach(string source in Directory.EnumerateFiles(originalPaths.Root,"*",SearchOption.AllDirectories))
        {
            string destination=Path.Combine(relocated,Path.GetRelativePath(originalPaths.Root,source));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination);
        }
        await Worker("portable",relocated);
        File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS: extended Windows checks. {DateTimeOffset.UtcNow:O}\n");
    }

    private static Process StartWorker(string mode,string root)
    {
        Directory.CreateDirectory(root);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--worker="+mode);start.ArgumentList.Add(root);
        return Process.Start(start)??throw new IOException("Cannot start worker.");
    }
    private static async Task Worker(string mode,string root)
    {
        using var process=StartWorker(mode,root);
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));}
        catch{if(!process.HasExited)process.Kill();throw;}
        string output=await stdout,error=await stderr;Console.Write(output);
        File.WriteAllText(Path.Combine(root,"stdout.txt"),output);File.WriteAllText(Path.Combine(root,"stderr.txt"),error);
        if(process.ExitCode!=0)throw new Exception($"Worker {mode} failed: {error}");
    }
}
