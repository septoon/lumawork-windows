using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal sealed class ReportWorkspaceView : UserControl
{
    private readonly WorkspaceController workspace;
    private readonly Func<string> scope;
    private readonly Func<bool> online;
    private readonly Func<Task> reconnect;
    private readonly Func<string> email;
    private readonly ComboBox monthPicker=new(){Header="Месяц",MinWidth=170};
    private readonly NumberBox year=new(){Header="Год",Minimum=2000,Maximum=2100,SmallChange=1,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,Width=140};
    private string renderedMonth="";
    private readonly TextBlock profile=WorkspaceUi.Text(""),summary=WorkspaceUi.Text("",true),routeEmpty=WorkspaceUi.Text("",true),fuelEmpty=WorkspaceUi.Text("",true);
    private readonly ListView routes=new(){MaxHeight=320,SelectionMode=ListViewSelectionMode.Single},fuel=new(){MaxHeight=240,SelectionMode=ListViewSelectionMode.Single};
    private readonly TextBlock jobs=WorkspaceUi.Text("",true),loading=WorkspaceUi.Text("",true);
    private readonly InfoBar notice=new(){IsClosable=false};
    private readonly ProgressBar progress=new(){IsIndeterminate=true,Visibility=Visibility.Collapsed};
    private readonly Button refresh,generate,editRoute,editFuel,addRoute,addFuel,cancel,load;
    internal ReportWorkspaceView(Window owner,WorkspaceController workspace,Func<string> scope,Func<bool> online,Func<Task> reconnect,Func<string> email,Action openProfile)
    {
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        this.workspace=workspace;this.scope=scope;this.online=online;this.reconnect=reconnect;this.email=email;
        var panel=new StackPanel{Spacing=16};
        var months=new[]{"Январь","Февраль","Март","Апрель","Май","Июнь","Июль","Август","Сентябрь","Октябрь","Ноябрь","Декабрь"};
        foreach(var name in months)monthPicker.Items.Add(name);
        var period=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Bottom};period.Children.Add(monthPicker);period.Children.Add(year);
        load=WorkspaceUi.Button("Показать",LoadMonth_Click);load.VerticalAlignment=VerticalAlignment.Bottom;period.Children.Add(load);
        refresh=WorkspaceUi.Button("Обновить данные",Refresh_Click);refresh.VerticalAlignment=VerticalAlignment.Bottom;period.Children.Add(refresh);
        cancel=WorkspaceUi.Button("Остановить загрузку",(_,_)=>workspace.CancelGsm());
        panel.Children.Add(WorkspaceUi.Card("Период отчёта","Выберите месяц. Маршруты и заправки загружаются из LumaWork.",period,progress,loading,cancel,notice,summary));
        panel.Children.Add(WorkspaceUi.Card("Профиль для отчёта","Реквизиты редактируются в разделе «Профиль».",profile,WorkspaceUi.Button("Открыть профиль",(_,_)=>openProfile())));
        var rowTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Label}' TextWrapping='Wrap' Margin='0,8'/></DataTemplate>");
        routes.ItemTemplate=rowTemplate;fuel.ItemTemplate=rowTemplate;
        addRoute=WorkspaceUi.Button("Добавить маршрут",AddRoute_Click);editRoute=WorkspaceUi.Button("Изменить маршрут",EditRoute_Click);
        var routeActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};routeActions.Children.Add(addRoute);routeActions.Children.Add(editRoute);
        panel.Children.Add(WorkspaceUi.Card("Маршруты","Выберите строку для редактирования. Адреса закрытых заявок можно добавить из раздела «Заявки».",routeActions,routeEmpty,routes));
        addFuel=WorkspaceUi.Button("Добавить заправку",AddFuel_Click);editFuel=WorkspaceUi.Button("Изменить заправку",EditFuel_Click);
        var fuelActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};fuelActions.Children.Add(addFuel);fuelActions.Children.Add(editFuel);
        panel.Children.Add(WorkspaceUi.Card("Заправки","Объём, стоимость и вид топлива за выбранный месяц.",fuelActions,fuelEmpty,fuel));
        generate=WorkspaceUi.Button("Сформировать и отправить отчёт",Generate_Click,true);
        panel.Children.Add(WorkspaceUi.Card("Готовый XLSX","После подтверждения LumaWork сформирует отчёт и отправит его на вашу почту.",generate,jobs));
        Content=panel;routes.SelectionChanged+=(_,_)=>RenderActions();fuel.SelectionChanged+=(_,_)=>RenderActions();workspace.Changed+=Render;Render();
    }
    private void RenderActions()
    {
        var canWrite=!workspace.IsGsmBusy&&online()&&workspace.MonthLoaded;
        addRoute.IsEnabled=addFuel.IsEnabled=canWrite;
        editRoute.IsEnabled=canWrite&&routes.SelectedItem!=null;editFuel.IsEnabled=canWrite&&fuel.SelectedItem!=null;
    }
    internal void Render()
    {
        var busy=workspace.IsGsmBusy;refresh.IsEnabled=!busy;load.IsEnabled=!busy;
        progress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;loading.Text=workspace.GsmLoadingMessage??"";
        loading.Visibility=busy?Visibility.Visible:Visibility.Collapsed;cancel.Visibility=busy&&!workspace.IsGsmMutation?Visibility.Visible:Visibility.Collapsed;
        if(renderedMonth!=workspace.Month){renderedMonth=workspace.Month;year.Value=int.Parse(workspace.Month[..4]);monthPicker.SelectedIndex=int.Parse(workspace.Month[5..])-1;}
        monthPicker.IsEnabled=year.IsEnabled=!workspace.IsGsmMutation;
        generate.IsEnabled=!busy&&online()&&workspace.MonthLoaded&&workspace.ProfileResult.Profile!=null&&!workspace.GenerationUnknown;
        profile.Text=workspace.ProfileResult.Profile is { } p?$"{GsmValues.Text(p,"employeeFullName")} · {GsmValues.Text(p,"carModel")} · {GsmValues.Text(p,"licensePlate")}":workspace.ProfileLoaded?"Реквизиты ещё не заполнены. Откройте профиль и загрузите прошлый ГСМ-отчёт или заполните поля вручную.":"Профиль ещё не загружен. Проверьте подключение в разделе «Профиль».";
        summary.Text=workspace.MonthLoaded?$"{workspace.Month}: маршрутов — {workspace.MonthData.Routes.Count}, заправок — {workspace.MonthData.Fuel.Count}. Начальный одометр: {workspace.MonthData.StartOdometer?.ToString()??"не задан"} км.":"Данные месяца ещё не загружены полностью. Отправка отчёта станет доступна после загрузки.";
        var selected=(routes.SelectedItem as Row)?.Document;var key=selected==null?"":GsmValues.Text(selected,"date")+"|"+GsmValues.Text(selected,"workType");
        var list=workspace.MonthData.Routes.Select(r=>new Row(r,$"{GsmValues.Text(r,"date")} · {GsmValues.Text(r,"workType")} · {GsmValues.Text(r,"distanceKm")} км\n"+RouteAddresses(r))).ToArray();
        routes.ItemsSource=list;routes.SelectedItem=list.FirstOrDefault(r=>GsmValues.Text(r.Document,"date")+"|"+GsmValues.Text(r.Document,"workType")==key);
        var fuelKey=(fuel.SelectedItem as Row)?.Document["id"]?.ToString();
        var fuels=workspace.MonthData.Fuel.Select(r=>new Row(r,$"{GsmValues.Text(r,"date")} · {GsmValues.Text(r,"fuelType")} · {GsmValues.Text(r,"liters")} л · {GsmValues.Text(r,"fuelCost")} ₽")).ToArray();fuel.ItemsSource=fuels;fuel.SelectedItem=fuels.FirstOrDefault(r=>GsmValues.Text(r.Document,"id")==fuelKey);
        routeEmpty.Text=workspace.MonthLoaded?"За этот месяц маршрутов нет.":"Ожидаем данные маршрутов…";routeEmpty.Visibility=list.Length==0?Visibility.Visible:Visibility.Collapsed;
        fuelEmpty.Text=workspace.MonthLoaded?"За этот месяц заправок нет.":"Ожидаем данные заправок…";fuelEmpty.Visibility=fuels.Length==0?Visibility.Visible:Visibility.Collapsed;
        jobs.Text=(workspace.GenerationUnknown?"Сначала обновите данные: результат предыдущей отправки ещё не проверен.\n":"")+"История отправок\n"+(workspace.Jobs.Count==0?"Отправок пока нет.":string.Join("\n",workspace.Jobs.Take(5).Select(j=>$"{GsmValues.Text(j,"month")} — {GsmValues.Text(j,"status")}")));
        notice.Message=workspace.GsmErrorMessage??workspace.GsmInfoMessage??"";notice.Severity=workspace.GsmErrorMessage!=null?InfoBarSeverity.Error:InfoBarSeverity.Success;notice.IsOpen=notice.Message.Length>0;RenderActions();
    }
    private static string RouteAddresses(JsonObject route)=>route["stops"] is JsonArray stops?string.Join(" → ",stops.OfType<JsonObject>().Select(s=>GsmValues.Text(s,"address"))):GsmValues.Text(route,"routeSummary");
    private void Error(string message){notice.Message=message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private async void Refresh_Click(object sender,RoutedEventArgs e){await reconnect();await workspace.RefreshGsmAsync();}
    private async void LoadMonth_Click(object sender,RoutedEventArgs e)
    {
        if(monthPicker.SelectedIndex<0||double.IsNaN(year.Value))return;
        await workspace.SelectMonthAsync($"{(int)year.Value:D4}-{monthPicker.SelectedIndex+1:D2}");
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
