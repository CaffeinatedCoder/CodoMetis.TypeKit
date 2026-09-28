namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every C# block in the READMEs is compiled code: it equals, after indentation, exactly one sample
/// region in <c>test/CodoMetis.TypeKit.Samples</c>, and every region there is shown in the README its
/// name starts with. A README edit that is not mirrored in the samples fails here, and a sample that
/// stops compiling fails the build.
/// </summary>
/// <remarks>
/// <para>
/// A pre-release audit compiled the READMEs' blocks and found a bare fault returned from a method typed
/// <c>Result&lt;T, TError&gt;</c>, statements that are not statements (<c>quantity.Value;</c>), and SQL in a
/// comment that the query does not produce. The consumer smoke test had found the first once before
/// (docs/plan.md §9). Each was fixed by hand, and nothing kept the next one out.
/// </para>
/// <para>
/// A block that cannot compile on purpose, showing a compile error, says so above its fence:
/// <c>&lt;!-- not compiled: {reason} --&gt;</c>. An analyzer diagnostic in a comment is no reason: the
/// sample suppresses it around the region and compiles. The READMEs and the samples are discovered,
/// and floors keep the theories from passing on empty lists.
/// </para>
/// </remarks>
public sealed class ReadmeSampleTests
{
    private static readonly Lazy<IReadOnlyList<CodeBlock>> Blocks = new(ReadmeSamples.CodeBlocks);

    private static readonly Lazy<IReadOnlyList<SampleRegion>> Samples = new(ReadmeSamples.Regions);

    public static TheoryData<string> CompiledBlocks =>
        [.. Blocks.Value.Where(block => block is { IsCSharp: true, OptOut: null }).Select(block => block.Location)];

    public static TheoryData<string> SampleNames => [.. Samples.Value.Select(sample => sample.Name)];

    [Fact]
    public void The_READMEs_are_discovered() =>
        ReadmeSamples.Readmes().Count.ShouldBeGreaterThanOrEqualTo(6, string.Join(", ", ReadmeSamples.Readmes()));

    [Fact]
    public void The_csharp_blocks_and_the_samples_are_discovered()
    {
        Blocks.Value.Count(block => block.IsCSharp).ShouldBeGreaterThanOrEqualTo(30);
        Samples.Value.Count.ShouldBeGreaterThanOrEqualTo(30);
    }

    [Theory]
    [MemberData(nameof(CompiledBlocks))]
    public void A_csharp_block_in_a_README_is_a_compiled_sample(string location)
    {
        var block = Blocks.Value.Single(candidate => candidate.Location == location);
        var sample = Samples.Value.SingleOrDefault(candidate => candidate.Code == block.Code);

        if (sample is null)
        {
            throw new ShouldAssertException(
                $"{location} is not compiled: no sample in {ReadmeSamples.SamplesProject} holds this code. Mirror the change in the sample region, " +
                $"or mark a block that cannot compile on purpose with <!-- not compiled: {{reason}} --> above its fence.{Environment.NewLine}" +
                Closest(block.Code, Samples.Value.Select(candidate => (candidate.Name + " at " + candidate.Location, candidate.Code)), "sample", "README"));
        }

        sample.ReadmePrefix.ShouldBe(
            ReadmeSamples.Prefix(block.Readme),
            $"{location} is compiled as '{sample.Name}' ({sample.Location}), whose name says it belongs to another README.");
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void A_sample_is_shown_in_the_README_its_name_starts_with(string name)
    {
        var sample = Samples.Value.Single(candidate => candidate.Name == name);
        var readme = ReadmeSamples.Readmes().SingleOrDefault(candidate => ReadmeSamples.Prefix(candidate) == sample.ReadmePrefix);

        readme.ShouldNotBeNull(
            $"'{name}' ({sample.Location}) starts with '{sample.ReadmePrefix}', which is no README. " +
            $"A sample's name starts with README or a package id: {string.Join(", ", ReadmeSamples.Readmes().Select(ReadmeSamples.Prefix))}.");

        var shown = Blocks.Value.Where(block => block.Readme == readme && block.IsCSharp).ToArray();

        if (!shown.Any(block => block.Code == sample.Code))
        {
            throw new ShouldAssertException(
                $"'{name}' ({sample.Location}) is shown nowhere in {readme}. Delete it, or mirror it back into the README.{Environment.NewLine}" +
                Closest(sample.Code, shown.Select(block => (block.Location, block.Code)), "README", "sample"));
        }
    }

    /// <summary>Two samples with one body would leave a README block two places to be compiled from, and one of them unread.</summary>
    [Fact]
    public void No_two_samples_hold_the_same_code()
    {
        var duplicates = Samples.Value.GroupBy(sample => sample.Code)
                                .Where(group => group.Count() > 1)
                                .Select(group => string.Join(" and ", group.Select(sample => $"'{sample.Name}' ({sample.Location})")))
                                .ToArray();

        duplicates.ShouldBeEmpty();
    }

    /// <summary>An opt-out is rare, says why, and is removed once the block compiles.</summary>
    [Fact]
    public void A_block_that_is_not_compiled_says_why_and_really_is_not()
    {
        var optedOut = Blocks.Value.Where(block => block.OptOut is not null).ToArray();

        optedOut.Where(block => !block.IsCSharp).Select(block => block.Location)
                .ShouldBeEmpty("The not-compiled marker is for C# blocks; nothing else is compiled.");
        optedOut.Where(block => block.OptOut!.Length == 0).Select(block => block.Location)
                .ShouldBeEmpty("A not-compiled marker says why: <!-- not compiled: {reason} -->.");
        optedOut.Where(block => Samples.Value.Any(sample => sample.Code == block.Code)).Select(block => block.Location)
                .ShouldBeEmpty("These blocks are marked not compiled, but a sample compiles them. Remove the marker.");
    }

    /// <summary>An unlabelled fence would hold C# that nothing compiles.</summary>
    [Fact]
    public void Every_code_fence_names_its_language() =>
        Blocks.Value.Where(block => block.Language.Length == 0).Select(block => block.Location)
              .ShouldBeEmpty("Name the language after the opening fence (```csharp, ```bash, ```json).");

    /// <summary>The positive control for the README side: the extraction finds what the theories rely on.</summary>
    [Fact]
    public void Code_blocks_are_found_indented_labelled_and_opted_out()
    {
        const string markdown =
            """
            # Title

            ```csharp
            var a = 1;

                var b = a;   // kept relative
            ```

            - a list item

              ```cs
              Option<int> none = Option.None();
              ```

            ```bash
            dotnet build
            ```

            <!-- not compiled: shows CS0029 -->

            ```csharp
            Result<long, int> r = 5;
            ```

            <!-- not compiled -->
            ```csharp
            x;
            ```
            """;

        var blocks = ReadmeSamples.CodeBlocks("R.md", markdown);

        blocks.Select(block => (block.Location, block.Language, block.IsCSharp, block.OptOut)).ShouldBe(
        [
            ("R.md:3", "csharp", true, null),
            ("R.md:11", "cs", true, null),
            ("R.md:15", "bash", false, null),
            ("R.md:21", "csharp", true, "shows CS0029"),
            ("R.md:26", "csharp", true, ""),
        ]);
        blocks[0].Code.ShouldBe("var a = 1;\n\n    var b = a;   // kept relative");
        blocks[1].Code.ShouldBe("Option<int> none = Option.None();");
    }

    [Fact]
    public void An_unclosed_fence_is_refused() =>
        Should.Throw<InvalidOperationException>(() => ReadmeSamples.CodeBlocks("R.md", "text\n```csharp\nvar a = 1;\n")).Message.ShouldBe("R.md:2: the code fence is never closed.");

    /// <summary>
    /// The positive control for the samples side: one region, one in parts at different depths, and a
    /// directive in column 0 beside indented code, as C# style writes it.
    /// </summary>
    [Fact]
    public void Sample_regions_are_found_and_their_parts_joined()
    {
        const string source =
            """
            // sample: README/usings-and-statements
            using CodoMetis.TypeKit;

            // end sample
            namespace Shop;

            // sample: README/type
            public readonly partial record struct OrderId : IValue<Guid>;
            // end sample

            public static class Scaffold
            {
                public static void M()
                {
                    // sample: README/usings-and-statements
                    var id = OrderId.New();
                    if (id != default)
                        Console.WriteLine(id);
                    // end sample

                    // sample: README/directive
            #pragma warning disable CMTK0001
                    OrderId none = default;
            #pragma warning restore CMTK0001
                    // end sample
                }
            }
            """;

        var regions = ReadmeSamples.Regions("S.cs", source);

        regions.Select(region => (region.Name, region.Location)).ShouldBe(
        [
            ("README/usings-and-statements", "S.cs:1"),
            ("README/type", "S.cs:7"),
            ("README/directive", "S.cs:21"),
        ]);
        regions[0].Code.ShouldBe("using CodoMetis.TypeKit;\n\nvar id = OrderId.New();\nif (id != default)\n    Console.WriteLine(id);");
        regions[1].Code.ShouldBe("public readonly partial record struct OrderId : IValue<Guid>;");
        regions[2].Code.ShouldBe("#pragma warning disable CMTK0001\nOrderId none = default;\n#pragma warning restore CMTK0001");
    }

    [Theory]
    [InlineData("// sample: README/a\n// sample: README/b\n// end sample\n// end sample", "S.cs:2: sample 'README/b' starts inside sample 'README/a' (S.cs:1).")]
    [InlineData("var a = 1;\n// end sample", "S.cs:2: '// end sample' ends no sample.")]
    [InlineData("// sample: README/a\nvar a = 1;", "S.cs:1: sample 'README/a' never ends.")]
    [InlineData("// sample: README/a\n\n// end sample", "S.cs:1: sample 'README/a' is empty.")]
    [InlineData("//sample: README/a\nvar a = 1;\n// end sample", "S.cs:1: '//sample: README/a' reads like a sample marker but is not one. Write '// sample: {README}/{name}' and '// end sample'.")]
    [InlineData("// sample: a\nvar a = 1;\n// end sample", "S.cs:1: '// sample: a' reads like a sample marker but is not one. Write '// sample: {README}/{name}' and '// end sample'.")]
    public void A_malformed_region_is_refused(string source, string message) =>
        Should.Throw<InvalidOperationException>(() => ReadmeSamples.Regions("S.cs", source)).Message.ShouldBe(message);

    /// <summary>The candidate sharing the most lines with <paramref name="code"/>, and a diff against it.</summary>
    private static string Closest(string code, IEnumerable<(string Label, string Code)> candidates, string candidateSide, string codeSide)
    {
        var all = candidates.ToArray();
        if (all.Length == 0) return $"There is no {candidateSide} to compare it with.";

        var best = all.MaxBy(candidate => ReadmeSamples.CommonLines(candidate.Code, code));
        var spacingOnly = ReadmeSamples.CollapseWhitespace(best.Code) == ReadmeSamples.CollapseWhitespace(code) ? ", which differs in whitespace only" : "";

        return $"The closest {candidateSide} is {best.Label}{spacingOnly} (- {candidateSide}, + {codeSide}):{Environment.NewLine}{ReadmeSamples.Diff(best.Code, code)}";
    }
}
