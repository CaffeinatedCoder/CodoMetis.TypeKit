using CodoMetis.TypeKit.ValueObjects;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// The fix for a validated value object without <c>Create</c>: the compiler's CS0535 at the interface,
/// answered with a <c>Create</c> that accepts every value, laid out as the type is.
/// </summary>
/// <remarks>
/// A second file declares the private constructor the generators add, so a fixed file that does not
/// compile fails the test as the build would; the generation itself is the Samples' to show, from the
/// block <c>The_README_s_block_is_what_the_fix_writes</c> holds to the fix.
/// </remarks>
public sealed class MissingCreateCodeFixTests
{
    private const string Usings =
        """
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;


        """;

    [Fact]
    public void The_names_the_fix_spells_are_the_contract_s()
    {
        MissingCreateCodeFixProvider.ValidatedValueMetadataName.ShouldBe(typeof(IValidatedValue<,,>).FullName);
        typeof(IValidatedValue<,,>).Assembly.ShouldBe(RealTypeKit.Assembly);

        var create = typeof(IValidatedValue<,,>).GetMethods().ShouldHaveSingleItem();
        create.Name.ShouldBe(MissingCreateCodeFixProvider.CreateName);
        create.GetParameters().ShouldHaveSingleItem().Name.ShouldBe("value");
    }

    [Fact]
    public Task A_struct_declared_with_a_semicolon_gets_a_body_and_Create() =>
        Fix(Usings + """
            public enum NameFault { Blank, TooLong }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|};
            """,
            Usings + """
            public enum NameFault { Blank, TooLong }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task A_class_gets_Create_as_a_struct_does() =>
        Fix(Usings + """
            public enum CountFault { Negative }

            public sealed partial record Count : {|CS0535:IValidatedValue<Count, int, CountFault>|};
            """,
            Usings + """
            public enum CountFault { Negative }

            public sealed partial record Count : IValidatedValue<Count, int, CountFault>
            {
                public static Result<Count, CountFault> Create(int value)
                {
                    // Add your rules: if (...) return Result.Error(CountFault.Negative);
                    return new Count(value);
                }
            }
            """,
            Constructor("public sealed partial record Count", "int"));

    [Fact]
    public Task Create_comes_first_in_a_body_of_several_lines_a_blank_line_before_the_next_member() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|}
            {
                /// <summary>The name as it is shown.</summary>
                public override string ToString() => "";
            }
            """,
            Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }

                /// <summary>The name as it is shown.</summary>
                public override string ToString() => "";
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task An_empty_body_on_lines_of_its_own_keeps_its_braces_where_they_are() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|} {
            }
            """,
            Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault> {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task An_empty_body_on_one_line_gets_a_body_on_lines_of_its_own() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|} { }
            """,
            Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task A_body_on_one_line_that_holds_members_gets_no_fix() =>
        NoFix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|} { public override string ToString() => ""; }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task The_semicolon_s_comment_stays_after_the_body() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|}; // a person's name
            """,
            Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            } // a person's name
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task A_nested_value_object_in_a_generic_type_is_indented_and_named_as_it_stands() =>
        Fix(Usings + """
            namespace Shop
            {
                public partial class Catalog<TItem>
                {
                    public enum SkuFault { Blank }

                    public readonly partial record struct Sku : {|CS0535:IValidatedValue<Sku, string, SkuFault>|};
                }
            }
            """,
            Usings + """
            namespace Shop
            {
                public partial class Catalog<TItem>
                {
                    public enum SkuFault { Blank }

                    public readonly partial record struct Sku : IValidatedValue<Sku, string, SkuFault>
                    {
                        public static Result<Sku, SkuFault> Create(string value)
                        {
                            // Add your rules: if (...) return Result.Error(SkuFault.Blank);
                            return new Sku(value);
                        }
                    }
                }
            }
            """,
            """
            namespace Shop
            {
                public partial class Catalog<TItem>
                {
                    public readonly partial record struct Sku { private Sku(string value) { } }
                }
            }
            """);

    [Fact]
    public Task Types_of_a_namespace_the_file_does_not_import_are_qualified() =>
        Fix("""
            namespace Faults { public enum WeightFault { Negative } }

            public readonly partial record struct Weight : {|CS0535:CodoMetis.TypeKit.ValueObjects.IValidatedValue<Weight, System.Text.StringBuilder, Faults.WeightFault>|};
            """,
            """
            namespace Faults { public enum WeightFault { Negative } }

            public readonly partial record struct Weight : CodoMetis.TypeKit.ValueObjects.IValidatedValue<Weight, System.Text.StringBuilder, Faults.WeightFault>
            {
                public static CodoMetis.TypeKit.Result<Weight, Faults.WeightFault> Create(System.Text.StringBuilder value)
                {
                    // Add your rules: if (...) return Result.Error(Faults.WeightFault.Negative);
                    return new Weight(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Weight", "System.Text.StringBuilder"));

    [Fact]
    public Task A_fault_that_is_no_enum_leaves_the_example_open() =>
        Fix(Usings + """
            public sealed record CodeFault(string Reason);

            public readonly partial record struct Code : {|CS0535:IValidatedValue<Code, string, CodeFault>|};
            """,
            Usings + """
            public sealed record CodeFault(string Reason);

            public readonly partial record struct Code : IValidatedValue<Code, string, CodeFault>
            {
                public static Result<Code, CodeFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(...);
                    return new Code(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Code", "string"));

    [Fact]
    public Task A_file_indented_with_tabs_and_ending_lines_in_CRLF_stays_so() =>
        Fix(Crlf(Usings + "namespace Shop\n{\n\tpublic enum NameFault { Blank }\n\n\tpublic readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|};\n}\n"),
            Crlf(Usings + "namespace Shop\n{\n\tpublic enum NameFault { Blank }\n\n\tpublic readonly partial record struct Name : IValidatedValue<Name, string, NameFault>\n\t{\n"
               + "\t\tpublic static Result<Name, NameFault> Create(string value)\n\t\t{\n\t\t\t// Add your rules: if (...) return Result.Error(NameFault.Blank);\n"
               + "\t\t\treturn new Name(value);\n\t\t}\n\t}\n}\n"),
            "namespace Shop { public readonly partial record struct Name { private Name(string value) { } } }");

    [Fact]
    public Task The_interface_brought_by_an_interface_of_the_application_is_fixed_too() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public interface IName<TSelf> : IValidatedValue<TSelf, string, NameFault> where TSelf : IName<TSelf>;

            public readonly partial record struct Name : {|CS0535:IName<Name>|};
            """,
            Usings + """
            public enum NameFault { Blank }

            public interface IName<TSelf> : IValidatedValue<TSelf, string, NameFault> where TSelf : IName<TSelf>;

            public readonly partial record struct Name : IName<Name>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task The_part_that_names_the_interface_gets_Create_when_the_type_has_two() =>
        new CodeFixTest<EmptyDiagnosticAnalyzer, MissingCreateCodeFixProvider>(
            Usings + "public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|};",
            Usings + """
            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }
            }
            """)
            {
                TestState  = { Sources = { ("Other.cs", OtherPart) } },
                FixedState = { Sources = { ("Other.cs", OtherPart) } },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    private const string OtherPart =
        """
        public enum NameFault { Blank }

        public readonly partial record struct Name
        {
            private Name(string value) { }

            public override string ToString() => "";
        }
        """;

    /// <summary>The instance to return is the other type's to build, not this one's.</summary>
    [Fact]
    public Task A_type_whose_TSelf_is_another_type_gets_no_fix() =>
        NoFix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value) => new Name(value);
            }

            public readonly partial record struct Alias : {|CS0535:IValidatedValue<Name, string, NameFault>|};
            """,
            Constructor("public readonly partial record struct Name", "string"));

    /// <summary>
    /// An instance <c>Create(string)</c> is CS8928, not CS0535, and a second <c>Create(string)</c> would not
    /// compile (CS0111): the one there is the author's to correct.
    /// </summary>
    [Fact]
    public Task An_instance_Create_of_the_wrapped_type_gets_no_fix() =>
        NoFix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS8928:IValidatedValue<Name, string, NameFault>|}
            {
                public Result<Name, NameFault> Create(string value) => new Name(value);
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    [Fact]
    public Task A_Create_of_another_type_is_an_overload_and_the_fix_is_offered() =>
        Fix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|}
            {
                public static Result<Name, NameFault> Create(char initial) => Result.Error(NameFault.Blank);
            }
            """,
            Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
            {
                public static Result<Name, NameFault> Create(string value)
                {
                    // Add your rules: if (...) return Result.Error(NameFault.Blank);
                    return new Name(value);
                }

                public static Result<Name, NameFault> Create(char initial) => Result.Error(NameFault.Blank);
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    /// <summary>Another interface's missing member is its own CS0535, at its own base type: not this fix's.</summary>
    [Fact]
    public Task Another_interface_s_missing_member_gets_no_fix() =>
        NoFix(Usings + """
            public enum NameFault { Blank }

            public interface IShown { string Shown { get; } }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>, {|CS0535:IShown|}
            {
                public static Result<Name, NameFault> Create(string value) => new Name(value);
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    /// <summary>An <c>in</c> parameter is another signature (CS0535), and a second <c>Create(string)</c> beside it would not compile (CS0663).</summary>
    [Fact]
    public Task A_Create_taking_an_in_T_gets_no_fix() =>
        NoFix(Usings + """
            public enum NameFault { Blank }

            public readonly partial record struct Name : {|CS0535:IValidatedValue<Name, string, NameFault>|}
            {
                public static Result<Name, NameFault> Create(in string value) => new Name(value);
            }
            """,
            Constructor("public readonly partial record struct Name", "string"));

    /// <summary>One interface that brings two missing members has a CS0535 for each: the fix is offered on Create's alone.</summary>
    [Fact]
    public async Task Of_two_members_one_interface_brings_the_fix_is_offered_on_Create_s()
    {
        var offered = await MissingCreateVariantTests.OfferedAsync(
            Usings + """
            public enum NameFault { Blank }

            public interface IName<TSelf> : IValidatedValue<TSelf, string, NameFault> where TSelf : IName<TSelf> { string Shown { get; } }

            public readonly partial record struct Name : IName<Name>;
            """,
            Constructor("public readonly partial record struct Name", "string"));

        offered.Count.ShouldBe(2);
        offered.Count(o => o.Offered).ShouldBe(1);
    }

    /// <summary>Another interface's own <c>Create</c> is missing too: the fix is offered at the contract, not there.</summary>
    [Fact]
    public async Task Another_interface_s_Create_gets_no_fix_beside_the_contract_s()
    {
        var offered = await MissingCreateVariantTests.OfferedAsync(
            Usings + """
            public enum NameFault { Blank }

            public interface IFactory<TSelf> { static abstract TSelf Create(int seed); }

            public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>, IFactory<Name>;
            """,
            Constructor("public readonly partial record struct Name", "string"));

        offered.ShouldBe([("IValidatedValue<Name, string, NameFault>", true), ("IFactory<Name>", false)], ignoreOrder: true);
    }

    /// <summary>Discovery is by the contract's identity: an interface of the application's own that shares its name gets no fix.</summary>
    [Fact]
    public async Task A_lookalike_IValidatedValue_of_the_application_gets_no_fix()
    {
        var offered = await MissingCreateVariantTests.OfferedAsync(
            """
            namespace Mine
            {
                public interface IValidatedValue<TSelf, T, TFault> { static abstract CodoMetis.TypeKit.Result<TSelf, TFault> Create(T value); }

                public enum NameFault { Blank }

                public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>;
            }
            """,
            "");

        offered.ShouldBe([("IValidatedValue<Name, string, NameFault>", false)]);
    }

    [Fact]
    public Task The_action_is_titled_Add_Create() =>
        new CodeFixTest<EmptyDiagnosticAnalyzer, MissingCreateCodeFixProvider>(
            Usings + "public enum F { A }\n\npublic readonly partial record struct N : {|CS0535:IValidatedValue<N, int, F>|};",
            Usings + "public enum F { A }\n\npublic readonly partial record struct N : IValidatedValue<N, int, F>\n{\n    public static Result<N, F> Create(int value)\n    {\n"
                   + "        // Add your rules: if (...) return Result.Error(F.A);\n        return new N(value);\n    }\n}")
            {
                TestState          = { Sources = { ("Generated.cs", Constructor("public readonly partial record struct N", "int")) } },
                FixedState         = { Sources = { ("Generated.cs", Constructor("public readonly partial record struct N", "int")) } },
                CodeActionVerifier = (action, verifier) => verifier.Equal("Add Create", action.Title),
            }
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The Generators README's block is what the fix writes for its declaration ended with <c>;</c>, as
    /// you would type it: the Samples compile the same block, generated, so what the fix writes is shown,
    /// compiled and generated in one place.
    /// </summary>
    [Fact]
    public Task The_README_s_block_is_what_the_fix_writes()
    {
        var readme = File.ReadAllText(Path.Combine(AnalyzerHelpLinkTests.RepositoryRoot(), "src", "CodoMetis.TypeKit.Generators", "README.md")).Replace("\r\n", "\n");
        var blocks = readme.Split("```csharp\n").Skip(1).Select(block => block[..block.IndexOf("```", StringComparison.Ordinal)].TrimEnd('\n'));
        var block  = blocks.Single(candidate => candidate.Contains("// Add your rules:", StringComparison.Ordinal));

        var lines       = block.Split('\n');
        var declaration = Array.FindIndex(lines, line => line.Contains(" : IValidatedValue<", StringComparison.Ordinal));
        var written     = string.Join('\n', lines.Take(declaration)) + '\n' + lines[declaration].Replace(" : IValidatedValue<", " : {|CS0535:IValidatedValue<") + "|};";
        var self        = lines[declaration].Split(" : ")[0];

        return Fix(Usings + written, Usings + block, Constructor(self, "string"));
    }

    /// <summary>The private constructor the generators add, in a file of its own as theirs is.</summary>
    internal static string Constructor(string declaration, string wrapped) =>
        $"{declaration} {{ private {declaration.Split(' ')[^1]}({wrapped} value) {{ }} }}";

    private static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static Task Fix(string source, string fixedSource, string generated) =>
        new CodeFixTest<EmptyDiagnosticAnalyzer, MissingCreateCodeFixProvider>(source, fixedSource)
            {
                TestState  = { Sources = { ("Generated.cs", generated) } },
                FixedState = { Sources = { ("Generated.cs", generated) } },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    private static Task NoFix(string source, string generated) => Fix(source, source, generated);
}
