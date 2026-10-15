using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Engineer.Core.Auth;

namespace Engineer.Lite.Windows.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsSessionCredentialStore(string target = "LumaWork.Engineer.Lite.Session.v1") : ISessionCredentialStore
{
    public SessionCredentials? Read()
    {
        if (!CredReadW(target, 1, 0, out var pointer))
        {
            var code = Marshal.GetLastWin32Error();
            if (code == 1168) return null;
            throw new Win32Exception(code);
        }
        byte[]? bytes = null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.BlobSize is 0 or > 2560) throw new IOException("Некорректная сохранённая сессия.");
            bytes = new byte[credential.BlobSize];
            Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            return JsonSerializer.Deserialize<SessionCredentials>(bytes) ?? throw new IOException("Некорректная сохранённая сессия.");
        }
        finally { if (bytes != null) CryptographicOperations.ZeroMemory(bytes); CredFree(pointer); }
    }
    public void Write(SessionCredentials credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            if (bytes.Length > 2560) throw new IOException("Сессия превышает размер хранилища Windows.");
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential { Type = 1, TargetName = target, BlobSize = (uint)bytes.Length, Blob = blob, Persist = 2, UserName = "Engineer Lite" };
                if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
                Marshal.FreeHGlobal(blob);
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Delete()
    {
        if (!CredDeleteW(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern void CredFree(IntPtr credential);
}
