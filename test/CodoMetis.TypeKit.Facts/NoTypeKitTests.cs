namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// A fact here is the framework's behaviour on its own: an assembly of TypeKit's in this test host would
/// let a fact pass or fail on TypeKit's code, and a failure would no longer say that a framework changed.
/// </summary>
public sealed class NoTypeKitTests
{
    [Fact]
    public void No_TypeKit_assembly_is_referenced() =>
        typeof(NoTypeKitTests).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("CodoMetis.", StringComparison.Ordinal))
            .ShouldBeEmpty();

    [Fact]
    public void No_TypeKit_assembly_is_beside_the_tests() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "CodoMetis.*.dll")
            .Select(Path.GetFileName)
            .Where(name => name != "CodoMetis.TypeKit.Facts.dll")
            .ShouldBeEmpty();
}
