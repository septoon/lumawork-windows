using System.Reflection;
using System.Text.Json;
using Engineer.Core.App;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Engineer.Lite.Windows.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Инженер Lite";
        AppWindow.Resize(new SizeInt32(1280, 820));

        foreach (var section in LiteNavigation.Sections)
        {
            Navigation.MenuItems.Add(new NavigationViewItem
            {
                Content = section.Title,
                Tag = section.Key,
                Icon = new SymbolIcon(section.Section == LiteSection.Home ? Symbol.Home : Symbol.List)
            });
        }

        VersionLabel.Text = "Версия " +
            typeof(App).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        ConfigurationStatus.Text = LoadConfigurationStatus();
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var section = LiteNavigation.Restore(key);
        ContentTitle.Text = section == LiteSection.Home ? "Главная" : "Заявки";
        ContentSummary.Text = section == LiteSection.Home
            ? "Подготовка месячного ГСМ-отчёта"
            : "Закрытые заявки";
        ContentState.Text = section == LiteSection.Home
            ? "Работа с отчётами ещё не подключена."
            : "Импорт и обновление закрытых заявок ещё не подключены.";
    }

    private static string LoadConfigurationStatus()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var packaged = File.Exists(configPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(configPath)) ?? []
                : new Dictionary<string, string?>();
            var environment = new Dictionary<string, string?>
            {
                ["LUMAWORK_API_URL"] = Environment.GetEnvironmentVariable("LUMAWORK_API_URL"),
                ["LUMAWORK_API_ORIGIN"] = Environment.GetEnvironmentVariable("LUMAWORK_API_ORIGIN")
            };

            return LiteConfiguration.ResolveLumaWork(environment, packaged, new Dictionary<string, string?>()).State switch
            {
                EndpointState.Ready => "Адрес LumaWork API настроен. Подключение ещё не проверено.",
                EndpointState.Invalid => "Ошибка конфигурации: некорректный адрес LumaWork API.",
                _ => "Адрес LumaWork API не настроен."
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return "Не удалось прочитать настройки подключения.";
        }
    }
}
