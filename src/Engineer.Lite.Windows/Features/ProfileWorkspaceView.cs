using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal sealed class ProfileWorkspaceView : UserControl
{
    private readonly WorkspaceController workspace;
    private readonly Window owner;
    private readonly Func<string> scope;
    private readonly Func<bool> online;
    private readonly Func<Task> reconnect;
    private readonly StackPanel details = new() { Spacing = 16 };
    private readonly TextBlock status = WorkspaceUi.Text("", true);
    private readonly InfoBar notice = new() { IsClosable = false };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly AppBarButton edit, import, refresh;
    private readonly Button cancel;
    private bool dialogOpen;
    private string? localError;
    private string previousScope="";

    internal ProfileWorkspaceView(Window owner, WorkspaceController workspace, Func<string> scope, Func<bool> online, Func<Task> reconnect)
    {
        this.owner = owner; this.workspace = workspace; this.scope = scope; this.online = online; this.reconnect = reconnect;
        var panel = new StackPanel { Spacing = 16, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(WorkspaceUi.Text("Реквизиты сотрудника, автомобиля и проектов для ГСМ-отчётов.", true));
        var actions = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsDynamicOverflowEnabled = true, HorizontalContentAlignment = HorizontalAlignment.Left };
        edit = new AppBarButton { Label = "Изменить профиль", Icon = new SymbolIcon(Symbol.Edit) };
        import = new AppBarButton { Label = "Загрузить ГСМ-отчёт (.xlsx)", Icon = new SymbolIcon(Symbol.OpenFile) };
        refresh = new AppBarButton { Label = "Обновить профиль", Icon = new SymbolIcon(Symbol.Refresh) };
        edit.Click += Edit_Click; import.Click += Import_Click; refresh.Click += Refresh_Click;
        actions.PrimaryCommands.Add(edit); actions.PrimaryCommands.Add(import); actions.PrimaryCommands.Add(refresh);
        panel.Children.Add(actions); panel.Children.Add(progress); cancel=WorkspaceUi.Button("Остановить загрузку", (_,_)=>workspace.CancelGsm()); panel.Children.Add(cancel); panel.Children.Add(notice); panel.Children.Add(status); panel.Children.Add(details);
        Content = panel;
        workspace.Changed += Render;
        Render();
    }

    internal void Render()
    {
        if(previousScope!=scope()){previousScope=scope();localError=null;}
        edit.IsEnabled = !dialogOpen;
        import.IsEnabled = !dialogOpen;
        refresh.IsEnabled = !workspace.IsGsmBusy;
        cancel.Visibility=workspace.IsGsmBusy&&!workspace.IsGsmMutation?Visibility.Visible:Visibility.Collapsed;
        progress.Visibility = workspace.IsGsmBusy ? Visibility.Visible : Visibility.Collapsed;
        notice.Message = localError ?? workspace.GsmErrorMessage ?? workspace.GsmInfoMessage ?? "";
        notice.Severity = localError != null || workspace.GsmErrorMessage != null ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        notice.IsOpen = notice.Message.Length > 0;
        status.Text = !workspace.ProfileLoaded
            ? workspace.IsGsmBusy ? "Загружаем профиль…" : "Профиль ещё не загружен. Обновите его, чтобы проверить реквизиты и сохранить изменения."
            : (online() ? "Профиль LumaWork" : "Сохранённый профиль · без подключения")
              + (workspace.LastGsmUpdated is { } updated ? $" · обновлено {updated.ToLocalTime():g}" : "");
        if (workspace.ProfileLoaded && workspace.GsmLoadingMessage is { } loading) status.Text += "\n" + loading;
        details.Children.Clear();
        if (workspace.ProfileResult.Profile is not { } profile)
        {
            if (workspace.ProfileLoaded)
                details.Children.Add(WorkspaceUi.Card("Профиль не заполнен", "Создайте профиль вручную или загрузите готовый ГСМ-отчёт. Перед сохранением можно проверить все поля."));
            return;
        }
        string Value(string key) => GsmValues.Text(profile, key) is { Length: > 0 } value ? value : "Не указано";
        UIElement Row(string label, string key) => WorkspaceUi.ValueRow(label, Value(key));
        details.Children.Add(WorkspaceUi.Card("Сотрудник и контакты", "Данные, которые используются в путевом листе и отчёте.",
            Row("ФИО", "employeeFullName"), Row("Должность", "employeeJobTitle"), Row("Компания", "employeeCompany"),
            Row("Адрес", "employeeAddress"), Row("Телефон", "employeePhone")));
        details.Children.Add(WorkspaceUi.Card("Автомобиль и топливо", "Реквизиты автомобиля и настройки расчёта расхода.",
            Row("Модель автомобиля", "carModel"), Row("Госномер", "licensePlate"), Row("Водительское удостоверение", "driverLicenseNumber"),
            Row("Топливная карта", "fuelCardNumber"), Row("Норма расхода, л/100 км", "fuelNorm"),
            WorkspaceUi.ValueRow("Виды топлива", FuelTypes(profile)), Row("Начальный одометр профиля, км", "defaultStartOdometer"),
            Row("Начальный месяц отчётов", "reportStartMonth"),
            WorkspaceUi.ValueRow("Автомобиль в LumaWork", GsmValues.Text(profile, "vehicleId").Length > 0 ? "Связан с профилем" : "Не связан")));
        details.Children.Add(WorkspaceUi.Card("Подписи и проекты", "Имена для подписей и проекты POS / АРМ.",
            Row("Краткое ФИО сотрудника", "employeeShortName"), Row("Подпись сотрудника в отчёте", "employeeReportSignName"),
            Row("ФИО уполномоченного", "authorizedFullName"), Row("Краткое ФИО уполномоченного", "authorizedShortName"),
            WorkspaceUi.ValueRow("Проект POS", Project(profile, "posProjectId")), WorkspaceUi.ValueRow("Проект АРМ", Project(profile, "armProjectId")),
            Row("Код бюджета профиля", "budgetCode")));
    }

    private string Project(JsonObject profile, string key)
    {
        var id = GsmValues.Text(profile, key);
        if (id.Length == 0) return "Не выбран";
        var project = workspace.Projects.FirstOrDefault(p => p.Id == id);
        return project == null ? "Проект выбран · название пока не загружено" : $"{project.Name} · {project.BudgetCode}";
    }

    private static string FuelTypes(JsonObject profile)
    {
        var types = (profile["fuelTypes"] as JsonArray)?.Select(t => t is JsonValue v && v.TryGetValue<string>(out var text) ? text : "")
            .Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? [];
        return types.Length > 0 ? string.Join(", ", types) : GsmValues.Text(profile, "fuelType") is { Length: > 0 } fuel ? fuel : "Не указаны";
    }

    private async Task EditAsync(GsmImportPreview? preview = null)
    {
        var capturedScope = scope();
        var capturedProfile = workspace.ProfileResult;
        var capturedProjects = workspace.Projects;
        var capturedMonth = workspace.Month;
        string? BlockedReason()
        {
            if (capturedScope != scope()) return "Сессия изменилась. Закройте окно и откройте профиль заново.";
            if (!ReferenceEquals(capturedProfile, workspace.ProfileResult) || !ReferenceEquals(capturedProjects, workspace.Projects) || capturedMonth != workspace.Month)
                return "Данные обновились. Закройте окно и откройте профиль заново, чтобы сохранить актуальные реквизиты.";
            if (!online()) return "Предпросмотр доступен без подключения. Для сохранения подключитесь к LumaWork.";
            if (!workspace.ProfileLoaded) return "Текущий профиль ещё не проверен. Обновите профиль перед сохранением, чтобы не перезаписать существующие данные.";
            if (workspace.IsGsmBusy) return "Дождитесь завершения загрузки ГСМ перед сохранением.";
            if (workspace.Projects.Count == 0 || workspace.ProfileResult.AvailableFuelTypes.Count == 0)
                return "Справочники проектов или топлива не загружены. Обновите профиль перед сохранением.";
            return null;
        }
        var draft = await GsmProfileDialog.ShowAsync(XamlRoot, capturedProfile, capturedProjects, capturedMonth, preview, BlockedReason, workspace);
        if (draft == null || capturedScope != scope()) return;
        if (BlockedReason() is { } reason) { localError = reason; Render(); return; }
        await workspace.SaveProfileAsync(draft);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) return;
        dialogOpen = true; localError = null; Render();
        var captured = scope();
        try { await EditAsync(); }
        catch (Exception ex) { if (captured == scope()) localError = WorkspaceUi.Message(ex); }
        finally { dialogOpen = false; Render(); }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) return;
        dialogOpen = true; localError = null; Render();
        var captured = scope();
        try
        {
            var book = await WorkspaceUi.PickAsync(owner, false);
            if (book == null || captured != scope()) return;
            await EditAsync(GsmProfileImporter.Parse(book));
        }
        catch (Exception ex) { if (captured == scope()) localError = WorkspaceUi.Message(ex); }
        finally { dialogOpen = false; Render(); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        localError = null;
        try { await reconnect(); await workspace.RefreshGsmAsync(); }
        catch (Exception ex) { localError = WorkspaceUi.Message(ex); }
        Render();
    }
}
