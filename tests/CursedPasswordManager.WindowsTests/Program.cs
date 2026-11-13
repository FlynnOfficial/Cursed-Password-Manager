using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CursedPasswordManager.App;
using CursedPasswordManager.App.Services;
using CursedPasswordManager.App.ViewModels;
using CursedPasswordManager.Core;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;

namespace CursedPasswordManager.WindowsTests;

internal static class Program
{
    private static int _checks;
    private static string _root="";
    [STAThread]
    private static int Main(string[] args)
    {
        _root=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--",StringComparison.Ordinal))??Path.Combine(Path.GetTempPath(),"CpmWindowsTests-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_root);
        var worker=args.FirstOrDefault(a=>a.StartsWith("--worker=",StringComparison.Ordinal));
        if(worker is not null) return NativeChecks.Run(worker[9..],_root);
        var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CursedPasswordManager;component/Themes/XpClassic.xaml",UriKind.Relative)});
        application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CursedPasswordManager;component/Themes/Controls.xaml",UriKind.Relative)});
        application.Resources.Add("BoolVisibility",new BooleanToVisibilityConverter());
        int result=1;
        application.Dispatcher.BeginInvoke(async()=>
        {
            MainWindow? window=null;
            try
            {
                if (args.Contains("--extended"))
                {
                    await ExtendedChecks.RunAsync(_root);
                    result=0;return;
                }
                if (args.Contains("--interactive"))
                {
                    await InteractiveChecks.RunAsync(_root);
                    result=0;return;
                }
                using(var clipboard=new SecureClipboard())
                {
                    bool rejected=false;try{clipboard.Copy("fixture\0tail",1);}catch(ArgumentException){rejected=true;}
                    Check(rejected,"NUL clipboard input rejected before native clipboard access");
                }
                var paths=new AppPaths(true,Path.Combine(_root,"Data"));var settings=new AppSettings{CloseToTray=false,AutoLockMinutes=1};
                var vm=new MainViewModel(paths,settings);window=new MainWindow(vm){ShowActivated=false};application.MainWindow=window;
                window.Show();await WaitIdle(vm);
                Check(vm.IsFirstRun,"first-run wizard");
                await vm.UnlockAsync("fictional ui master phrase","fictional ui master phrase");Check(vm.IsUnlocked,"create vault through viewmodel");
                Check(vm.ReplicaStates.Count==3 && vm.ReplicaStates.All(s=>s.Status=="Íntegra"),"three committed copies");
                vm.GenerateCommand.Execute(null);await WaitIdle(vm);Check(vm.GeneratedDisplay.StartsWith('•'),"generated password hidden");
                vm.RevealGeneratedCommand.Execute(null);Check(vm.GeneratedDisplay.Length==30 && vm.GeneratedDisplay.All(char.IsAsciiDigit),"numeric generator UI command");
                // Drive modal controls inside the isolated WPF test process; no real credentials.
                ScheduleDialog(w=>
                {
                    var boxes=Descendants<TextBox>(w).ToArray();boxes[0].Text="Conta de demonstração";boxes[1].Text="jogador@example.invalid";
                    Click(w,"Salvar credencial");
                });
                vm.SaveGeneratedCommand.Execute(null);await WaitIdle(vm);Check(vm.Credentials.Count==1,"save generated credential through dialog");
                vm.Selected=vm.Credentials[0];vm.FavoriteCommand.Execute(null);await WaitIdle(vm);Check(vm.Credentials[0].Favorite,"favorite command");
                vm.Query="DEMONSTRAÇÃO";Check(vm.Credentials.Count==1,"instant case-insensitive search");vm.Query="absent";Check(vm.EmptyVault,"empty search state");vm.Query="";
                vm.Selected=vm.Credentials[0];
                ScheduleDialog(w=>{Descendants<PasswordBox>(w).Single().Password="new fictional credential 123!";Click(w,"Salvar credencial");});
                vm.EditCommand.Execute(null);await WaitIdle(vm);Check(vm.Credentials[0].History.Count==1,"edit preserves password history");
                vm.AuditCommand.Execute(null);await WaitIdle(vm);Check(vm.AuditSummary.Contains("1 contas"),"offline audit command");
                vm.Page=0;vm.GenerateCommand.Execute(null);await WaitIdle(vm);
                foreach(var theme in new[]{"XpClassic","CursedXp","CursedModern"})
                {
                    (theme switch{"XpClassic"=>vm.ClassicCommand,"CursedXp"=>vm.CursedXpCommand,_=>vm.ModernCommand}).Execute(null);await WaitIdle(vm);
                    Check((await paths.LoadAsync()).Theme==theme,"persist theme "+theme);
                    await Capture(window,Path.Combine(_root,theme+".png"));
                }
                vm.Page=1;await Capture(window,Path.Combine(_root,"credentials.png"));
                vm.Page=2;await Capture(window,Path.Combine(_root,"settings.png"));
                vm.Lock();Check(vm.IsLocked && vm.Credentials.Count==0 && !vm.GeneratedDisplay.StartsWith('•'),"lock clears visible secrets");
                await Capture(window,Path.Combine(_root,"locked.png"));
                await vm.UnlockAsync("fictional ui master phrase","");Check(vm.IsUnlocked && vm.Credentials.Count==1,"unlock persisted vault");
                Check(vm.Selected is null,"no secret automatically revealed after unlock");
                Check(!HelloService.IsEnrolled(paths),"Hello disabled by default in portable mode");
                try{await HelloService.OpenAsync(paths,[]);throw new Exception("Portable Hello unexpectedly allowed");}catch(InvalidOperationException){Check(true,"portable Hello fails closed");}
                vm.VerifyCommand.Execute(null);await WaitIdle(vm);Check(vm.ReplicaStates.All(s=>s.Status=="Íntegra"),"UI integrity verification");
                vm.Lock();var pendingUnlock=vm.UnlockAsync("fictional ui master phrase","");vm.Lock();await pendingUnlock;
                Check(vm.IsLocked && vm.Credentials.Count==0,"lock invalidates in-flight authentication");
                await vm.UnlockAsync("fictional ui master phrase","");
                Check(vm.IsUnlocked,"master unlock after canceled authentication");
                var powerHandler=typeof(MainWindow).GetMethod("PowerChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                foreach(var mode in new[]{Microsoft.Win32.PowerModes.Suspend,Microsoft.Win32.PowerModes.Resume})
                {
                    powerHandler.Invoke(window,[null,new Microsoft.Win32.PowerModeChangedEventArgs(mode)]);
                    await Task.Delay(100);
                    Check(vm.IsLocked && vm.Credentials.Count==0,"injected power callback clears vault: "+mode);
                    await vm.UnlockAsync("fictional ui master phrase","");
                }
                Check(((System.Windows.Media.SolidColorBrush)window.Background).Color==((System.Windows.Media.SolidColorBrush)application.FindResource("BackgroundBrush")).Color,"window background follows theme");
                window.WindowState=WindowState.Minimized;await Task.Delay(300);Check(!window.IsVisible,"minimize to tray");window.Restore();await Task.Delay(200);Check(window.IsVisible,"restore from tray");
                // Prevent unrelated human input into this fixture from restarting its idle clock.
                window.Hide();
                Console.WriteLine("Waiting 62 seconds to verify actual idle timer with fixture hidden...");
                await Task.Delay(TimeSpan.FromSeconds(62));Check(vm.IsLocked,"automatic lock after inactivity");
                File.WriteAllText(Path.Combine(_root,"result.txt"),$"PASS: {_checks} Windows integration checks. {DateTimeOffset.UtcNow:O}\n");
                Console.WriteLine($"PASS: {_checks} Windows integration checks.");result=0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);File.WriteAllText(Path.Combine(_root,"failure.txt"),ex.ToString());}
            finally{if(window is not null)window.ExitApplication();application.Shutdown(result);}
        });
        application.Run();return result;
    }
    private static void Check(bool condition,string label){if(!condition)throw new Exception("FAILED: "+label);_checks++;Console.WriteLine("PASS: "+label);}
    private static async Task WaitIdle(MainViewModel vm)
    {await Task.Delay(100);var limit=DateTime.UtcNow.AddSeconds(30);while(vm.IsBusy){if(DateTime.UtcNow>limit)throw new TimeoutException("UI operation timed out");await Task.Delay(50);}await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Click(Window window,string label)=>Descendants<Button>(window).Single(b=>b.Content as string==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void ScheduleDialog(Action<Window> action)
    {
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};int attempts=0;
        timer.Tick+=(_,_)=>{var modal=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w!=Application.Current.MainWindow && w.IsVisible);if(modal is null){if(++attempts>100){timer.Stop();throw new TimeoutException("Modal did not appear");}return;}timer.Stop();action(modal);};timer.Start();
    }
    private static async Task Capture(Window window,string path)
    {
        await Application.Current.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Render);await Task.Delay(150);
        var content=(FrameworkElement)window.Content;
        var image=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
    }
}
