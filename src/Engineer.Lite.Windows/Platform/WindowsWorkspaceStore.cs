using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Engineer.Core.Workspace;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace Engineer.Lite.Windows.Platform;

internal sealed class WindowsWorkspaceStore : IWorkspaceStore
{
    private readonly string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWork","EngineerLite","Cache");
    private string PathFor(string key)
    {
        if(key.Length!=64||key.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Некорректный ключ кеша.");
        return Path.Combine(directory,key+".dat");
    }
    public async Task<JsonObject?> LoadAsync(string key,CancellationToken cancellationToken)
    {
        var path=PathFor(key);if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Сохранённые данные превышают допустимый размер.");
        var encrypted=await File.ReadAllBytesAsync(path,cancellationToken);
        var buffer=await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(encrypted));
        cancellationToken.ThrowIfCancellationRequested();
        CryptographicBuffer.CopyToByteArray(buffer,out var bytes);
        try{return JsonNode.Parse(bytes) as JsonObject??throw new InvalidDataException("Повреждены сохранённые данные.");}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public async Task SaveAsync(string key,JsonObject document,CancellationToken cancellationToken)
    {
        var path=PathFor(key);var bytes=Encoding.UTF8.GetBytes(document.ToJsonString());
        byte[] encrypted;
        try
        {
            if(bytes.Length>32*1024*1024)throw new InvalidDataException("Слишком много данных для сохранения.");
            var buffer=await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.CreateFromByteArray(bytes));
            cancellationToken.ThrowIfCancellationRequested();CryptographicBuffer.CopyToByteArray(buffer,out encrypted);
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
        Directory.CreateDirectory(directory);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllBytesAsync(temporary,encrypted,cancellationToken);cancellationToken.ThrowIfCancellationRequested();File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
