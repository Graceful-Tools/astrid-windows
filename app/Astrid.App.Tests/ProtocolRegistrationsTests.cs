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
}
