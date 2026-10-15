using System.Globalization;
using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal static class GsmProfileDialog
{
    internal static readonly (string Key, string Label)[] Fields = [
        ("employeeFullName", "ФИО сотрудника"), ("employeeShortName", "Краткое ФИО сотрудника"), ("employeeReportSignName", "Подпись сотрудника в отчёте"),
        ("authorizedFullName", "ФИО уполномоченного"), ("authorizedShortName", "Краткое ФИО уполномоченного"),
        ("employeeJobTitle", "Должность"), ("employeeCompany", "Компания"), ("employeeAddress", "Адрес"), ("employeePhone", "Телефон"),
        ("driverLicenseNumber", "Водительское удостоверение"), ("fuelCardNumber", "Топливная карта"), ("carModel", "Модель автомобиля"), ("licensePlate", "Госномер"),
        ("fuelNorm", "Норма расхода, л/100 км"), ("defaultStartOdometer", "Начальный одометр профиля, км"), ("reportStartMonth", "Начальный месяц отчётов, YYYY-MM")];

    internal static async Task<JsonObject?> ShowAsync(XamlRoot root, GsmProfileResult loaded, IReadOnlyList<GsmProject> projects,
        string targetMonth, GsmImportPreview? preview = null, Func<string?>? saveBlockedReason = null, WorkspaceController? workspace = null)
    {
        var existing = loaded.Profile;
        var draft = preview?.Merge(existing) ?? (existing == null ? new JsonObject() : (JsonObject)existing.DeepClone());
        if (existing == null)
        {
            draft["reportStartMonth"] = targetMonth;
            if (preview?.SourceMonth == targetMonth && int.TryParse(preview.SourceStartOdometer, out var start)) draft["defaultStartOdometer"] = start;
        }
        var panel = new StackPanel { Spacing = 16, MinWidth = 320, MaxWidth = 700 };
        panel.Children.Add(WorkspaceUi.Text("Проверьте реквизиты. Сохранение обновит профиль ГСМ в LumaWork для остальных клиентов приложения.", true));
        var blocked = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Informational };
        panel.Children.Add(blocked);
        if (preview != null)
        {
            panel.Children.Add(WorkspaceUi.Card("Данные из ГСМ-отчёта", $"Исходный месяц: {preview.SourceMonth ?? "не распознан"}",
                WorkspaceUi.ValueRow("Одометр исходного месяца", $"{Present(preview.SourceStartOdometer)} → {Present(preview.SourceEndOdometer)}"),
                WorkspaceUi.Text("Импортируются только реквизиты. Маршруты и заправки из файла не переносятся.", true)));
            if (preview.Warnings.Count > 0)
                panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Title = "Проверьте данные файла", Message = string.Join("\n", preview.Warnings) });
        }
        var linkedVehicle = existing != null && GsmValues.Text(existing, "vehicleId").Length > 0;
        if (linkedVehicle)
            panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational,
                Message = "Профиль связан с автомобилем. Модель и госномер берутся из автомобиля и здесь не изменяются. Сохранение также обновит его норму расхода, топливо и начальные настройки." });
        var boxes = new Dictionary<string, TextBox>();
        UIElement Field(string key)
        {
            var label = Fields.First(f => f.Key == key).Label;
            var box = new TextBox { Header = label, Text = GsmValues.Text(draft, key), IsReadOnly = (linkedVehicle && key is ("carModel" or "licensePlate")) || (existing != null && key == "defaultStartOdometer") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box,"Profile."+key);
            if (key == "employeePhone") box.PlaceholderText = "+7(XXX)XXX-XX-XX";
            if (key == "reportStartMonth") box.PlaceholderText = "YYYY-MM";
            boxes[key] = box;
            var fieldPanel = new StackPanel { Spacing = 4 };
            fieldPanel.Children.Add(box);
            if (preview?.Fields.FirstOrDefault(f => f.Key == key) is { } field)
            {
                fieldPanel.Children.Add(WorkspaceUi.Text("Источник: " + string.Join("; ", field.Sources)
                    + (field.Conflict ? ". Значения расходятся — введите верное вручную." : field.Derived ? ". Получено из полного ФИО, проверьте." : ""), true));
                if (existing != null && GsmValues.Text(existing, key) is { Length: > 0 } before && before != box.Text)
                    fieldPanel.Children.Add(WorkspaceUi.Text("Сейчас в профиле: " + before, true));
                if (box.IsReadOnly && existing != null)
                {
                    box.Text = GsmValues.Text(existing, key);
                    fieldPanel.Children.Add(WorkspaceUi.Text("Сохраняется текущее значение профиля.", true));
                }
            }
            return fieldPanel;
        }
        var employeeFields = new List<UIElement> { Field("employeeFullName"), Field("employeeJobTitle"), Field("employeeCompany"), Field("employeeAddress"), Field("employeePhone") };
        foreach (var (key, label) in new[] { ("employeeTitleCompany", "Исходная строка: сотрудник, должность, компания"), ("employeeAddressPhone", "Исходная строка: адрес, телефон") })
            if (GsmValues.Text(draft, key) is { Length: > 0 } raw) employeeFields.Add(WorkspaceUi.ValueRow(label, raw));
        panel.Children.Add(WorkspaceUi.Card("Сотрудник и контакты", "Укажите реквизиты полностью, как они должны выглядеть в отчёте.", employeeFields.ToArray()));
        panel.Children.Add(WorkspaceUi.Card("Автомобиль", "Данные путевого листа и топливной карты.",
            Field("carModel"), Field("licensePlate"), Field("driverLicenseNumber"), Field("fuelCardNumber")));
        var fuelPanel = new StackPanel { Spacing = 8 };
        fuelPanel.Children.Add(WorkspaceUi.Text("Виды топлива"));
        var checks = new List<CheckBox>();
        var selected = (draft["fuelTypes"] as JsonArray)?.Select(v => v?.GetValue<string>() ?? "").ToArray()
            ?? (GsmValues.Text(draft, "fuelType") is { Length: > 0 } single ? new[] { single } : []);
        foreach (var type in loaded.AvailableFuelTypes.Concat(selected).Where(t => t.Length > 0).Distinct())
        {
            var check = new CheckBox { Content = loaded.AvailableFuelTypes.Contains(type) ? type : type + " (нет в справочнике)", Tag = type, IsChecked = selected.Contains(type) };
            checks.Add(check); fuelPanel.Children.Add(check);
        }
        if (checks.Count == 0) fuelPanel.Children.Add(WorkspaceUi.Text("Справочник топлива ещё не загружен.", true));
        panel.Children.Add(WorkspaceUi.Card("Топливо и начало отчётов", existing == null
                ? "Задайте начальный месяц и одометр нового профиля."
                : "Начальный одометр существующего профиля сохраняется сервером. Для другого месяца задайте начальный пробег в маршруте.",
            Field("fuelNorm"), fuelPanel, Field("defaultStartOdometer"), Field("reportStartMonth")));
        panel.Children.Add(WorkspaceUi.Card("Подписи", "ФИО и краткие формы для подписей в отчётных документах.",
            Field("employeeShortName"), Field("employeeReportSignName"), Field("authorizedFullName"), Field("authorizedShortName")));
        ComboBox ProjectBox(string header, string key, bool optional)
        {
            var box = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Выберите проект" };
            if (optional) box.Items.Add(new ComboBoxItem { Content = "Не выбран", Tag = "" });
            foreach (var project in projects) box.Items.Add(new ComboBoxItem { Content = $"{project.Name} — {project.BudgetCode}", Tag = project.Id });
            var current = GsmValues.Text(draft, key);
            if (current.Length > 0 && !projects.Any(p => p.Id == current)) box.Items.Add(new ComboBoxItem { Content = "Текущий проект · название не загружено", Tag = current });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == current);
            return box;
        }
        var pos = ProjectBox("Проект POS", "posProjectId", false);
        var arm = ProjectBox("Проект АРМ", "armProjectId", true);
        var projectFields = new List<UIElement> { pos, arm };
        if (preview?.BudgetCodes.Count > 0) projectFields.Add(WorkspaceUi.Text("Бюджеты из файла: " + string.Join(", ", preview.BudgetCodes) + ". Сопоставьте их с проектами выше.", true));
        panel.Children.Add(WorkspaceUi.Card("Проекты", "Проект POS обязателен. Проект АРМ нужен для маршрутов АРМ.", projectFields.ToArray()));
        var error = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Error };
        panel.Children.Add(error);
        var dialog = WorkspaceUi.Dialog(root, preview == null ? "Изменить профиль" : "Проверить реквизиты из отчёта",
            new ScrollViewer { Content = panel, MaxHeight = 560, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, "Сохранить в LumaWork");
        dialog.DefaultButton=ContentDialogButton.Primary;
        void UpdateAvailability()
        {
            var reason = saveBlockedReason?.Invoke();
            dialog.IsPrimaryButtonEnabled = reason == null;
            blocked.Message = reason ?? ""; blocked.IsOpen = reason != null;
        }
        JsonObject? result = null;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (saveBlockedReason?.Invoke() is { } reason)
            {
                error.Message = reason; error.IsOpen = true; e.Cancel = true; UpdateAvailability(); return;
            }
            foreach (var (key, box) in boxes)
            {
                if (box.IsReadOnly) continue;
                var text = box.Text.Trim();
                draft[key] = key is "fuelNorm" or "defaultStartOdometer"
                    ? double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? JsonValue.Create(number) : JsonValue.Create(text)
                    : JsonValue.Create(text);
            }
            // Linked vehicle values and the established odometer come from the current profile.
            foreach (var (key, box) in boxes.Where(pair => pair.Value.IsReadOnly)) draft[key] = existing?[key]?.DeepClone();
            draft["posProjectId"] = (pos.SelectedItem as ComboBoxItem)?.Tag as string;
            var armId = (arm.SelectedItem as ComboBoxItem)?.Tag as string;
            draft["armProjectId"] = string.IsNullOrEmpty(armId) ? null : JsonValue.Create(armId);
            if (projects.FirstOrDefault(p => p.Id == GsmValues.Text(draft, "posProjectId")) is { } project)
            { draft["budgetCode"] = project.BudgetCode; draft["projectName"] = project.Name; }
            var fuels = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
            draft["fuelTypes"] = new JsonArray(fuels.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()); draft["fuelType"] = fuels.FirstOrDefault();
            var errors = GsmProfileValidation.Validate(draft, loaded.AvailableFuelTypes).ToList();
            if (existing == null && GsmValues.Number(draft, "defaultStartOdometer") == null) errors.Add("Укажите начальный одометр нового профиля.");
            if (errors.Count > 0) { error.Message = string.Join("\n", errors); error.IsOpen = true; e.Cancel = true; return; }
            result = (JsonObject)draft.DeepClone();
        };
        UpdateAvailability();
        if (workspace != null) workspace.Changed += UpdateAvailability;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary ? result : null; }
        finally { if (workspace != null) workspace.Changed -= UpdateAvailability; }
    }

    private static string Present(string value) => string.IsNullOrWhiteSpace(value) ? "Не указан" : value;
}
