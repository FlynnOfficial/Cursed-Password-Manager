using System.IO;
using System.Windows;
using CursedPasswordManager.App;
using CursedPasswordManager.App.ViewModels;
using CursedPasswordManager.Core;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;
using Microsoft.Win32;

namespace CursedPasswordManager.WindowsTests;

internal static class InteractiveChecks
{
    internal const string FixturePassword="fictional validation master phrase";
    internal static async Task RunAsync(string root)
    {
        string log=Path.Combine(root,"events.txt");
        void Record(string message) { File.AppendAllText(log,$"{DateTimeOffset.UtcNow:O} {message}\n");Console.WriteLine(message); }
        var paths=new AppPaths(true,Path.Combine(root,"Data"));
        var settings=new AppSettings {CloseToTray=false,AutoLockMinutes=120};await paths.SaveAsync(settings);
        using(var session=await VaultCipher.CreateSessionAsync(FixturePassword))
        {
            var data=new VaultData{Credentials=[new(){Service="VALIDAÇÃO — conta fictícia",Account="teste@example.invalid",Password="fictional account password"}]};
            await new ReplicaStore(paths.Replicas(settings)).CommitAsync(VaultCipher.Seal(session,data,1),session);
        }
        var vm=new MainViewModel(paths,settings);
        var window=new MainWindow(vm){Title="VALIDAÇÃO Cursed Password Manager — somente dados fictícios"};Application.Current.MainWindow=window;
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool sessionLockReceived=false, sessionUnlockChecked=false, suspendReceived=false;
        SessionSwitchEventHandler sessionHandler=(_,e)=>
        {
            Application.Current.Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    await Task.Delay(400);
                    if(e.Reason==SessionSwitchReason.SessionLock)
                    {
                        sessionLockReceived=true;
                        if(!vm.IsLocked || vm.Credentials.Count!=0)throw new Exception("Real session lock did not clear the vault.");
                        Record("PASS: REAL_SESSION_LOCK cleared vault and visible credentials");
                    }
                    if(e.Reason==SessionSwitchReason.SessionUnlock && sessionLockReceived && !sessionUnlockChecked)
                    {
                        if(!vm.IsLocked)throw new Exception("Vault automatically unlocked after Windows sign-in.");
                        Record("PASS: REAL_SESSION_UNLOCK kept vault locked");
                        // Fixture-only preparation of the next test; never part of the shipping app.
                        await vm.UnlockAsync(FixturePassword,"");
                        if(!vm.IsUnlocked)throw new Exception("Cannot prepare suspend fixture.");
                        sessionUnlockChecked=true;Record("READY_SUSPEND: fixture reopened; waiting for manual suspend/resume");
                    }
                }
                catch(Exception ex){done.TrySetException(ex);}
            });
        };
        PowerModeChangedEventHandler powerHandler=(_,e)=>
        {
            if(e.Mode==PowerModes.Suspend){suspendReceived=true;Record("OBSERVED_REAL_SUSPEND");}
            if(e.Mode==PowerModes.Resume)
            {
                Application.Current.Dispatcher.BeginInvoke(async()=>
                {
                    try
                    {
                        await Task.Delay(1000);
                        if(!sessionUnlockChecked || !suspendReceived)throw new Exception("Resume observed without the required test sequence.");
                        if(!vm.IsLocked || vm.Credentials.Count!=0)throw new Exception("Vault not locked after real suspend/resume.");
                        Record("PASS: REAL_SUSPEND_RESUME kept vault locked and cleared credentials");
                        File.WriteAllText(Path.Combine(root,"result.txt"),"PASS: 3 real Windows session/power checks.\n");done.TrySetResult();
                    }
                    catch(Exception ex){done.TrySetException(ex);}
                });
            }
        };
        SystemEvents.SessionSwitch+=sessionHandler;SystemEvents.PowerModeChanged+=powerHandler;
        try
        {
            window.Show();await Task.Delay(300);while(vm.IsBusy)await Task.Delay(50);
            await vm.UnlockAsync(FixturePassword,"");
            if(!vm.IsUnlocked)throw new Exception("Cannot unlock interactive fixture.");
            window.Restore();
            Record("READY_LOCK: fixture unlocked; waiting for manual Windows lock/unlock");
            await done.Task.WaitAsync(TimeSpan.FromMinutes(30));
        }
        finally{SystemEvents.SessionSwitch-=sessionHandler;SystemEvents.PowerModeChanged-=powerHandler;window.ExitApplication();}
    }
}
