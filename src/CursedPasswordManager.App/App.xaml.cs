using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CursedPasswordManager.App.Services;
using CursedPasswordManager.App.ViewModels;
using CursedPasswordManager.Storage;

namespace CursedPasswordManager.App;

public partial class App : Application
{
    private Mutex? _instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths=new AppPaths(e.Args.Contains("--portable") || File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.flag")));
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())));
        _instance=new Mutex(true,@"Local\CursedPasswordManager-"+id,out bool created);
        if(!created){MessageBox.Show("O Cursed Password Manager já está em execução. Abra-o pelo ícone próximo ao relógio.","Cursed Password Manager");Shutdown();return;}
        try
        {
            if(!paths.Portable)WindowsIntegration.ProtectDirectory(paths.Root);else Directory.CreateDirectory(paths.Root);
            if(!paths.Portable)WindowsIntegration.ProtectDirectory(paths.Recovery);else Directory.CreateDirectory(paths.Recovery);
            AppSettings settings;string? warning=null;
            try{settings=await paths.LoadAsync();}
            catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {settings=new();warning="Não foi possível ler as configurações. Os valores padrão foram carregados; a recuperação interna continua disponível. O arquivo original foi preservado.";}
            ThemeService.Apply(settings.Theme);
            var vm=new MainViewModel(paths,settings);var window=new MainWindow(vm);MainWindow=window;window.Show();
            if(warning is not null)Dialogs.Message("Configurações preservadas",warning);
            if(settings.StartMinimized || e.Args.Contains("--minimized")){window.WindowState=WindowState.Minimized;if(settings.MinimizeToTray)window.Hide();}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {MessageBox.Show("Não foi possível inicializar o aplicativo. Verifique as permissões da pasta de dados.\n"+ex.Message,"Cursed Password Manager");Shutdown();}
    }
    protected override void OnExit(ExitEventArgs e){_instance?.Dispose();base.OnExit(e);}
}
