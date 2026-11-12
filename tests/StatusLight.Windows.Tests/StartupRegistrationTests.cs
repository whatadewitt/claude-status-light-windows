using System.Runtime.Versioning;
using Microsoft.Win32;
using StatusLight.App.Platform;

namespace StatusLight.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationTests : IDisposable
{
    private const string ValueName = "ClaudeStatusLightTest";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly StartupRegistration _registration = new(ValueName);

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    [Fact]
    public void Apply_true_registers_the_quoted_exe_path()
    {
        _registration.Apply(true, @"C:\x\a.exe");

        Assert.True(_registration.IsRegistered());
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.Equal("\"C:\\x\\a.exe\"", key?.GetValue(ValueName));
    }

    [Fact]
    public void Apply_false_removes_the_value()
    {
        _registration.Apply(true, @"C:\x\a.exe");

        _registration.Apply(false, @"C:\x\a.exe");

        Assert.False(_registration.IsRegistered());
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.Null(key?.GetValue(ValueName));
    }

    [Fact]
    public void Apply_false_when_not_registered_is_a_no_op()
    {
        _registration.Apply(false, @"C:\x\a.exe");

        Assert.False(_registration.IsRegistered());
    }
}
