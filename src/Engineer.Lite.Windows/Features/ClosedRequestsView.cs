using Engineer.Core.ClosedRequests;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Engineer.Lite.Windows.Features;

internal sealed class ClosedRequestsView : UserControl
{
    private readonly Window owner;private readonly WorkspaceController workspace;private readonly Func<string> scope;private readonly Func<bool> online;private readonly Func<Task> reconnect;
    private readonly ListView list=new(){MaxHeight=420,SelectionMode=ListViewSelectionMode.Single};
    private readonly TextBox search=new(){PlaceholderText="Поиск по номеру или адресу"};
    private readonly TextBlock summary=WorkspaceUi.Text("",true);
    private readonly InfoBar notice=new(){IsClosable=false};
    private readonly ProgressBar progress=new(){IsIndeterminate=true,Visibility=Visibility.Collapsed};
    private readonly Button import,refresh,add,copy,cancel;private readonly TextBlock empty=WorkspaceUi.Text("",true),loading=WorkspaceUi.Text("",true);private string previousScope="";private bool dialogOpen;private string? localError;
    internal ContentControl ConnectionHost {get;}=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    internal ClosedRequestsView(Window owner,WorkspaceController workspace,Func<string> scope,Func<bool> online,Func<Task> reconnect)
    {
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        this.owner=owner;this.workspace=workspace;this.scope=scope;this.online=online;this.reconnect=reconnect;
        var panel=new StackPanel{Spacing=16};
        import=WorkspaceUi.Button("Выбрать XLSX с заявками",Import_Click,true);
        panel.Children.Add(WorkspaceUi.Card("Загрузка из Excel","1. В SimpleOne откройте свои закрытые заявки и экспортируйте список в Excel.\n2. Выберите полученный .xlsx здесь. Проверьте предпросмотр и подтвердите импорт.\nВход в SimpleOne в приложении для этого не требуется.",import));
        refresh=WorkspaceUi.Button("Обновить из SimpleOne",Refresh_Click);cancel=WorkspaceUi.Button("Остановить загрузку",(_,_)=>workspace.CancelClosed());
        var sync=new StackPanel{Spacing=12};sync.Children.Add(ConnectionHost);sync.Children.Add(WorkspaceUi.Text("После подключения обновляем заявки каждые 5 минут.",true));sync.Children.Add(refresh);
        var connection=new Expander{Header="Автоматическое обновление из SimpleOne",Content=sync,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(connection,"SimpleOneConnection");
        panel.Children.Add(connection);panel.Children.Add(progress);panel.Children.Add(loading);panel.Children.Add(cancel);
        panel.Children.Add(notice);
        var archive=new StackPanel{Spacing=12};archive.Children.Add(summary);archive.Children.Add(search);archive.Children.Add(empty);
        search.TextChanged+=(_,_)=>RenderList();
        var header=new Grid{ColumnSpacing=16};header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(150)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var numberHeader=WorkspaceUi.Text("Номер заявки",true);var addressHeader=WorkspaceUi.Text("Адрес",true);Grid.SetColumn(addressHeader,1);header.Children.Add(numberHeader);header.Children.Add(addressHeader);archive.Children.Add(header);
        list.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid ColumnSpacing='16' Padding='0,6'><Grid.ColumnDefinitions><ColumnDefinition Width='150'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Number}'/><TextBlock Grid.Column='1' Text='{Binding Address}' TextWrapping='Wrap'/></Grid></DataTemplate>");
        archive.Children.Add(list);var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        copy=WorkspaceUi.Button("Копировать адрес",Copy_Click);actions.Children.Add(copy);add=WorkspaceUi.Button("Добавить в маршрут",AddRoute_Click);actions.Children.Add(add);archive.Children.Add(actions);
        panel.Children.Add(WorkspaceUi.Card("Закрытые заявки","Выберите строку, чтобы скопировать адрес или добавить его в маршрут отчётного месяца.",archive));
        list.SelectionChanged+=(_,_)=>RenderActions();
        Content=panel;workspace.Changed+=Render;Render();
    }
    internal void Render()
    {
        if(previousScope!=scope()){previousScope=scope();localError=null;search.Text="";}
        import.IsEnabled=!dialogOpen;refresh.IsEnabled=!workspace.IsClosedBusy;cancel.Visibility=workspace.IsClosedBusy?Visibility.Visible:Visibility.Collapsed;progress.Visibility=workspace.IsClosedBusy?Visibility.Visible:Visibility.Collapsed;loading.Text=workspace.ClosedLoadingMessage??"";loading.Visibility=workspace.IsClosedBusy?Visibility.Visible:Visibility.Collapsed;
        summary.Text=$"В архиве: {workspace.ClosedRequests.Count}."+(workspace.LastClosedUpdated is { } at?$" Обновлено {at.ToLocalTime():g}.":"")+" Поиск по номеру и адресу.";
        notice.Message=localError??workspace.ClosedErrorMessage??workspace.ClosedInfoMessage??"";notice.Severity=localError!=null||workspace.ClosedErrorMessage!=null?InfoBarSeverity.Error:InfoBarSeverity.Success;notice.IsOpen=notice.Message.Length>0;
        RenderList();
    }
    private void RenderList()
    {
        var selected=(list.SelectedItem as ClosedRequest)?.Number;var query=search.Text.Trim();
        var records=workspace.ClosedRequests.Where(r=>query.Length==0||r.Number.Contains(query,StringComparison.OrdinalIgnoreCase)||r.Address.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        list.ItemsSource=records;list.SelectedItem=records.FirstOrDefault(r=>r.Number==selected);empty.Text=query.Length>0?"Ничего не найдено. Измените запрос.":"Архив пуст. Загрузите файл Excel или подключите SimpleOne выше.";empty.Visibility=records.Length==0?Visibility.Visible:Visibility.Collapsed;RenderActions();
    }
    private void RenderActions(){var selected=list.SelectedItem is ClosedRequest{Address.Length:>0};copy.IsEnabled=selected;add.IsEnabled=selected&&online()&&!workspace.IsGsmBusy&&workspace.MonthLoaded;}
    private void Error(string message){localError=message;notice.Message=message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private async void Refresh_Click(object sender,RoutedEventArgs e){localError=null;await reconnect();await workspace.RefreshClosedAsync();}
    private async void Import_Click(object sender,RoutedEventArgs e)
    {
        if(dialogOpen)return;localError=null;dialogOpen=true;Render();var captured=scope();
        try
        {
            var book=await WorkspaceUi.PickAsync(owner,true);if(book==null||captured!=scope())return;
            var preview=ClosedRequestsImporter.Parse(book);
            var previewText=$"Принято закрытых заявок: {preview.Records.Count}.\nИсключено: {preview.RejectedCount}. Без адреса: {preview.MissingAddressCount}.\n"+
                "Проверьте, что это ваша выгрузка. Записи добавятся в текущий архив; отсутствующие строки не удаляются.\n\n"+
                string.Join("\n",preview.Records.Take(5).Select(r=>$"{r.Number} — {r.Address}"));
            if(await WorkspaceUi.ConfirmAsync(XamlRoot,"Импортировать заявки?",previewText,"Импортировать")&&captured==scope())await workspace.ImportClosedAsync(preview);
        }
        catch(Exception ex){if(captured==scope())Error(WorkspaceUi.Message(ex));}
        finally{dialogOpen=false;Render();}
    }
    private void Copy_Click(object sender,RoutedEventArgs e)
    {
        if(list.SelectedItem is not ClosedRequest request){Error("Выберите заявку в списке.");return;}
        var data=new DataPackage();data.SetText(request.Address);Clipboard.SetContent(data);
    }
    private async void AddRoute_Click(object sender,RoutedEventArgs e)
    {
        if(list.SelectedItem is not ClosedRequest request||string.IsNullOrWhiteSpace(request.Address)){Error("Выберите заявку с адресом.");return;}
        var captured=scope();
        try
        {
            System.Text.Json.Nodes.JsonObject? chosen=null;
            if(workspace.MonthData.Routes.Count>0)
            {
                var options=new ComboBox{Header="Маршрут",HorizontalAlignment=HorizontalAlignment.Stretch};
                options.Items.Add(new ComboBoxItem{Content="Новый маршрут"});
                foreach(var route in workspace.MonthData.Routes)options.Items.Add(new ComboBoxItem{Content=GsmValues.Text(route,"date")+" — "+GsmValues.Text(route,"workType"),Tag=route});
                options.SelectedIndex=0;
                if(await WorkspaceUi.Dialog(XamlRoot,"Куда добавить адрес?",options,"Открыть маршрут").ShowAsync()!=ContentDialogResult.Primary||captured!=scope())return;
                chosen=(options.SelectedItem as ComboBoxItem)?.Tag as System.Text.Json.Nodes.JsonObject;
            }
            var draft=await GsmRouteDialog.ShowAsync(XamlRoot,chosen,workspace.Month,workspace.MonthData.StartOdometer,request);
            if(draft==null||captured!=scope())return;
            var existing=workspace.MonthData.Routes.FirstOrDefault(r=>GsmValues.Text(r,"date")==GsmValues.Text(draft,"date")&&GsmValues.Text(r,"workType")==GsmValues.Text(draft,"workType"));
            if(chosen==null&&existing!=null){Error("На эту дату уже есть маршрут. Добавьте адрес через редактирование маршрута на главной, чтобы не заменить прежние остановки.");return;}
            await workspace.SaveRouteAsync(draft);
        }
        catch(Exception ex){if(captured==scope())Error(WorkspaceUi.Message(ex));}
    }
}
