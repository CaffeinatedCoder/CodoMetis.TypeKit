using CodoMetis.TypeKit;
using CodoMetis.TypeKit.Testing;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Generators.AddCreate;

// sample: CodoMetis.TypeKit.Generators/add-create
public enum NameFault { Blank, TooLong }

public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
{
    public static Result<Name, NameFault> Create(string value)
    {
        // Add your rules: if (...) return Result.Error(NameFault.Blank);
        return new Name(value);
    }
}
// end sample

/// <summary>What the fix writes is generated as any hand-written <c>Create</c> is: every factory runs it.</summary>
public sealed class AddCreateTests
{
    [Fact]
    public void The_written_Create_accepts_every_value_and_the_factories_run_it()
    {
        Name.Create("").ShouldBeSuccess().Value.ShouldBe("");
        Name.TryFrom("Ada").ShouldBeSome().Value.ShouldBe("Ada");
        Name.FromKnownGood("Ada").Value.ShouldBe("Ada");
    }
}
