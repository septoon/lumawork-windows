using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal sealed class ReportWorkspaceView : UserControl
{
    private readonly WorkspaceController workspace;
    private readonly Window owner;
    private readonly Func<string> scope;
    private readonly Func<bool> online;
    private readonly Func<Task> reconnect;
    private readonly Func<string> email;
    private readonly TextBox month;
    private readonly TextBlock profile=WorkspaceUi.Text(""),summary=WorkspaceUi.Text("",true);
    private readonly ListView routes=new(){MaxHeight=280,SelectionMode=ListViewSelectionMode.Single},fuel=new(){MaxHeight=200,SelectionMode=ListViewSelectionMode.Single};
    private readonly TextBlock jobs=WorkspaceUi.Text("",true);
    private readonly InfoBar notice=new(){IsClosable=false};
    private readonly ProgressBar progress=new(){IsIndeterminate=true,Visibility=Visibility.Collapsed};
    private readonly List<Button> writes=[];
    private readonly Button refresh,generate;
    private readonly ContentControl fields;
    internal ReportWorkspaceView(Window owner,WorkspaceController workspace,Func<string> scope,Func<bool> online,Func<Task> reconnect,Func<string> email)
    {
        this.owner=owner;this.workspace=workspace;this.scope=scope;this.online=online;this.reconnect=reconnect;this.email=email;
        var panel=new StackPanel{Spacing=14};
        month=new TextBox{Header="Месяц отчёта",PlaceholderText="YYYY-MM",Text=workspace.Month,MinWidth=150};
        var monthRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};monthRow.Children.Add(month);
        var load=WorkspaceUi.Button("Загрузить месяц",LoadMonth_Click);monthRow.Children.Add(load);
        refresh=WorkspaceUi.Button("Обновить",Refresh_Click);monthRow.Children.Add(refresh);
        panel.Children.Add(monthRow);panel.Children.Add(progress);panel.Children.Add(notice);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var edit=WorkspaceUi.Button("Реквизиты ГСМ",Profile_Click);var import=WorkspaceUi.Button("Импорт ГСМ XLSX",Import_Click);
        writes.Add(edit);writes.Add(import);buttons.Children.Add(edit);buttons.Children.Add(import);panel.Children.Add(buttons);
        panel.Children.Add(profile);panel.Children.Add(summary);
        var routeHeader=new TextBlock{Text="Маршруты месяца",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};panel.Children.Add(routeHeader);
        var rowTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Label}' TextWrapping='Wrap' Margin='0,4'/></DataTemplate>");
        routes.ItemTemplate=rowTemplate;fuel.ItemTemplate=rowTemplate;
        panel.Children.Add(routes);
        var routeButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var add=WorkspaceUi.Button("Добавить маршрут",AddRoute_Click);var change=WorkspaceUi.Button("Изменить выбранный",EditRoute_Click);writes.Add(add);writes.Add(change);
        routeButtons.Children.Add(add);routeButtons.Children.Add(change);panel.Children.Add(routeButtons);
        panel.Children.Add(new TextBlock{Text="Заправки месяца",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});panel.Children.Add(fuel);
        var fuelButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};var addFuel=WorkspaceUi.Button("Добавить заправку",AddFuel_Click);var changeFuel=WorkspaceUi.Button("Изменить заправку",EditFuel_Click);
        writes.Add(addFuel);writes.Add(changeFuel);fuelButtons.Children.Add(addFuel);fuelButtons.Children.Add(changeFuel);panel.Children.Add(fuelButtons);
        generate=WorkspaceUi.Button("Сформировать и отправить отчёт",Generate_Click,true);panel.Children.Add(generate);panel.Children.Add(jobs);
        fields=new ContentControl{Content=panel,HorizontalContentAlignment=HorizontalAlignment.Stretch};Content=fields;
        workspace.Changed+=Render;Render();
    }
    internal void Render()
    {
        var busy=workspace.IsBusy;refresh.IsEnabled=!busy;progress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        foreach(var b in writes)b.IsEnabled=!busy&&online();
        month.IsEnabled=!busy;
        if(month.FocusState==FocusState.Unfocused)month.Text=workspace.Month;
        generate.IsEnabled=!busy&&online()&&workspace.ProfileResult.Profile!=null&&!workspace.GenerationUnknown;
        profile.Text=workspace.ProfileResult.Profile is { } p
            ? $"{GsmValues.Text(p,"employeeFullName")}\n{GsmValues.Text(p,"carModel")}, {GsmValues.Text(p,"licensePlate")} · норма {GsmValues.Text(p,"fuelNorm")} л/100 км"
            : "Профиль ГСМ не заполнен. Импортируйте готовый XLSX или откройте реквизиты.";
        summary.Text=$"{workspace.Month}: маршрутов {workspace.MonthData.Routes.Count}, заправок {workspace.MonthData.Fuel.Count}. Начальный одометр: {workspace.MonthData.StartOdometer?.ToString()??"требует проверки"}."+
            (workspace.LastGsmUpdated is { } at?$" Обновлено {at.ToLocalTime():g}.":"");
        var selected=(routes.SelectedItem as Row)?.Document;
        var key=selected==null?"":GsmValues.Text(selected,"date")+"|"+GsmValues.Text(selected,"workType");
        var list=workspace.MonthData.Routes.Select(r=>new Row(r,$"{GsmValues.Text(r,"date")} · {GsmValues.Text(r,"workType")} · {GsmValues.Text(r,"distanceKm")} км\n"+RouteAddresses(r))).ToArray();
        routes.ItemsSource=list;routes.SelectedItem=list.FirstOrDefault(r=>GsmValues.Text(r.Document,"date")+"|"+GsmValues.Text(r.Document,"workType")==key);
        var fuelKey=(fuel.SelectedItem as Row)?.Document["id"]?.ToString();
        var fuels=workspace.MonthData.Fuel.Select(r=>new Row(r,$"{GsmValues.Text(r,"date")} · {GsmValues.Text(r,"fuelType")} · {GsmValues.Text(r,"liters")} л · {GsmValues.Text(r,"fuelCost")} ₽")).ToArray();
        fuel.ItemsSource=fuels;fuel.SelectedItem=fuels.FirstOrDefault(r=>GsmValues.Text(r.Document,"id")==fuelKey);
        jobs.Text=(workspace.GenerationUnknown?"Отправка временно заблокирована: проверьте историю кнопкой «Обновить».\n":"")+"Последние отчёты:\n"+
            (workspace.Jobs.Count==0?"Нет загруженных заданий.":string.Join("\n",workspace.Jobs.Take(5).Select(j=>$"{GsmValues.Text(j,"month")} — {GsmValues.Text(j,"status")}")));
        notice.Message=workspace.ErrorMessage??workspace.InfoMessage??"";notice.Severity=workspace.ErrorMessage!=null?InfoBarSeverity.Error:InfoBarSeverity.Success;notice.IsOpen=notice.Message.Length>0;
    }
    private static string RouteAddresses(JsonObject route)=>route["stops"] is JsonArray stops?string.Join(" → ",stops.OfType<JsonObject>().Select(s=>GsmValues.Text(s,"address"))):GsmValues.Text(route,"routeSummary");
    private void Error(string message){notice.Message=message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private async void Refresh_Click(object sender,RoutedEventArgs e){await reconnect();await workspace.RefreshGsmAsync();}
    private async void LoadMonth_Click(object sender,RoutedEventArgs e){if(!GsmValues.IsValidMonth(month.Text.Trim())){Error("Укажите месяц в формате YYYY-MM.");return;}await workspace.SelectMonthAsync(month.Text.Trim());}
    private async Task ProfileAsync(GsmImportPreview? preview=null)
    {
        if(workspace.Projects.Count==0||workspace.ProfileResult.AvailableFuelTypes.Count==0){Error("Обновите справочники перед изменением реквизитов.");return;}
        var captured=scope();
        var draft=await GsmProfileDialog.ShowAsync(XamlRoot,workspace.ProfileResult,workspace.Projects,workspace.Month,preview);
        if(captured==scope()&&draft!=null)await workspace.SaveProfileAsync(draft);
    }
    private async void Profile_Click(object sender,RoutedEventArgs e){try{await ProfileAsync();}catch(Exception ex){Error(WorkspaceUi.Message(ex));}}
    private async void Import_Click(object sender,RoutedEventArgs e)
    {
        var captured=scope();
        try{var book=await WorkspaceUi.PickAsync(owner,false);if(book==null||captured!=scope())return;var preview=GsmProfileImporter.Parse(book);await ProfileAsync(preview);}
        catch(Exception ex){if(captured==scope())Error(WorkspaceUi.Message(ex));}
    }
    private async Task RouteAsync(JsonObject? existing)
    {
        var captured=scope();var draft=await GsmRouteDialog.ShowAsync(XamlRoot,existing,workspace.Month,workspace.MonthData.StartOdometer);
        if(captured!=scope()||draft==null)return;
        if(existing==null&&workspace.MonthData.Routes.Any(r=>GsmValues.Text(r,"date")==GsmValues.Text(draft,"date")&&GsmValues.Text(r,"workType")==GsmValues.Text(draft,"workType")))
        {Error("Маршрут на эту дату и тип работ уже есть. Выберите его и нажмите «Изменить выбранный».");return;}
        await workspace.SaveRouteAsync(draft);
    }
    private async void AddRoute_Click(object sender,RoutedEventArgs e){try{await RouteAsync(null);}catch(Exception ex){Error(WorkspaceUi.Message(ex));}}
    private async void EditRoute_Click(object sender,RoutedEventArgs e){if(routes.SelectedItem is not Row row){Error("Выберите маршрут в списке.");return;}try{await RouteAsync(row.Document);}catch(Exception ex){Error(WorkspaceUi.Message(ex));}}
    private async Task RefuelAsync(JsonObject? existing)
    {
        var captured=scope();var types=(workspace.ProfileResult.Profile?["fuelTypes"] as JsonArray)?.Select(n=>n?.GetValue<string>()??"").Where(s=>s.Length>0).ToArray()??[];
        var draft=await GsmRefuelDialog.ShowAsync(XamlRoot,existing,workspace.Month,types);
        if(captured==scope()&&draft!=null)await workspace.SaveRefuelAsync(draft);
    }
    private async void AddFuel_Click(object sender,RoutedEventArgs e){try{await RefuelAsync(null);}catch(Exception ex){Error(WorkspaceUi.Message(ex));}}
    private async void EditFuel_Click(object sender,RoutedEventArgs e){if(fuel.SelectedItem is not Row row){Error("Выберите заправку в списке.");return;}try{await RefuelAsync(row.Document);}catch(Exception ex){Error(WorkspaceUi.Message(ex));}}
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        var captured=scope();
        if(await WorkspaceUi.ConfirmAsync(XamlRoot,"Сформировать ГСМ-отчёт?",$"Месяц: {workspace.Month}.\nМаршрутов: {workspace.MonthData.Routes.Count}, заправок: {workspace.MonthData.Fuel.Count}.\nСервер сформирует XLSX и отправит его на {email()}.","Сформировать и отправить")&&captured==scope())await workspace.GenerateReportAsync();
    }
    private sealed record Row(JsonObject Document,string Label){public override string ToString()=>Label;}
}
