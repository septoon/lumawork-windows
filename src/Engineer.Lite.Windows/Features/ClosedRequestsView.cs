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
    private readonly Button import,refresh,add;private string previousScope="";
    internal ClosedRequestsView(Window owner,WorkspaceController workspace,Func<string> scope,Func<bool> online,Func<Task> reconnect)
    {
        this.owner=owner;this.workspace=workspace;this.scope=scope;this.online=online;this.reconnect=reconnect;
        var panel=new StackPanel{Spacing=12};var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        import=WorkspaceUi.Button("Импорт заявок XLSX",Import_Click);refresh=WorkspaceUi.Button("Обновить",Refresh_Click);toolbar.Children.Add(import);toolbar.Children.Add(refresh);panel.Children.Add(toolbar);
        panel.Children.Add(progress);panel.Children.Add(notice);panel.Children.Add(summary);panel.Children.Add(search);
        search.TextChanged+=(_,_)=>RenderList();
        panel.Children.Add(new TextBlock{Text="Номер заявки  ·  Адрес",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        list.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid ColumnSpacing='16' Padding='0,6'><Grid.ColumnDefinitions><ColumnDefinition Width='150'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Number}'/><TextBlock Grid.Column='1' Text='{Binding Address}' TextWrapping='Wrap'/></Grid></DataTemplate>");
        panel.Children.Add(list);var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        actions.Children.Add(WorkspaceUi.Button("Копировать адрес",Copy_Click));add=WorkspaceUi.Button("Добавить в маршрут",AddRoute_Click);actions.Children.Add(add);panel.Children.Add(actions);
        Content=panel;workspace.Changed+=Render;Render();
    }
    internal void Render()
    {
        if(previousScope!=scope()){previousScope=scope();search.Text="";}
        import.IsEnabled=!workspace.IsBusy;refresh.IsEnabled=!workspace.IsBusy;add.IsEnabled=!workspace.IsBusy&&online();progress.Visibility=workspace.IsBusy?Visibility.Visible:Visibility.Collapsed;
        summary.Text=$"В архиве: {workspace.ClosedRequests.Count}."+(workspace.LastClosedUpdated is { } at?$" Обновлено {at.ToLocalTime():g}.":"")+" Поиск идёт по всему загруженному архиву; импорт не гарантирует полноту истории.";
        notice.Message=workspace.ErrorMessage??workspace.InfoMessage??"";notice.Severity=workspace.ErrorMessage!=null?InfoBarSeverity.Error:InfoBarSeverity.Success;notice.IsOpen=notice.Message.Length>0;
        RenderList();
    }
    private void RenderList()
    {
        var selected=(list.SelectedItem as ClosedRequest)?.Number;var query=search.Text.Trim();
        var records=workspace.ClosedRequests.Where(r=>query.Length==0||r.Number.Contains(query,StringComparison.OrdinalIgnoreCase)||r.Address.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        list.ItemsSource=records;list.SelectedItem=records.FirstOrDefault(r=>r.Number==selected);
    }
    private void Error(string message){notice.Message=message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private async void Refresh_Click(object sender,RoutedEventArgs e){await reconnect();await workspace.RefreshClosedAsync();}
    private async void Import_Click(object sender,RoutedEventArgs e)
    {
        var captured=scope();
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
