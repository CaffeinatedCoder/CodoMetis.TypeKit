namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// <c>Patch&lt;T&gt;</c> is the one struct of the base package whose <c>default</c> means something:
/// absent. It carries no <c>[RequireCustomInitialization]</c>, so the no-default rules leave it alone.
/// </summary>
public sealed class PatchIsDefaultableTests
{
    private const string Source =
        """
        using CodoMetis.TypeKit;

        public sealed class Update
        {
            public Patch<string> Name { get; set; }

            public Patch<int> Written() => default;
            public Patch<int> Made() => new();
            public Patch<int>[] Slots() => new Patch<int>[3];
        }
        """;

    [Fact]
    public Task CMTK0001_is_silent_on_a_default_patch() =>
        new AnalyzerTest<ForbiddenDefaultInitializationAnalyzer>(Source).RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task CMTK0005_is_silent_on_an_array_of_patches() =>
        new AnalyzerTest<DefaultFilledCollectionAnalyzer>(Source).RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task CMTK0006_is_silent_on_an_unassigned_patch_member() =>
        new AnalyzerTest<UnassignedNoDefaultMemberAnalyzer>(Source).RunAsync(TestContext.Current.CancellationToken);
}
