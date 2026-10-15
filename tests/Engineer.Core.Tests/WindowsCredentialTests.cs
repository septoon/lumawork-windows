using Engineer.Core.Auth;
using Engineer.Lite.Windows.Platform;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class WindowsCredentialTests
{
    [Fact]
    public void WindowsCredentialManagerRoundTripAndDelete()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSessionCredentialStore("LumaWork.Engineer.Lite.Tests." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(store.Read());
            var value = new SessionCredentials("https://app.example.invalid", "https://so.example.invalid/v1", new("synthetic-token", new("fixture-user", "engineer@example.invalid", "engineer")), new("synthetic-cookie", new("00001", "fixture", "Иван", "Тестов")));
            store.Write(value);
            Assert.Equal(value, store.Read());
            store.Delete(); Assert.Null(store.Read()); store.Delete();
        }
        finally { store.Delete(); }
    }
}
