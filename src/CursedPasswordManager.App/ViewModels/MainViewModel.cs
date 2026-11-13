using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using CursedPasswordManager.App.Services;
using CursedPasswordManager.Core;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;
using Microsoft.Win32;

namespace CursedPasswordManager.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private VaultSession? _session;
    private VaultData? _data;
    private byte[]? _current;
    private ReplicaStore _store;
    private readonly SecureClipboard _clipboard = new();
    private bool _busy, _unlocked, _firstRun, _lockPending;
    private int _generation;
    private string _status = "Seu cofre, sob seu controle. Totalmente offline.";
    private string _query = "", _sort = "Nome", _filterCategory = "Todas", _generated = "", _entropy = "Gere uma senha para começar.";
    private bool _favoritesOnly, _revealGenerated, _revealSelected;
    private Credential? _selected;
    private int _page, _length = 30;
    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public ObservableCollection<Credential> Credentials { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<string> FilterCategories { get; } = ["Todas"];
    public ObservableCollection<ReplicaState> ReplicaStates { get; } = [];
    public ObservableCollection<AuditFinding> Findings { get; } = [];
    public string[] SortOptions { get; } = ["Nome", "Criação", "Alteração", "Favoritos", "Categoria"];
    public bool IsBusy { get => _busy; private set { Set(ref _busy,value); Changed(nameof(CanInteract)); CommandManager.InvalidateRequerySuggested(); } }
    public bool CanInteract => !IsBusy;
    public bool IsUnlocked { get => _unlocked; private set { Set(ref _unlocked,value); Changed(nameof(IsLocked)); Changed(nameof(LockLabel)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsLocked => !IsUnlocked;
    public bool IsFirstRun { get=>_firstRun; private set { Set(ref _firstRun,value); Changed(nameof(UnlockTitle)); Changed(nameof(UnlockButton)); } }
    public string UnlockTitle => IsFirstRun ? "Bem-vindo ao seu novo cofre." : "Bom ter você de volta.";
    public string UnlockButton => IsFirstRun ? "Criar meu cofre" : "Desbloquear cofre";
    public string LockLabel => IsUnlocked ? "● Cofre desbloqueado" : "● Cofre bloqueado";
    public string ModeLabel => Paths.Portable ? "MODO PORTÁTIL • dados junto ao aplicativo" : "LOCAL • nenhum serviço conectado";
    public string CryptoInfo => _session is null ? "AES-256-GCM • Argon2id • proteção totalmente local."
        : $"AES-256-GCM • Argon2id: {_session.Parameters.MemoryKiB / 1024} MiB / {_session.Parameters.Iterations} passagens / {_session.Parameters.Parallelism} via(s). A senha mestra não é armazenada.";
    public string Status { get=>_status; set=>Set(ref _status,value); }
    public int Page { get=>_page; set=>Set(ref _page,value); }
    public string Query { get=>_query; set { Set(ref _query,value); RefreshList(); } }
    public string Sort { get=>_sort; set { Set(ref _sort,value); RefreshList(); } }
    public string FilterCategory { get=>_filterCategory; set { Set(ref _filterCategory,value); RefreshList(); } }
    public bool FavoritesOnly { get=>_favoritesOnly; set { Set(ref _favoritesOnly,value); RefreshList(); } }
    public Credential? Selected { get=>_selected; set { Set(ref _selected,value); _revealSelected=false; Changed(nameof(SelectedPassword)); Changed(nameof(HasSelection)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasSelection => Selected is not null;
    public string SelectedPassword => Selected is null ? "Selecione uma credencial" : _revealSelected ? Selected.Password : "••••••••••••••••";
    public string GeneratedDisplay => _generated.Length==0 ? "Sua próxima senha começa aqui." : _revealGenerated ? _generated : new string('•', Math.Min(_generated.Length, 40));
    public string Entropy { get=>_entropy; private set=>Set(ref _entropy,value); }
    public int Length { get=>_length; set=>Set(ref _length,value); }
    public bool Digits { get; set; } = true;
    public bool Lowercase { get; set; }
    public bool Uppercase { get; set; }
    public bool Symbols { get; set; }
    public bool RequireEach { get; set; } = true;
    public string AuditSummary { get; private set; } = "Execute uma análise para encontrar senhas que merecem revisão.";
    public bool EmptyVault => Credentials.Count == 0;
    public event Action? Locked;
    public ICommand GenerateCommand { get; }
    public ICommand CopyGeneratedCommand { get; }
    public ICommand RevealGeneratedCommand { get; }
    public ICommand SaveGeneratedCommand { get; }
    public ICommand LockCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand CopyAccountCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand RevealCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand NewCategoryCommand { get; }
    public ICommand RenameCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand AuditCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand RecoverCommand { get; }
    public ICommand ChangeMasterCommand { get; }
    public ICommand ChoosePrimaryCommand { get; }
    public ICommand ChooseSecondaryCommand { get; }
    public ICommand ClassicCommand { get; }
    public ICommand CursedXpCommand { get; }
    public ICommand ModernCommand { get; }
    public ICommand HelloCommand { get; }
    public ICommand DocumentationCommand { get; }
    public string HelloStatus => Paths.Portable ? "Desabilitado no modo portátil." : "Proteção por chave Windows Hello: verificar disponibilidade ao ativar.";
    private string[] _configuredPaths;

    public MainViewModel(AppPaths paths, AppSettings settings)
    {
        Paths=paths; Settings=settings; _configuredPaths=paths.Replicas(settings).ToArray(); _store=new(_configuredPaths);
        GenerateCommand = Command(()=> { var result=PasswordGenerator.Generate(new(Length,Digits,Lowercase,Uppercase,Symbols,RequireEach)); _generated=result.Value; _revealGenerated=false; Changed(nameof(GeneratedDisplay)); Entropy=$"{result.EntropyBits:F1} bits estimados • geração aleatória uniforme"; return Task.CompletedTask; });
        CopyGeneratedCommand=Command(()=>Copy(_generated),()=>_generated.Length>0);
        RevealGeneratedCommand=new RelayCommand(()=>{ _revealGenerated=!_revealGenerated; Changed(nameof(GeneratedDisplay)); },()=>_generated.Length>0 && !IsBusy);
        SaveGeneratedCommand=Command(()=>EditAsync(true),()=>IsUnlocked && _generated.Length>0);
        LockCommand=new RelayCommand(Lock);
        AddCommand=Command(()=>EditAsync(false),()=>IsUnlocked);
        EditCommand=Command(()=>EditAsync(false,true),()=>Selected is not null);
        DeleteCommand=Command(DeleteAsync,()=>Selected is not null);
        FavoriteCommand=Command(()=>MutateAsync(d=>{ var c=d.Credentials.Single(c=>c.Id==Selected!.Id); c.Favorite=!c.Favorite; c.ModifiedAt=DateTimeOffset.UtcNow; }),()=>Selected is not null);
        CopyAccountCommand=Command(()=>Copy(Selected!.Account),()=>Selected is not null);
        CopyPasswordCommand=Command(()=>Copy(Selected!.Password),()=>Selected is not null);
        RevealCommand=new RelayCommand(()=> { _revealSelected=!_revealSelected; Changed(nameof(SelectedPassword)); },()=>Selected is not null && !IsBusy);
        HistoryCommand=Command(HistoryAsync,()=>Selected is not null);
        NewCategoryCommand=Command(()=>CategoryAsync("new"),()=>IsUnlocked);
        RenameCategoryCommand=Command(()=>CategoryAsync("rename"),()=>IsUnlocked);
        DeleteCategoryCommand=Command(()=>CategoryAsync("delete"),()=>IsUnlocked);
        AuditCommand=Command(()=>{ var report=PasswordAudit.Analyze(_data!); Findings.Clear(); foreach(var f in report.Findings) Findings.Add(f); AuditSummary=$"{report.Total} contas • {report.Reused} reutilizadas • {report.Weak} fracas • {report.Findings.Count} para revisar"; Changed(nameof(AuditSummary)); return Task.CompletedTask; },()=>IsUnlocked);
        ExportCommand=Command(ExportAsync,()=>IsUnlocked); ImportCommand=Command(ImportAsync,()=>IsUnlocked);
        SaveSettingsCommand=Command(SaveSettingsAsync,()=>IsUnlocked);
        VerifyCommand=Command(async()=>SetStates(await _store.VerifyAsync(_session!,VaultCipher.Hash(_current!))),()=>IsUnlocked);
        SyncCommand=Command(async()=>{ var result=await _store.CommitAsync(_current!,_session!); Report(result); },()=>IsUnlocked);
        RecoverCommand=Command(RecoverAsync);
        ChangeMasterCommand=Command(ChangeMasterAsync,()=>IsUnlocked);
        ChoosePrimaryCommand=new RelayCommand(()=>ChooseFolder(true),()=>!IsBusy);
        ChooseSecondaryCommand=new RelayCommand(()=>ChooseFolder(false),()=>!IsBusy);
        ClassicCommand=Command(()=>SetThemeAsync("XpClassic")); CursedXpCommand=Command(()=>SetThemeAsync("CursedXp")); ModernCommand=Command(()=>SetThemeAsync("CursedModern"));
        HelloCommand=Command(HelloAsync,()=>IsUnlocked && !Paths.Portable);
        DocumentationCommand=new RelayCommand(()=> { string file=Path.Combine(AppContext.BaseDirectory,"README.md"); if(File.Exists(file)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file){UseShellExecute=true}); else Dialogs.Message("Documentação", "Consulte README.md e a pasta docs na raiz do projeto ou no pacote de distribuição."); });
    }
    private ICommand Command(Func<Task> action, Func<bool>? enabled=null)=>new RelayCommand(()=>_ = RunAsync(action),()=>!IsBusy && (enabled?.Invoke()??true));
    public async Task InitializeAsync()
    {
        var scan=await _store.ScanAsync(); IsFirstRun=scan.Candidates.Count==0 && scan.States.All(s=>s.Status=="Ausente") && !_store.RecoveryFiles().Any(); SetStates(scan.States);
        if (!IsFirstRun && scan.Candidates.Count==0) Status="Nenhuma cópia corrente legível. Use Recuperar para escolher um snapshot ou backup.";
    }
    public async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return; IsBusy=true;
        try { await operation(); }
        catch (OperationCanceledException) { Status="Operação cancelada."; }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException)
        {
            string message=ex switch { CryptographicException=>"Não foi possível autenticar o cofre. Verifique a senha e a integridade do arquivo.", System.Text.Json.JsonException=>"O conteúdo do arquivo não tem uma estrutura válida.", UnauthorizedAccessException=>"Acesso negado à pasta. Escolha um destino com permissão de gravação.", _=>ex.Message };
            Status=message; if(!_lockPending) Dialogs.Message("Não foi possível concluir",message);
        }
        finally { IsBusy=false; if(_lockPending) { _lockPending=false; FinishLock(); } }
    }
    public Task UnlockAsync(string password,string confirmation)=>RunAsync(async()=>
    {
        int generation=_generation;
        if(IsFirstRun)
        {
            if(password!=confirmation) throw new ArgumentException("As senhas mestras não coincidem.");
            Settings.Validate(); var paths=Paths.Replicas(Settings).ToArray(); var store=new ReplicaStore(paths);
            var scan=await store.ScanAsync(); if(scan.States.Any(s=>s.Status!="Ausente")) throw new IOException("Já existe um arquivo em um dos destinos. Use Recuperar para abrir o cofre existente.");
            var session=await VaultCipher.CreateSessionAsync(password);
            try
            {
                if(generation!=_generation) throw new OperationCanceledException();
                var data=new VaultData(); var bytes=await Task.Run(()=>VaultCipher.Seal(session,data,1));
                var result=await store.CommitAsync(bytes,session);
                _session=session; _data=data; _current=bytes; _store=store; _configuredPaths=paths; IsFirstRun=false;
                await Paths.SaveAsync(Settings);
                if(generation!=_generation) return;
                IsUnlocked=true; Refresh(); Report(result);
            }
            catch { if(_session!=session) session.Dispose(); throw; }
        }
        else
        {
            var scan=await _store.ScanAsync(); var distinct=scan.Candidates.GroupBy(c=>c.Hash).Select(g=>g.First()).ToArray();
            if(distinct.Length==0) throw new IOException("Não há cópia corrente legível. Use Recuperar para selecionar um snapshot ou backup.");
            var choice=distinct.Length==1 ? distinct[0] : Dialogs.Choose("Cópias divergentes", "As revisões abaixo ainda não foram autenticadas. Escolha a cópia que deseja abrir. As demais serão preservadas em .prev na sincronização; nenhuma revisão será escolhida automaticamente.", distinct,c=>c.Label);
            if(choice is null) return;
            var opened=await VaultCipher.OpenAsync(choice.Bytes,password);
            if(generation!=_generation) { opened.Session.Dispose(); return; }
            _session?.Dispose(); _session=opened.Session; _data=opened.Data; _current=choice.Bytes; IsUnlocked=true; Refresh();
            SetStates(await _store.VerifyAsync(_session,choice.Hash));
            Status="Cofre desbloqueado. Verifique o estado das cópias em Configurações.";
        }
    });
    public void Lock()
    {
        _generation++; IsUnlocked=false; _lockPending=IsBusy;
        ClearDisplay(); _clipboard.ClearOwned(); Locked?.Invoke();
        if(!IsBusy) FinishLock();
    }
    private void FinishLock() { IsUnlocked=false; _session?.Dispose(); _session=null; _data=null; _current=null; ClearDisplay(); Status="Cofre bloqueado. Use sua senha mestra para continuar."; }
    private void ClearDisplay()
    {
        Selected=null; Credentials.Clear(); Categories.Clear(); FilterCategories.Clear(); FilterCategories.Add("Todas"); Findings.Clear();
        _query=""; _filterCategory="Todas"; _generated=""; _revealGenerated=false; _revealSelected=false;
        Changed(nameof(Query)); Changed(nameof(FilterCategory)); Changed(nameof(GeneratedDisplay)); Changed(nameof(EmptyVault));
        AuditSummary="Execute uma análise para encontrar senhas que merecem revisão."; Changed(nameof(AuditSummary)); Entropy="Gere uma senha para começar.";
    }
    private void EnsureActive() { if(_lockPending || !IsUnlocked || _session is null || _data is null) throw new OperationCanceledException(); }
    private async Task MutateAsync(Action<VaultData> mutation)
    {
        EnsureActive(); var next=_data!.Clone(); mutation(next); next.Validate(); await CommitDataAsync(next);
    }
    private async Task CommitDataAsync(VaultData next)
    {
        EnsureActive(); var session=_session!; var current=_current!;
        var header=VaultCipher.ReadHeader(current); var parent=SHA256.HashData(current);
        var bytes=await Task.Run(()=>VaultCipher.Seal(session,next,checked(header.Revision+1),parent));
        if(_lockPending) throw new OperationCanceledException();
        var result=await _store.CommitAsync(bytes,session);
        _current=bytes; _data=next;
        if(!_lockPending) { Refresh(); Report(result); }
    }
    private async Task EditAsync(bool fromGenerator,bool edit=false)
    {
        EnsureActive(); var result=Dialogs.Edit(edit?Selected:null,_data!.Categories,fromGenerator?_generated:null);
        if(result is null) return;
        await MutateAsync(d=>{ int index=d.Credentials.FindIndex(c=>c.Id==result.Id); if(index<0)d.Credentials.Add(result);else d.Credentials[index]=result; });
        if(!_lockPending) { Selected=Credentials.FirstOrDefault(c=>c.Id==result.Id); Page=1; }
    }
    private async Task DeleteAsync()
    {
        var id=Selected!.Id;
        if(Dialogs.Confirm("Excluir credencial?","A credencial e seu histórico serão removidos do cofre atual. Snapshots anteriores ainda podem conter esses dados.")) await MutateAsync(d=>d.Credentials.RemoveAll(c=>c.Id==id));
    }
    private async Task HistoryAsync()
    {
        var credential=Selected!;
        var version=Dialogs.Choose("Histórico de senhas", "Valores ocultos por padrão. Guardar senhas antigas aumenta a quantidade de segredos preservados.", credential.History,h=>$"{h.ChangedAt.ToLocalTime():g}  •  ••••••••••••");
        if(version is null) return;
        var action=Dialogs.Choose("Versão anterior","Escolha uma operação. Restaurar também preserva a senha atualmente em uso.",new[]{"Copiar","Mostrar","Restaurar","Excluir versão","Limpar todo o histórico"},s=>s);
        EnsureActive();
        switch(action)
        {
            case "Copiar": await Copy(version.Password); break;
            case "Mostrar": Dialogs.Message("Senha anterior",version.Password); break;
            case "Restaurar": if(Dialogs.Confirm("Restaurar senha?","A senha atual será movida para o histórico.")) await MutateAsync(d=>d.Credentials.Single(c=>c.Id==credential.Id).ChangePassword(version.Password)); break;
            case "Excluir versão": if(Dialogs.Confirm("Excluir versão?","Esta versão será removida do histórico atual.")) await MutateAsync(d=>d.Credentials.Single(c=>c.Id==credential.Id).History.RemoveAll(h=>h.Id==version.Id)); break;
            case "Limpar todo o histórico": if(Dialogs.Confirm("Limpar histórico?","Todas as versões anteriores desta credencial serão removidas do cofre atual.")) await MutateAsync(d=>d.Credentials.Single(c=>c.Id==credential.Id).History.Clear()); break;
        }
    }
    private async Task CategoryAsync(string action)
    {
        if(action=="new")
        { var name=Dialogs.Text("Nova categoria","Nome da categoria"); if(name is not null) await MutateAsync(d=>{if(d.Categories.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("A categoria já existe.");d.Categories.Add(name);}); return; }
        var category=Dialogs.Choose("Organizar categorias","Selecione uma categoria. Outros é uma categoria permanente.",_data!.Categories.Where(c=>c!="Outros").ToArray(),s=>s);
        if(category is null)return;
        if(action=="rename") { var name=Dialogs.Text("Renomear categoria","Novo nome",category); if(name is not null)await MutateAsync(d=>d.RenameCategory(category,name)); }
        else if(Dialogs.Confirm("Excluir categoria?","As credenciais serão movidas para Outros e continuarão no cofre."))await MutateAsync(d=>d.RemoveCategory(category));
    }
    private Task Copy(string value) { _clipboard.Copy(value,Settings.ClipboardSeconds); Status=$"Copiado. Limpeza em {Settings.ClipboardSeconds} segundos se a área de transferência não mudar."; return Task.CompletedTask; }
    private async Task ExportAsync()
    {
        var dialog=new SaveFileDialog { Filter="Backup criptografado|*.cpmbackup",FileName="CursedPasswordManager.cpmbackup",DefaultExt=".cpmbackup" };
        if(dialog.ShowDialog()!=true)return;
        var password=Dialogs.Secret("Proteger exportação","Crie uma senha exclusiva para este backup. Ela será necessária para importá-lo em outro computador.",true); if(password is null)return;
        EnsureActive(); var data=_data!.Clone(); using var export=await VaultCipher.CreateSessionAsync(password); password=null;
        if(_lockPending)throw new OperationCanceledException();
        var bytes=await Task.Run(()=>VaultCipher.Seal(export,data,1)); await AtomicFile.WriteAsync(dialog.FileName,bytes);
        _=VaultCipher.Decrypt(await AtomicFile.ReadBoundedAsync(dialog.FileName),export); Status="Backup criptografado exportado e verificado.";
    }
    private async Task ImportAsync()
    {
        var dialog=new OpenFileDialog { Filter="Cofres e backups criptografados|*.cpmbackup;*.cpmvault;*.prev" };
        if(dialog.ShowDialog()!=true)return;
        var password=Dialogs.Secret("Importar backup","Informe a senha de proteção do arquivo selecionado."); if(password is null)return;
        var opened=await VaultCipher.OpenAsync(await AtomicFile.ReadBoundedAsync(dialog.FileName),password); password=null;
        using(opened.Session)
        {
            EnsureActive(); var mode=Dialogs.Choose("Como importar?","A mesclagem preserva as duas versões quando um identificador possui conteúdo divergente. Duplicatas exatas são ignoradas.",new[]{"Mesclar e preservar divergências","Substituir todo o conteúdo"},s=>s);
            if(mode is null)return;
            if(mode.StartsWith("Mesclar",StringComparison.Ordinal)) await MutateAsync(d=>d.Merge(opened.Data));
            else if(Dialogs.Confirm("Substituir o cofre?","O conteúdo atual será preservado em um snapshot criptografado antes da substituição.")) await CommitDataAsync(opened.Data.Clone());
        }
    }
    private async Task ChangeMasterAsync()
    {
        string? old=Dialogs.Secret("Confirmar identidade","Informe a senha mestra atual."); if(old is null)return;
        var authenticated=await VaultCipher.OpenAsync(_current!,old); old=null; authenticated.Session.Dispose(); EnsureActive();
        string? password=Dialogs.Secret("Nova senha mestra","Snapshots e exportações anteriores continuam protegidos pela senha antiga. Guarde-a enquanto precisar deles.",true); if(password is null)return;
        var next=await VaultCipher.CreateSessionAsync(password,_session!.VaultId); password=null;
        try
        {
            EnsureActive(); var header=VaultCipher.ReadHeader(_current!); var bytes=await Task.Run(()=>VaultCipher.Seal(next,_data!,checked(header.Revision+1),SHA256.HashData(_current!)));
            if(_lockPending)throw new OperationCanceledException();
            await HelloService.DisableAsync(Paths);
            var result=await _store.CommitAsync(bytes,next); _session.Dispose(); _session=next; _current=bytes; Changed(nameof(CryptoInfo)); Report(result);
            if(!result.Complete) Status="Senha alterada na recuperação interna. Há cópias pendentes que ainda podem exigir a senha anterior.";
        }
        catch { if(_session!=next)next.Dispose(); throw; }
    }
    private async Task RecoverAsync()
    {
        if(IsUnlocked && !Dialogs.Confirm("Abrir outra cópia?","O cofre atual será bloqueado antes da recuperação."))return;
        if(IsUnlocked) { _session?.Dispose(); _session=null; _data=null; _current=null; IsUnlocked=false; ClearDisplay(); }
        var files=_store.RecoveryFiles().ToList(); files.Add("Escolher outro arquivo…");
        string? selected=Dialogs.Choose("Recuperar cofre","Selecione uma cópia ou snapshot. A integridade será autenticada com a senha. Nenhum arquivo será substituído até você sincronizar ou salvar.",files,s=>s);
        if(selected is null)return;
        if(selected=="Escolher outro arquivo…") { var dialog=new OpenFileDialog{Filter="Cofres e backups|*.cpmvault;*.cpmbackup;*.prev"};if(dialog.ShowDialog()!=true)return;selected=dialog.FileName; }
        var password=Dialogs.Secret("Desbloquear recuperação","Use a senha que protegia esta cópia na data em que foi salva.");if(password is null)return;
        int generation=_generation; var bytes=await AtomicFile.ReadBoundedAsync(selected); var opened=await VaultCipher.OpenAsync(bytes,password); password=null;
        if(generation!=_generation){opened.Session.Dispose();return;}
        await _store.ScanAsync(); _session=opened.Session; _data=opened.Data; _current=bytes; IsFirstRun=false; IsUnlocked=true; Refresh();
        Status="Cópia recuperada em memória. Confira os registros e use Sincronizar para restaurar os destinos."; Page=1;
    }
    private async Task SaveSettingsAsync()
    {
        Settings.Validate(); var paths=Paths.Replicas(Settings).ToArray();
        if(!Settings.SecondaryEnabled && !Dialogs.Confirm("Reduzir redundância?","A cópia secundária ficará desativada. A recuperação interna continuará obrigatória.")) { Settings.SecondaryEnabled=true; Changed(nameof(Settings)); return; }
        ReplicaStore? migratedStore=null;
        if(!paths.SequenceEqual(_configuredPaths,StringComparer.OrdinalIgnoreCase))
        {
            EnsureActive(); var migrated=new ReplicaStore(paths); var scan=await migrated.ScanAsync();
            if(scan.Candidates.Any(c=>c.Hash!=VaultCipher.Hash(_current!)))throw new IOException("Um novo destino contém um cofre diferente. Escolha uma pasta vazia para a migração.");
            var result=await migrated.CommitAsync(_current!,_session!); Report(result);
            if(!result.Complete)throw new IOException("A migração não foi concluída. Os caminhos anteriores continuam ativos; ajuste os destinos e tente novamente.");
            migratedStore=migrated;
        }
        WindowsIntegration.SetStartup(Settings.StartWithWindows,Paths.Portable); await Paths.SaveAsync(Settings); Status="Configurações salvas.";
        if(migratedStore is not null){_store=migratedStore;_configuredPaths=paths;}
    }
    private void ChooseFolder(bool primary)
    {
        var dialog=new OpenFolderDialog { Title=primary?"Pasta principal":"Pasta da cópia secundária" };
        if(dialog.ShowDialog()!=true)return;
        if(primary)Settings.PrimaryFolder=Paths.StorePath(dialog.FolderName);else Settings.SecondaryFolder=Paths.StorePath(dialog.FolderName);
        Changed(nameof(Settings));
    }
    private async Task SetThemeAsync(string name)
    {
        var persisted=await Paths.LoadAsync();persisted.Theme=name;await Paths.SaveAsync(persisted);
        Settings.Theme=name;ThemeService.Apply(name);Status="Tema aplicado.";
    }
    private async Task HelloAsync()
    {
        EnsureActive();
        if(HelloService.IsEnrolled(Paths)) { if(Dialogs.Confirm("Desativar Windows Hello?","O desbloqueio por senha mestra continuará disponível.")){await HelloService.DisableAsync(Paths);Status="Windows Hello desativado.";}return; }
        var password=Dialogs.Secret("Ativar Windows Hello","Confirme sua senha mestra. A chave ficará vinculada ao usuário e ao dispositivo; mantenha sua senha mestra para recuperação."); if(password is null)return;
        var opened=await VaultCipher.OpenAsync(_current!,password); password=null;
        using(opened.Session) { EnsureActive(); await HelloService.EnrollAsync(Paths,opened.Session); }
        Status="Windows Hello ativado com chave não exportável e autenticação obrigatória.";
    }
    public Task HelloUnlockAsync()=>RunAsync(async()=>
    {
        int generation=_generation; var scan=await _store.ScanAsync(); var unique=scan.Candidates.GroupBy(c=>c.Hash).Select(g=>g.First()).ToArray();
        if(unique.Length!=1)throw new InvalidOperationException("Use a senha mestra para resolver cópias ausentes ou divergentes.");
        var opened=await HelloService.OpenAsync(Paths,unique[0].Bytes);
        if(generation!=_generation){opened.Session.Dispose();return;}
        _session=opened.Session;_data=opened.Data;_current=unique[0].Bytes;IsUnlocked=true;Refresh();Status="Cofre desbloqueado pelo Windows Hello.";
    });
    public void SelectFinding(AuditFinding finding) { Query=""; FavoritesOnly=false; FilterCategory="Todas"; Selected=Credentials.FirstOrDefault(c=>c.Id==finding.Id); }
    private void Refresh()
    {
        if(_lockPending)return;
        Changed(nameof(CryptoInfo));
        Categories.Clear();FilterCategories.Clear();FilterCategories.Add("Todas"); foreach(var category in _data!.Categories){Categories.Add(category);FilterCategories.Add(category);}
        if(!FilterCategories.Contains(_filterCategory)) {_filterCategory="Todas";Changed(nameof(FilterCategory));}
        Findings.Clear(); AuditSummary="Execute novamente a auditoria após alterações.";Changed(nameof(AuditSummary)); RefreshList();
    }
    private void RefreshList()
    {
        var id=Selected?.Id; Credentials.Clear();
        if(IsUnlocked && _data is not null) foreach(var c in _data.Search(_query,_favoritesOnly,_filterCategory=="Todas"?null:_filterCategory,_sort)) Credentials.Add(c);
        Selected=Credentials.FirstOrDefault(c=>c.Id==id);Changed(nameof(EmptyVault));
    }
    private void SetStates(IEnumerable<ReplicaState> states) { ReplicaStates.Clear();foreach(var s in states)ReplicaStates.Add(s); }
    private void Report(SaveResult result) { SetStates(result.States); Status=result.Complete?"Cofre salvo e todas as cópias verificadas.":"Cofre salvo na recuperação interna. Há cópias pendentes: confira Configurações."; }
    public void Dispose() { _session?.Dispose(); _clipboard.Dispose(); }
}
