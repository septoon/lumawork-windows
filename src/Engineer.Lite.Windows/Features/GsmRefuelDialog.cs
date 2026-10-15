using System.Globalization;
using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Engineer.Lite.Windows.Features;

internal static class GsmRefuelDialog
{
    internal static async Task<JsonObject?> ShowAsync(XamlRoot root,JsonObject? existing,string month,IReadOnlyList<string> fuelTypes)
    {
        var panel=new StackPanel{Spacing=12,MinWidth=360};var draft=existing==null?new JsonObject():(JsonObject)existing.DeepClone();
        var dateText=existing==null?month+"-01":GsmValues.Text(draft,"date");
        var date=new CalendarDatePicker{Header="Дата заправки",Date=DateTimeOffset.Parse(dateText,CultureInfo.InvariantCulture)};
        var type=new ComboBox{Header="Вид топлива"};foreach(var fuel in fuelTypes)type.Items.Add(fuel);
        var selected=GsmValues.Text(draft,"fuelType");if(selected.Length>0&&!type.Items.Contains(selected))type.Items.Add(selected);
        type.SelectedItem=selected.Length>0?selected:fuelTypes.Count==1?fuelTypes[0]:null;
        var liters=new TextBox{Header="Литры",Text=GsmValues.Text(draft,"liters")};var cost=new TextBox{Header="Стоимость, ₽",Text=GsmValues.Text(draft,"fuelCost")};
        panel.Children.Add(date);panel.Children.Add(type);panel.Children.Add(liters);panel.Children.Add(cost);
        if(existing!=null&&GsmValues.Text(draft,"source") is { Length:>0 } source&&source!="MANUAL")panel.Children.Add(WorkspaceUi.Text("После сохранения эта заправка будет считаться ручной записью; связь с прежним импортом снимется.",true));
        var error=new InfoBar{Severity=InfoBarSeverity.Error,IsClosable=false};panel.Children.Add(error);
        var dialog=WorkspaceUi.Dialog(root,existing==null?"Добавить заправку":"Изменить заправку",panel,"Сохранить заправку");
        JsonObject? result=null;
        dialog.PrimaryButtonClick+=(_,e)=>
        {
            var validLiters=double.TryParse(liters.Text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var l)&&double.IsFinite(l)&&l>0;
            var validCost=double.TryParse(cost.Text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var c)&&double.IsFinite(c)&&c>=0;
            if(date.Date==null||!date.Date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture).StartsWith(month,StringComparison.Ordinal)||type.SelectedItem==null||!validLiters||!validCost)
            {error.Message="Выберите дату в этом месяце, топливо, положительные литры и неотрицательную стоимость.";error.IsOpen=true;e.Cancel=true;return;}
            draft["recordType"]="fuel";draft["date"]=date.Date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);draft["liters"]=l;draft["fuelCost"]=c;draft["fuelType"]=(string)type.SelectedItem;
            result=draft;
        };
        return await dialog.ShowAsync()==ContentDialogResult.Primary?result:null;
    }
}
