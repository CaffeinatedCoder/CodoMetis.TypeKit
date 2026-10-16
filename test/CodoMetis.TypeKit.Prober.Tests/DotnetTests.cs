namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// Which dotnet a tool starts (tools/Shared/Dotnet.cs, design/adr/0105): the one on PATH when it satisfies
/// global.json, the SDK installer's when only that one does, and otherwise the one on PATH, whose own error then
/// reaches whoever reads the output.
/// </summary>
public sealed class DotnetTests : IDisposable
{
    private readonly string installer = Directory.CreateTempSubdirectory("dotnet-installer-").FullName;
    public void Dispose() => Directory.Delete(installer, recursive: true);

    private string Executable => Path.Combine(installer, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private void Install() => File.WriteAllText(Executable, "");

    [Fact]
    public void The_one_on_path_when_it_satisfies_the_pin()
    {
        Install();
        var asked = new List<string>();
        Dotnet.Choose(installer, d => { asked.Add(d); return true; }).ShouldBe(("dotnet", null));
        asked.ShouldBe(["dotnet"], "the installer's is not even asked");
    }

    [Fact]
    public void The_installers_when_only_it_satisfies_the_pin()
    {
        Install();
        Dotnet.Choose(installer, d => d != "dotnet").ShouldBe((Executable, installer));
    }

    [Fact]
    public void The_one_on_path_when_neither_does()
    {
        Install();
        Dotnet.Choose(installer, _ => false).ShouldBe(("dotnet", null));
    }

    [Fact]
    public void The_one_on_path_when_the_installer_put_none()
    {
        var asked = new List<string>();
        Dotnet.Choose(installer, d => { asked.Add(d); return d != "dotnet"; }).ShouldBe(("dotnet", null));
        asked.ShouldBe(["dotnet"]);
    }

    [Fact]
    public void A_dotnet_satisfies_a_directory_whose_pin_it_has_and_not_one_whose_pin_it_lacks()
    {
        Dotnet.Satisfies("dotnet", installer).ShouldBeTrue("no global.json: any SDK");
        File.WriteAllText(Path.Combine(installer, "global.json"), """{"sdk": {"version": "0.0.1", "rollForward": "disable"}}""");
        Dotnet.Satisfies("dotnet", installer).ShouldBeFalse("an SDK no dotnet has");
    }

    [Fact]
    public void A_program_that_does_not_start_satisfies_nothing() =>
        Dotnet.Satisfies(Path.Combine(installer, "no-such-dotnet"), installer).ShouldBeFalse();

    [Fact]
    public void A_child_is_pointed_at_the_chosen_directory()
    {
        var environment = new Dictionary<string, string?> { ["PATH"] = "/usr/bin" };
        Dotnet.Apply(installer, environment);
        environment["PATH"].ShouldBe(installer + Path.PathSeparator + "/usr/bin");
        environment["DOTNET_ROOT"].ShouldBe(installer);

        var untouched = new Dictionary<string, string?> { ["PATH"] = "/usr/bin" };
        Dotnet.Apply(null, untouched);
        untouched.Keys.ShouldBe(["PATH"]);
        untouched["PATH"].ShouldBe("/usr/bin");
    }
}
