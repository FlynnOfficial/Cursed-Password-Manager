using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CursedPasswordManager.App.ViewModels;
using CursedPasswordManager.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CursedPasswordManager.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly DispatcherTimer _idleTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly System.Diagnostics.Stopwatch _activity=System.Diagnostics.Stopwatch.StartNew();
    private readonly Forms.NotifyIcon _tray;
    private bool _exit;
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel=viewModel;InitializeComponent();DataContext=viewModel;
        var iconStream=Application.GetResourceStream(new Uri("pack://application:,,,/CursedPasswordManager;component/Assets/cursed.ico"))!.Stream;
        using(iconStream) _tray=new Forms.NotifyIcon { Text="Cursed Password Manager",Icon=new System.Drawing.Icon(iconStream),Visible=true };
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir",null,(_,_)=>Restore());menu.Items.Add("Gerador de Senhas",null,(_,_)=>{Restore();viewModel.Page=0;});
        menu.Items.Add("Bloquear cofre",null,(_,_)=>viewModel.Lock());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Sair",null,(_,_)=>ExitApplication());
        _tray.ContextMenuStrip=menu;_tray.DoubleClick+=(_,_)=>Restore();
        menu.Opening+=(_,_)=>
        {
            var surface=((System.Windows.Media.SolidColorBrush)FindResource("SurfaceBrush")).Color;
            var text=((System.Windows.Media.SolidColorBrush)FindResource("TextBrush")).Color;
            menu.BackColor=System.Drawing.Color.FromArgb(surface.R,surface.G,surface.B);
            menu.ForeColor=System.Drawing.Color.FromArgb(text.R,text.G,text.B);
            foreach(Forms.ToolStripItem item in menu.Items){item.BackColor=menu.BackColor;item.ForeColor=menu.ForeColor;}
        };
        InputManager.Current.PreProcessInput+=OnInput;
        _idleTimer.Tick+=(_,_)=>{if(viewModel.IsUnlocked && _activity.Elapsed>=TimeSpan.FromMinutes(viewModel.Settings.AutoLockMinutes))viewModel.Lock();};_idleTimer.Start();
        SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        viewModel.Locked+=()=>{MasterPassword.Clear();ConfirmPassword.Clear();foreach(Window dialog in OwnedWindows.Cast<Window>().ToArray())dialog.Close();};
        Closing+=WindowClosing;StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized && viewModel.Settings.MinimizeToTray)Hide();};
        Loaded+=async(_,_)=>{await viewModel.RunAsync(viewModel.InitializeAsync);MasterPassword.Focus();};
    }
    private void OnInput(object sender,PreProcessInputEventArgs e)
    {
        if(e.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs or MouseWheelEventArgs || e.StagingItem.Input is MouseEventArgs { RoutedEvent: var routed } && routed==Mouse.MouseMoveEvent)
        { if(IsActive || OwnedWindows.Cast<Window>().Any(w=>w.IsActive)) _activity.Restart(); }
    }
    private async void UnlockClicked(object sender,RoutedEventArgs e)=>await Unlock();
    private async Task Unlock()
    {
        string password=MasterPassword.Password,confirmation=ConfirmPassword.Password;MasterPassword.Clear();ConfirmPassword.Clear();
        await ViewModel.UnlockAsync(password,confirmation);password="";confirmation="";_activity.Restart();
    }
    private async void HelloClicked(object sender,RoutedEventArgs e){await ViewModel.HelloUnlockAsync();_activity.Restart();}
    private async void MasterKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;await Unlock();}}
    private void AuditSelected(object sender,SelectionChangedEventArgs e){if(sender is ListBox list && list.SelectedItem is AuditFinding finding)ViewModel.SelectFinding(finding);}
    private void SessionChanged(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock)Dispatcher.BeginInvoke(ViewModel.Lock);}
    private void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode is PowerModes.Suspend or PowerModes.Resume)Dispatcher.BeginInvoke(ViewModel.Lock);}
    private void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(!_exit && ViewModel.Settings.CloseToTray){e.Cancel=true;Hide();return;}
        if(ViewModel.IsBusy){e.Cancel=true;ViewModel.Status="Aguarde a operação de disco terminar antes de sair.";return;}
        _idleTimer.Stop();InputManager.Current.PreProcessInput-=OnInput;SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;
        _tray.Visible=false;_tray.Icon?.Dispose();_tray.Dispose();ViewModel.Dispose();Application.Current.Shutdown();
    }
    public void Restore(){Show();WindowState=WindowState.Normal;Activate();}
    public void ExitApplication(){_exit=true;Close();}
}
