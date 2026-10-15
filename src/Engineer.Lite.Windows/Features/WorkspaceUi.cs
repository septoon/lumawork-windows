using Engineer.Core.Spreadsheet;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Engineer.Lite.Windows.Features;

internal static class WorkspaceUi
{
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
