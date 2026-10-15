using Engineer.Core.Spreadsheet;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Engineer.Lite.Windows.Features;

internal static class WorkspaceUi
{
    internal static TextBlock SectionTitle(string title)=>new(){Text=title,FontSize=20,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    internal static Border Card(string title,string subtitle,params UIElement[] content)
    {
        var panel=new StackPanel{Spacing=12};
        if(title.Length>0)panel.Children.Add(SectionTitle(title));
        if(subtitle.Length>0)panel.Children.Add(Text(subtitle,true));
        foreach(var item in content)panel.Children.Add(item);
        return new Border{Child=panel,Padding=new Thickness(20),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),
            Background=(Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush=(Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]};
    }
    internal static Grid ValueRow(string label,string value)
    {
        var grid=new Grid{ColumnSpacing=20,RowSpacing=4};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        grid.Children.Add(Text(label,true));var text=Text(string.IsNullOrWhiteSpace(value)?"Не заполнено":value);text.IsTextSelectionEnabled=true;Grid.SetColumn(text,1);grid.Children.Add(text);return grid;
    }
    internal static TextBlock Text(string text,bool secondary=false)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Opacity=secondary?0.72:1};
    internal static Button Button(string title,RoutedEventHandler handler,bool accent=false)
    {
        var button=new Button{Content=title};button.Click+=handler;
        if(accent)button.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
        return button;
    }
    internal static ContentDialog Dialog(XamlRoot root,string title,UIElement content,string primary="Сохранить")
    {
        var dialog=new ContentDialog{XamlRoot=root,Title=title,Content=content,PrimaryButtonText=primary,CloseButtonText="Отмена",DefaultButton=ContentDialogButton.Close};
        dialog.Resources["ContentDialogMaxWidth"]=840d;return dialog;
    }
    internal static async Task<bool> ConfirmAsync(XamlRoot root,string title,string text,string primary)=>await Dialog(root,title,Text(text),primary).ShowAsync()==ContentDialogResult.Primary;
    internal static async Task<XlsxWorkbook?> PickAsync(Window owner,bool requests)
    {
        var picker=new FileOpenPicker{ViewMode=PickerViewMode.List,SuggestedStartLocation=PickerLocationId.Downloads};picker.FileTypeFilter.Add(".xlsx");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file=await picker.PickSingleFileAsync();if(file==null)return null;
        return await Task.Run(()=>{using var input=File.OpenRead(file.Path);return XlsxReader.Read(input,file.Name,default,requests?XlsxLimits.ClosedRequests:null);});
    }
    internal static string Message(Exception error)=>error is InvalidDataException or ArgumentException?error.Message:"Не удалось прочитать файл. Выберите доступный XLSX и повторите.";
}
