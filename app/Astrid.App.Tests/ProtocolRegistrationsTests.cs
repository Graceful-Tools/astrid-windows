using Astrid.App.ViewModels;
using Xunit;
using static Astrid.App.ViewModels.ProtocolRegistrations;

namespace Astrid.App.Tests;

/// <summary>
/// Which <c>astrid://</c> registrations are some other build's. Regression for task 64c02099
/// (2026-09-13): six build flavours had each registered the scheme, Windows showed its picker,
/// and the wrong one won.
/// </summary>
public sealed class ProtocolRegistrationsTests
{
    private const string Mine = @"C:\src\astrid-windows\app\Astrid.App\bin\Debug\net9.0-windows10.0.19041.0\win-arm64\Astrid.App.exe";

    [Fact]
    public void Other_builds_are_stale_and_this_one_is_not()
    {
        var registrations = new[]
        {
            new Registration("App.1", $"\"{Mine}\" \"----ms-protocol:%1\""),
            new Registration("App.2", @"C:\src\astrid-windows\dist\portable\win-arm64\Astrid.App.exe ""----ms-protocol:%1"""),
            new Registration("App.3", @"""C:\src\astrid-windows\app\Astrid.App\bin\Release\net9.0-windows10.0.19041.0\win-arm64\Astrid.App.exe"" ""----ms-protocol:%1"""),
        };

        Assert.Equal(
            new[]
            {
                @"C:\src\astrid-windows\dist\portable\win-arm64\Astrid.App.exe",
                @"C:\src\astrid-windows\app\Astrid.App\bin\Release\net9.0-windows10.0.19041.0\win-arm64\Astrid.App.exe",
            },
            StaleExecutables(registrations, Mine));
    }

    /// <summary>The same file spelt with a different case is this build, not a stale one.</summary>
    [Fact]
    public void The_same_path_in_another_case_is_not_stale()
    {
        var lower = Mine.Replace("C:\\", "c:\\");
        var registrations = new[] { new Registration("App.1", $"{lower} \"----ms-protocol:%1\"") };

        Assert.Empty(StaleExecutables(registrations, Mine));
    }

    /// <summary>A registration with no command left behind cannot be unregistered by path, so it is skipped.</summary>
    [Fact]
    public void Registrations_without_a_command_are_skipped_and_duplicates_collapse()
    {
        var registrations = new[]
        {
            new Registration("App.1", null),
            new Registration("App.2", ""),
            new Registration("App.3", @"C:\other\Astrid.App.exe ""----ms-protocol:%1"""),
            new Registration("App.4", @"""c:\other\Astrid.App.exe"" ""----ms-protocol:%1"""),
        };

        Assert.Equal(new[] { @"C:\other\Astrid.App.exe" }, StaleExecutables(registrations, Mine));
    }

    [Theory]
    [InlineData(@"""C:\a b\Astrid.App.exe"" ""----ms-protocol:%1""", @"C:\a b\Astrid.App.exe")]
    [InlineData(@"C:\a\Astrid.App.exe ""----ms-protocol:%1""", @"C:\a\Astrid.App.exe")]
    [InlineData(@"C:\a\Astrid.App.exe", @"C:\a\Astrid.App.exe")]
    [InlineData(@"  ""C:\a\Astrid.App.exe"" ""%1""  ", @"C:\a\Astrid.App.exe")]
    public void The_executable_is_read_out_of_either_command_shape(string command, string expected)
    {
        Assert.Equal(expected, ExecutableOf(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void No_executable_in_nothing(string? command)
    {
        Assert.Null(ExecutableOf(command));
    }

    /// <summary>
    /// The shell's "How do you want to open this?" picker is not a claimant — regression for task
    /// 02a8b67e.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the symptom task 64c02099 actually saw: with several apps claiming <c>astrid://</c>
    /// and no default recorded, the shell answers <c>OpenWith.exe</c>, and the first pick made in
    /// that dialog becomes the default — which is how a build that could not finish the sign-in
    /// came to own the callback.
    /// </para>
    /// <para>
    /// The smoke test that exists to catch it could not: <c>OpenWith.exe</c> is a real executable
    /// of the host's own architecture and its open command carries <c>%1</c>, so every assertion
    /// made about the resolved command passed while the shell was showing a picker. A packaged
    /// build and an unpackaged one claim the scheme by different mechanisms — <c>uap:Protocol</c>
    /// and the App SDK — and neither can see the other, so side by side is the ordinary way to
    /// arrive at two claimants and no default.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(@"C:\WINDOWS\system32\OpenWith.exe ""%1""")]
    [InlineData(@"""C:\WINDOWS\system32\OpenWith.exe"" ""%1""")]
    [InlineData(@"""c:\windows\system32\openwith.exe"" ""%1""")]
    [InlineData(@"C:\WINDOWS\system32\OpenWith.exe")]
    public void The_shell_picker_is_not_a_claimant(string command)
    {
        Assert.True(IsShellPicker(command), $"the picker went unnoticed: {command}");
    }

    /// <summary>A real build, and an app merely named like the picker, are claimants.</summary>
    [Theory]
    [InlineData(@"""C:\src\astrid-windows\app\Astrid.App\bin\Debug\win-arm64\Astrid.App.exe"" ""----ms-protocol:%1""")]
    [InlineData(@"C:\src\astrid-windows\dist\portable\win-arm64\Astrid.App.exe ""----ms-protocol:%1""")]
    [InlineData(@"""C:\apps\MyOpenWith.exe"" ""%1""")]
    [InlineData(@"""C:\apps\OpenWithHelper.exe"" ""%1""")]
    [InlineData(null)]
    [InlineData("")]
    public void An_app_is_not_the_picker(string? command)
    {
        Assert.False(IsShellPicker(command), $"a claimant was mistaken for the picker: {command}");
    }
}
