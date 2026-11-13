using System.Windows;
using System.Windows.Controls;
using CursedPasswordManager.Core;

namespace CursedPasswordManager.App.Services;

public static class Dialogs
{
    private static Window Shell(string title, UIElement body, double width = 520)
    {
        var window = new Window { Title = title, Owner = Application.Current.MainWindow, Width = width, SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height - 60, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Language = System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR"), Icon = Application.Current.MainWindow?.Icon };
        window.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        return window;
    }
    private static StackPanel Panel(string title, string message)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,18) });
        return panel;
    }
    private static void Buttons(Window w, Panel p, Action accept, string label = "Confirmar")
    {
        var row = new WrapPanel { Margin = new Thickness(0,16,0,0), HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true }; cancel.Click += (_,_) => w.DialogResult = false;
        var ok = new Button { Content = label, IsDefault = true }; ok.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); ok.Click += (_,_) => accept();
        row.Children.Add(cancel); row.Children.Add(ok); p.Children.Add(row);
    }
    public static bool Confirm(string title, string message)
    {
        var p = Panel(title, message); var w = Shell(title,p); Buttons(w,p,()=>w.DialogResult=true); return w.ShowDialog() == true;
    }
    public static void Message(string title, string message)
    {
        var p = Panel(title,message); var w = Shell(title,p);
        var close = new Button { Content="Entendi", IsDefault=true, IsCancel=true, HorizontalAlignment=HorizontalAlignment.Right };
        close.Click += (_,_)=>w.Close(); p.Children.Add(close); w.ShowDialog();
    }
    public static string? Text(string title, string message, string initial = "")
    {
        var p = Panel(title,message); var field = new TextBox { Text=initial, MaxLength=100 }; p.Children.Add(field); var w = Shell(title,p);
        Buttons(w,p,()=> { if (!string.IsNullOrWhiteSpace(field.Text)) w.DialogResult=true; }); w.Loaded += (_,_)=>field.Focus();
        return w.ShowDialog()==true ? field.Text.Trim() : null;
    }
    public static string? Secret(string title, string message, bool confirm = false)
    {
        var p = Panel(title,message); var field = new PasswordBox { MaxLength=1024 }; var again = new PasswordBox { MaxLength=1024 };
        p.Children.Add(new TextBlock { Text="Senha" }); p.Children.Add(field);
        if (confirm) { p.Children.Add(new TextBlock { Text="Confirme a senha" }); p.Children.Add(again); }
        var error = new TextBlock { TextWrapping=TextWrapping.Wrap }; p.Children.Add(error); var w = Shell(title,p);
        Buttons(w,p,()=> { if (confirm && (field.Password!=again.Password || field.Password.Length<12)) { error.Text="As senhas devem coincidir e ter pelo menos 12 caracteres."; return; } w.DialogResult=true; });
        w.Loaded += (_,_)=>field.Focus(); bool accepted = w.ShowDialog()==true; string? value = accepted ? field.Password : null;
        field.Clear(); again.Clear(); return value;
    }
    public static T? Choose<T>(string title, string message, IReadOnlyList<T> items, Func<T,string> label) where T : class
    {
        var p = Panel(title,message); var list = new ListBox { MaxHeight=300, MinHeight=90 };
        foreach (var item in items) list.Items.Add(new TextBlock { Text=label(item), TextWrapping=TextWrapping.Wrap, Tag=item });
        p.Children.Add(list); var w = Shell(title,p,700); Buttons(w,p,()=> { if (list.SelectedItem is not null) w.DialogResult=true; },"Selecionar");
        return w.ShowDialog()==true && list.SelectedItem is TextBlock selected ? (T)selected.Tag : null;
    }
    public static Credential? Edit(Credential? source, IEnumerable<string> categories, string? generated = null)
    {
        var p = Panel(source is null ? "Nova credencial" : "Editar credencial", "Os campos serão guardados dentro do cofre criptografado.");
        var service = new TextBox { Text=source?.Service ?? "", MaxLength=500 };
        var account = new TextBox { Text=source?.Account ?? "", MaxLength=1000 };
        var password = new PasswordBox { Password=generated ?? source?.Password ?? "", MaxLength=4096 };
        var category = new ComboBox { ItemsSource=categories, SelectedItem=source?.Category ?? "Outros" };
        var favorite = new CheckBox { Content="★ Favorito", IsChecked=source?.Favorite ?? false };
        foreach (var pair in new (string,UIElement)[] { ("Serviço / aplicativo",service),("Conta",account),("Senha",password),("Categoria",category) })
        { p.Children.Add(new TextBlock { Text=pair.Item1 }); p.Children.Add(pair.Item2); }
        p.Children.Add(favorite); var error = new TextBlock(); p.Children.Add(error);
        var w = Shell("Credencial",p); Credential? result = null;
        Buttons(w,p,()=>
        {
            if (string.IsNullOrWhiteSpace(service.Text) || string.IsNullOrWhiteSpace(password.Password)) { error.Text="Preencha o serviço e a senha."; return; }
            result = new Credential { Id=source?.Id ?? Guid.NewGuid(), Service=service.Text.Trim(), Account=account.Text,
                Password=source?.Password ?? password.Password, Category=category.SelectedItem as string ?? "Outros", Favorite=favorite.IsChecked==true,
                CreatedAt=source?.CreatedAt ?? DateTimeOffset.UtcNow, ModifiedAt=DateTimeOffset.UtcNow, History=source?.History.ToList() ?? [] };
            if (source is not null) result.ChangePassword(password.Password);
            w.DialogResult=true;
        },"Salvar credencial");
        w.Loaded += (_,_)=>service.Focus(); w.ShowDialog(); password.Clear(); service.Clear(); account.Clear(); return result;
    }
}
