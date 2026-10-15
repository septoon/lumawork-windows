using System.Reflection;
using Engineer.Core.App;
using Engineer.Core.Auth;
using Engineer.Lite.Windows.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace Engineer.Lite.Windows.Views;

public sealed partial class MainWindow : Window
{
    private readonly SessionController? session;
    private bool restored;
    public MainWindow()
    {
        InitializeComponent();
        Title = "Инженер Lite";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new SizeInt32(1160, 840));
        var version = "Версия " + typeof(App).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        VersionLabel.Text = LoginVersion.Text = version;
        foreach (var section in LiteNavigation.Sections)
            Navigation.MenuItems.Add(new NavigationViewItem { Content = section.Title, Tag = section.Key, Icon = new SymbolIcon(section.Section == LiteSection.Home ? Symbol.Home : Symbol.List) });
        Navigation.SelectedItem = Navigation.MenuItems[0];
        try
        {
            session = SessionFactory.Create();
            session.Changed += RenderSession;
            LoginScreen.Loaded += async (_, _) => { if (restored) return; restored = true; await session.RestoreAsync(); RenderSession(); };
            Closed += (_, _) => { session.CancelPending(); CodeBox.Password = SoPassword.Password = ""; };
            RenderSession();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            LoginError.Message = "Не удалось загрузить настройки подключения. Проверьте app/appsettings.json и перезапустите приложение.";
            LoginError.IsOpen = true; LoginFields.IsEnabled = false;
        }
    }
    private void RenderSession()
    {
        if (session == null) return;
        var loggedIn = session.App != null;
        LoginScreen.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        Navigation.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        LoginFields.IsEnabled = !session.IsBusy;
        LoginProgress.IsActive = !loggedIn && session.IsBusy;
        LoginProgress.Visibility = LoginProgress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        EmailBox.IsEnabled = session.CodeEmail == null;
        CodeBox.Visibility = CodeHint.Visibility = CodeActions.Visibility = session.CodeEmail != null ? Visibility.Visible : Visibility.Collapsed;
        CodeHint.Text = session.CodeEmail == null ? "" : $"Код отправлен на {session.CodeEmail}.";
        LoginButton.Content = session.CodeEmail == null ? "Получить код" : "Войти";
        LoginError.Message = session.Error ?? ""; LoginError.IsOpen = !loggedIn && session.Error != null;
        RestoreRetry.Visibility = !loggedIn && session.Error != null && session.CodeEmail == null ? Visibility.Visible : Visibility.Collapsed;
        RestoreRetry.IsEnabled = !session.IsBusy;
        AccountEmail.Text = session.App?.User.Email ?? "";
        LumaStatus.Text = loggedIn ? $"Вы вошли как {session.App!.User.Email}." : "";
        SessionWarning.Message = session.Error ?? ""; SessionWarning.IsOpen = loggedIn && session.Error != null;
        SoConnected.Visibility = session.SimpleOne != null ? Visibility.Visible : Visibility.Collapsed;
        SoDisconnected.Visibility = session.SimpleOne == null ? Visibility.Visible : Visibility.Collapsed;
        SoIdentity.Text = session.SimpleOne == null ? "" : $"Подключён: {session.SimpleOne.User.DisplayName} ({session.SimpleOne.User.Username})";
        SoError.Message = session.SimpleOneError ?? ""; SoError.IsOpen = session.SimpleOneError != null;
        SoFields.IsEnabled = SoOpenButton.IsEnabled = !session.IsBusy && session.AppConnection == ConnectionState.Online;
        SoLogoutButton.IsEnabled = !session.IsBusy;
        SoProgress.IsActive = loggedIn && session.IsBusy; SoProgress.Visibility = SoProgress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (session.SimpleOne != null) { SoFields.Visibility = Visibility.Collapsed; SoPassword.Password = ""; }
        if (!loggedIn) { SoPassword.Password = ""; SoUsername.Text = ""; SoFields.Visibility = Visibility.Collapsed; }
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        SoPassword.Password = ""; SoFields.Visibility = Visibility.Collapsed;
        var section = LiteNavigation.Restore((args.SelectedItem as NavigationViewItem)?.Tag as string);
        ContentTitle.Text = section == LiteSection.Home ? "Главная" : "Заявки";
        ContentSummary.Text = section == LiteSection.Home ? "Подготовка месячного ГСМ-отчёта" : "Закрытые заявки: номера и адреса";
        ContentState.Text = section == LiteSection.Home
            ? "Импорт реквизитов из XLSX и подготовка отчёта — следующий этап Lite."
            : "Подключите SimpleOne ниже. Импорт и обновление архива заявок — следующий этап Lite.";
    }
    private async Task SubmitLoginAsync()
    {
        if (session == null || session.IsBusy) return;
        if (session.CodeEmail == null)
        {
            if (await session.RequestCodeAsync(EmailBox.Text)) { CodeBox.Password = ""; CodeBox.Focus(FocusState.Programmatic); }
        }
        else
        {
            var code = CodeBox.Password; CodeBox.Password = "";
            await session.VerifyCodeAsync(code);
        }
    }
    private async void Login_Click(object sender, RoutedEventArgs e) => await SubmitLoginAsync();
    private async void Login_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await SubmitLoginAsync(); } }
    private void ChangeEmail_Click(object sender, RoutedEventArgs e) { session?.ChangeEmail(); CodeBox.Password = ""; EmailBox.Focus(FocusState.Programmatic); }
    private async void Resend_Click(object sender, RoutedEventArgs e) { if (session?.CodeEmail is { } email) await session.RequestCodeAsync(email); }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (session != null) await session.RestoreAsync(); }
    private async void Logout_Click(object sender, RoutedEventArgs e) { if (session != null) { CodeBox.Password = SoPassword.Password = ""; EmailBox.Text = ""; await session.LogoutAsync(); } }
    private void SoOpen_Click(object sender, RoutedEventArgs e) { SoFields.Visibility = Visibility.Visible; SoUsername.Focus(FocusState.Programmatic); }
    private void SoCancel_Click(object sender, RoutedEventArgs e) { SoFields.Visibility = Visibility.Collapsed; SoPassword.Password = ""; }
    private async Task SubmitSimpleOneAsync()
    {
        if (session == null || session.IsBusy) return;
        var password = SoPassword.Password; SoPassword.Password = "";
        await session.LoginSimpleOneAsync(SoUsername.Text, password);
    }
    private async void SoLogin_Click(object sender, RoutedEventArgs e) => await SubmitSimpleOneAsync();
    private async void So_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await SubmitSimpleOneAsync(); } }
    private void SoLogout_Click(object sender, RoutedEventArgs e) => session?.DisconnectSimpleOne();
}
