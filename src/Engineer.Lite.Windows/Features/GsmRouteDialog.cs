using System.Globalization;
using System.Text.Json.Nodes;
using Engineer.Core.ClosedRequests;
using Engineer.Core.Gsm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal static class GsmRouteDialog
{
    internal static async Task<JsonObject?> ShowAsync(XamlRoot root,JsonObject? existing,string month,int? odometer,ClosedRequest? request=null)
    {
        var source=existing==null?new JsonObject():(JsonObject)existing.DeepClone();var isNew=existing==null;
        var panel=new StackPanel{Spacing=12,MinWidth=360,MaxWidth=700};
        panel.Children.Add(WorkspaceUi.Text("Укажите дату и фактический пробег поездки. Дата закрытия заявки не заменяет дату поездки. Добавьте адреса начала и окончания маршрута.",true));
        var dateText=isNew?month+"-01":GsmValues.Text(source,"date");
        var date=new CalendarDatePicker{Header="Дата поездки",Date=DateTimeOffset.Parse(dateText,CultureInfo.InvariantCulture),IsEnabled=isNew};
        var work=new ComboBox{Header="Тип работ",IsEnabled=isNew};work.Items.Add("POS");work.Items.Add("ARM");work.SelectedItem=isNew?"POS":GsmValues.Text(source,"workType");
        var distance=new TextBox{Header="Пробег за день, км",Text=isNew?"":GsmValues.Text(source,"distanceKm")};
        var start=new TextBox{Header="Начальный одометр месяца, км (если нужно задать)",Text=isNew?"":GsmValues.Text(source,"periodStartOdometer")};
        panel.Children.Add(date);panel.Children.Add(work);panel.Children.Add(distance);panel.Children.Add(start);
        if(odometer!=null)panel.Children.Add(WorkspaceUi.Text($"Подсказка сервера для {month}: {odometer} км. Не переносите пробег из другого месяца без проверки.",true));
        var stops=new StackPanel{Spacing=12};panel.Children.Add(stops);
        var rows=new List<(TextBox Address,TextBox Number,ComboBox Status,StackPanel Container)>();
        void Add(string address,string number,string status,bool removable)
        {
            var row=new StackPanel{Spacing=6};var a=new TextBox{Header="Адрес",Text=address,TextWrapping=TextWrapping.Wrap,AcceptsReturn=false};var n=new TextBox{Header="Номер заявки (необязательно)",Text=number};
            var s=new ComboBox{Header="Состояние"};foreach(var value in new[]{"Выполнена","В процессе","Отказ"})s.Items.Add(value);
            if(!s.Items.Contains(status))s.Items.Add(status);s.SelectedItem=status;
            row.Children.Add(a);row.Children.Add(n);row.Children.Add(s);
            rows.Add((a,n,s,row));stops.Children.Add(row);
            if(removable)row.Children.Add(WorkspaceUi.Button("Убрать добавленный адрес",(_,_)=>{var at=rows.FindIndex(r=>r.Container==row);if(at>=0)rows.RemoveAt(at);stops.Children.Remove(row);}));
        }
        if(source["stops"] is JsonArray saved)
            foreach(var stop in saved.OfType<JsonObject>())Add(GsmValues.Text(stop,"address"),GsmValues.Text(stop,"requestNumber"),GsmValues.Text(stop,"status"),false);
        if(request!=null)Add(request.Address,request.Number,"Выполнена",true);
        if(rows.Count==0)Add("","","Выполнена",true);
        panel.Children.Add(WorkspaceUi.Button("Добавить адрес",(_,_)=>Add("","","Выполнена",true)));
        var error=new InfoBar{IsClosable=false,Severity=InfoBarSeverity.Error};panel.Children.Add(error);
        var dialog=WorkspaceUi.Dialog(root,isNew?"Новый маршрут":"Изменить маршрут",new ScrollViewer{Content=panel,MaxHeight=560,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},"Сохранить маршрут");
        JsonObject? result=null;
        dialog.PrimaryButtonClick+=(_,e)=>
        {
            try
            {
                if(date.Date==null||work.SelectedItem==null)throw new ArgumentException("Выберите дату и тип работ.");
                var selected=date.Date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                if(!selected.StartsWith(month,StringComparison.Ordinal))throw new ArgumentException("Дата должна входить в выбранный месяц.");
                if(!int.TryParse(distance.Text.Trim(),out var km)||km<0)throw new ArgumentException("Укажите целый неотрицательный пробег.");
                int? initial=null;
                if(start.Text.Trim().Length>0){if(!int.TryParse(start.Text.Trim(),out var value)||value<0)throw new ArgumentException("Проверьте начальный одометр.");initial=value;}
                if(rows.Count==0||rows.Any(r=>string.IsNullOrWhiteSpace(r.Address.Text)))throw new ArgumentException("Добавьте и заполните адреса маршрута.");
                result=GsmRouteDraft.Apply(source,selected,(string)work.SelectedItem,km,initial,rows.Select(r=>(r.Address.Text,r.Number.Text,(string)r.Status.SelectedItem)).ToArray());
            }
            catch(ArgumentException ex){error.Message=ex.Message;error.IsOpen=true;e.Cancel=true;}
        };
        return await dialog.ShowAsync()==ContentDialogResult.Primary?result:null;
    }
}
