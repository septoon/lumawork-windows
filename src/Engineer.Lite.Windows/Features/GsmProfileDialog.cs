using System.Globalization;
using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal static class GsmProfileDialog
{
    internal static readonly (string Key,string Label)[] Fields=[
        ("employeeFullName","ФИО сотрудника"),("employeeShortName","Краткое ФИО сотрудника"),("employeeReportSignName","Подпись сотрудника в отчёте"),
        ("authorizedFullName","ФИО уполномоченного"),("authorizedShortName","Краткое ФИО уполномоченного"),
        ("employeeJobTitle","Должность"),("employeeCompany","Компания"),("employeeAddress","Адрес"),("employeePhone","Телефон"),
        ("driverLicenseNumber","Водительское удостоверение"),("fuelCardNumber","Топливная карта"),("carModel","Модель автомобиля"),("licensePlate","Госномер"),
        ("fuelNorm","Норма расхода, л/100 км"),("defaultStartOdometer","Начальный одометр профиля, км"),("reportStartMonth","Начальный месяц ведения отчётов, YYYY-MM")];
    internal static async Task<JsonObject?> ShowAsync(XamlRoot root,GsmProfileResult loaded,IReadOnlyList<GsmProject> projects,string targetMonth,GsmImportPreview? preview=null)
    {
        var existing=loaded.Profile;var draft=preview?.Merge(existing)??(existing==null?new JsonObject():(JsonObject)existing.DeepClone());
        if(existing==null){draft["reportStartMonth"]=targetMonth;if(preview?.SourceMonth==targetMonth&&int.TryParse(preview.SourceStartOdometer,out var start))draft["defaultStartOdometer"]=start;}
        var panel=new StackPanel{Spacing=12,MinWidth=360,MaxWidth=700};
        panel.Children.Add(WorkspaceUi.Text("Проверьте реквизиты. Сохранение обновит профиль ГСМ в LumaWork для остальных клиентов приложения."));
        if(preview!=null)
        {
            panel.Children.Add(WorkspaceUi.Text($"Файл за {preview.SourceMonth??"неизвестный месяц"}. Одометр исходного месяца: {preview.SourceStartOdometer} → {preview.SourceEndOdometer}. Маршруты и заправки не переносятся.",true));
            if(preview.Warnings.Count>0)panel.Children.Add(new InfoBar{IsOpen=true,IsClosable=false,Severity=InfoBarSeverity.Warning,Message=string.Join("\n",preview.Warnings)});
        }
        if(existing!=null&&GsmValues.Text(existing,"vehicleId").Length>0)
            panel.Children.Add(new InfoBar{IsOpen=true,IsClosable=false,Severity=InfoBarSeverity.Warning,Message="Профиль связан с автомобилем. Сервер возьмёт его модель и госномер; норма, топливо и начальные настройки автомобиля также будут обновлены. Связь с автомобилем сохраняется."});
        var boxes=new Dictionary<string,TextBox>();
        foreach(var (key,label) in Fields)
        {
            var box=new TextBox{Header=label,Text=GsmValues.Text(draft,key)};boxes[key]=box;panel.Children.Add(box);
            if(preview?.Fields.FirstOrDefault(f=>f.Key==key) is { } field)
            {
                panel.Children.Add(WorkspaceUi.Text("Источник: "+string.Join("; ",field.Sources)+(field.Conflict?". Значения расходятся — выберите вручную.":field.Derived?". Получено из полного ФИО, проверьте.":""),true));
                if(existing!=null&&GsmValues.Text(existing,key) is { Length:>0 } before&&before!=box.Text)panel.Children.Add(WorkspaceUi.Text("Сейчас на сервере: "+before,true));
            }
        }
        if(existing!=null)panel.Children.Add(WorkspaceUi.Text("Сервер сохраняет существующий начальный одометр профиля. Для другого месяца начальный пробег задаётся в маршруте.",true));
        ComboBox ProjectBox(string header,string key,bool optional)
        {
            var box=new ComboBox{Header=header,HorizontalAlignment=HorizontalAlignment.Stretch};
            if(optional)box.Items.Add(new ComboBoxItem{Content="Не выбран",Tag=""});
            foreach(var project in projects)box.Items.Add(new ComboBoxItem{Content=$"{project.Name} — {project.BudgetCode}",Tag=project.Id});
            var current=GsmValues.Text(draft,key);
            if(current.Length>0&&!projects.Any(p=>p.Id==current))box.Items.Add(new ComboBoxItem{Content="Текущий выбранный проект",Tag=current});
            box.SelectedItem=box.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>(string?)i.Tag==current);
            return box;
        }
        var pos=ProjectBox("Проект POS","posProjectId",false);var arm=ProjectBox("Проект АРМ","armProjectId",true);panel.Children.Add(pos);panel.Children.Add(arm);
        if(preview?.BudgetCodes.Count>0)panel.Children.Add(WorkspaceUi.Text("Бюджеты из исходного отчёта: "+string.Join(", ",preview.BudgetCodes)+". Сопоставьте со справочником выше.",true));
        panel.Children.Add(WorkspaceUi.Text("Виды топлива"));var checks=new List<CheckBox>();
        var selected=(draft["fuelTypes"] as JsonArray)?.Select(v=>v?.GetValue<string>()??"").ToArray()??[];
        foreach(var type in loaded.AvailableFuelTypes){var check=new CheckBox{Content=type,IsChecked=selected.Contains(type)};checks.Add(check);panel.Children.Add(check);}
        var error=new InfoBar{IsClosable=false,Severity=InfoBarSeverity.Error};panel.Children.Add(error);
        var dialog=WorkspaceUi.Dialog(root,preview==null?"Реквизиты ГСМ":"Импорт реквизитов ГСМ",new ScrollViewer{Content=panel,MaxHeight=560,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},"Сохранить в LumaWork");
        JsonObject? result=null;
        dialog.PrimaryButtonClick+=(_,e)=>
        {
            foreach(var (key,box) in boxes)
            {
                var text=box.Text.Trim();
                if(key is "fuelNorm" or "defaultStartOdometer")
                    draft[key]=double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)?JsonValue.Create(number):JsonValue.Create(text);
                else draft[key]=text;
            }
            draft["posProjectId"]=(pos.SelectedItem as ComboBoxItem)?.Tag as string;
            var armId=(arm.SelectedItem as ComboBoxItem)?.Tag as string;draft["armProjectId"]=string.IsNullOrEmpty(armId)?null:JsonValue.Create(armId);
            if(projects.FirstOrDefault(p=>p.Id==GsmValues.Text(draft,"posProjectId")) is { } project){draft["budgetCode"]=project.BudgetCode;draft["projectName"]=project.Name;}
            var fuels=checks.Where(c=>c.IsChecked==true).Select(c=>c.Content.ToString()!).ToArray();
            draft["fuelTypes"]=new JsonArray(fuels.Select(f=>(JsonNode?)JsonValue.Create(f)).ToArray());draft["fuelType"]=fuels.FirstOrDefault();
            var errors=GsmProfileValidation.Validate(draft,loaded.AvailableFuelTypes).ToList();
            if(existing==null&&(GsmValues.Number(draft,"defaultStartOdometer")==null))errors.Add("Укажите начальный одометр нового профиля.");
            if(errors.Count>0){error.Message=string.Join("\n",errors);error.IsOpen=true;e.Cancel=true;return;}
            result=(JsonObject)draft.DeepClone();
        };
        return await dialog.ShowAsync()==ContentDialogResult.Primary?result:null;
    }
}
