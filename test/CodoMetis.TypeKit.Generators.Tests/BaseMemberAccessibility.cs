using System.Text;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// A base type's member of a generated member's name or signature, by its accessibility (#230): what the value
/// object or the closed set can see stands in for the generated member and is refused, and what it cannot see, a
/// base's private member, is hidden by C# and stands in for nothing, so the type is generated as 2.3.0 generated
/// it. One base per member and accessibility, one value object or set over each, built in
/// <see cref="BuildOutcomeTests.Consumer"/>; what the private ones generate is <see cref="InheritedMembersTests"/>'.
/// </summary>
internal static class BaseMemberAccessibility
{
    /// <summary>Every accessibility a member of a base can have, by the name the declarations use for it.</summary>
    public static IReadOnlyList<(string Name, string Keyword)> Accessibilities { get; } =
    [
        ("Private", "private"),
        ("PrivateProtected", "private protected"),
        ("Protected", "protected"),
        ("Internal", "internal"),
        ("ProtectedInternal", "protected internal"),
        ("Public", "public"),
    ];

    /// <summary>
    /// The generated members a base record's member stands in for, the declaration of each in a base record
    /// <c>&lt;TSelf&gt;</c>, whether the value object is a validated one, and the error that refuses it.
    /// </summary>
    private static readonly (string Name, string Declaration, bool Validated, string Id)[] ValueObjectMembers =
    [
        ("Value", "int Value => 0;", false, "CMTK1011"),
        ("From", "static TSelf From(int value) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("MinValue", "static int MinValue => 0;", false, "CMTK1011"),
        ("MaxValue", "static int MaxValue => 0;", false, "CMTK1011"),
        ("Parse", "static TSelf Parse(string text, System.IFormatProvider? provider) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("TryParse", "static bool TryParse(string? text, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("SpanParse", "static TSelf Parse(System.ReadOnlySpan<char> text, System.IFormatProvider? provider) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("SpanTryParse", "static bool TryParse(System.ReadOnlySpan<char> text, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("Utf8Parse", "static TSelf Parse(System.ReadOnlySpan<byte> text, System.IFormatProvider? provider) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("Utf8TryParse", "static bool TryParse(System.ReadOnlySpan<byte> text, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("Format", "string ToString(string? format, System.IFormatProvider? provider) => \"\";", false, "CMTK1011"),
        ("TryFormat", "bool TryFormat(System.Span<char> destination, out int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => throw new System.NotSupportedException();", false, "CMTK1011"),
        ("ObjectCompareTo", "int CompareTo(object? other) => 0;", false, "CMTK1008"),
        ("CompareTo", "int CompareTo(TSelf? other) => 0;", false, "CMTK1008"),
        ("TryFrom", "static Option<TSelf> TryFrom(int value) => throw new System.NotSupportedException();", true, "CMTK1011"),
        ("Revalidate", "Result<TSelf, AccessibilityFault> Revalidate() => throw new System.NotSupportedException();", true, "CMTK1011"),
    ];

    /// <summary>The generated members of a closed set a base class's member stands in for, declared in a base class <c>&lt;TSelf&gt;</c>.</summary>
    private static readonly (string Name, string Declaration)[] SetMembers =
    [
        ("Parse", "static TSelf Parse(string name) => throw new System.NotSupportedException();"),
        // The ignore-case form, which no interface declares: a base's protected TryParse(string?, out TSelf) is also
        // CS0737 beside the refusal, as it can't implement IClosedSet<TSelf>.TryParse.
        ("TryParse", "static bool TryParse(string? name, bool ignoreCase, out TSelf member) => throw new System.NotSupportedException();"),
        ("Match", "TResult Match<TResult>(System.Func<TResult> a) => a();"),
    ];

    private static string ValueObject(string member, string accessibility) => $"{member}In{accessibility}Base";

    private static string Set(string member, string accessibility) => $"SetWith{member}In{accessibility}Base";

    /// <summary>The value objects, for the consumer's declarations build.</summary>
    public static string ValueObjectSource()
    {
        var source = new StringBuilder(
            """
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            public enum AccessibilityFault { Negative }

            // A value object nested in its base record sees the base's private members, as C# does: refused.
            public abstract record EnclosingBase
            {
                private static int MaxValue => 0;

                public sealed partial record NestedInItsBase : EnclosingBase, IValue<int>;
            }

            // A library's private and internal members, which no project but the library's sees.
            public sealed partial record OverALibrarysHiddenMembers : Library.HiddenMembersBase<OverALibrarysHiddenMembers>, IValue<int>;

            """);

        foreach (var (member, declaration, validated, _) in ValueObjectMembers)
        {
            foreach (var (accessibility, keyword) in Accessibilities)
            {
                var type = ValueObject(member, accessibility);
                source.AppendLine($"public abstract record {type}Record<TSelf> where TSelf : notnull {{ {keyword} {declaration} }}");
                source.AppendLine(validated
                    ? $"public sealed partial record {type} : {type}Record<{type}>, IValidatedValue<{type}, int, AccessibilityFault> {{ public static Result<{type}, AccessibilityFault> Create(int value) => throw new System.NotSupportedException(); }}"
                    : $"public sealed partial record {type} : {type}Record<{type}>, IValue<int>;");
            }
        }

        return source.ToString();
    }

    /// <summary>The closed sets, for the consumer's refused-sets build, where a refused set's CS0535 is expected.</summary>
    public static string SetSource()
    {
        var source = new StringBuilder(
            """
            using CodoMetis.TypeKit.ClosedSets;

            namespace Consumer;

            public sealed partial class SetOverALibrarysHiddenMembers : Library.HiddenSetMembersBase<SetOverALibrarysHiddenMembers>, IClosedSet<SetOverALibrarysHiddenMembers> { public static readonly SetOverALibrarysHiddenMembers A = new(); }

            """);

        foreach (var (member, declaration) in SetMembers)
        {
            foreach (var (accessibility, keyword) in Accessibilities)
            {
                var type = Set(member, accessibility);
                source.AppendLine($"public abstract class {type}Class<TSelf> {{ {keyword} {declaration} }}");
                source.AppendLine($"public sealed partial class {type} : {type}Class<{type}>, IClosedSet<{type}> {{ public static readonly {type} A = new(); }}");
            }
        }

        return source.ToString();
    }

    /// <summary>What the library declares, appended to its file-scoped namespace, which the consumer cannot see.</summary>
    public const string LibrarySource =
        """

        public abstract record HiddenMembersBase<TSelf> where TSelf : notnull
        {
            private static int MaxValue => 0;
            internal static int MinValue => 0;
            private static TSelf Parse(string text, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            internal static bool TryParse(string? text, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            private int CompareTo(object? other) => 0;
            internal int CompareTo(TSelf? other) => 0;
            protected int Uses() => MaxValue + CompareTo(null) + (Parse("", null) is null ? 0 : 1);
        }

        public abstract class HiddenSetMembersBase<TSelf>
        {
            private static TSelf Parse(string name) => throw new System.NotSupportedException();
            internal static bool TryParse(string? name, out TSelf member) => throw new System.NotSupportedException();
            protected static TSelf Uses() => Parse("");
        }
        """;

    /// <summary>Each value object or set over a member it can see, with the error that refuses it and the member's name.</summary>
    public static TheoryData<string, string, string, string> Refused()
    {
        var rows = new TheoryData<string, string, string, string>();

        foreach (var (member, _, _, id) in ValueObjectMembers)
            foreach (var (accessibility, _) in Accessibilities.Where(accessibility => accessibility.Name != "Private"))
                rows.Add(accessibility, id, ValueObject(member, accessibility), $"{ValueObject(member, accessibility)}Record<{ValueObject(member, accessibility)}>.");

        foreach (var (member, _) in SetMembers)
            foreach (var (accessibility, _) in Accessibilities.Where(accessibility => accessibility.Name != "Private"))
                rows.Add(accessibility, "CMTK1011", Set(member, accessibility), $"{Set(member, accessibility)}Class<{Set(member, accessibility)}>.");

        rows.Add("private, seen from a nested type", "CMTK1011", "EnclosingBase.NestedInItsBase", "EnclosingBase.MaxValue in its base type EnclosingBase");
        return rows;
    }

    /// <summary>Each value object or set over a member it cannot see.</summary>
    public static TheoryData<string> Generated() =>
    [
        .. ValueObjectMembers.Select(member => ValueObject(member.Name, "Private")),
        .. SetMembers.Select(member => Set(member.Name, "Private")),
        "OverALibrarysHiddenMembers",
        "SetOverALibrarysHiddenMembers",
    ];
}

public sealed partial class BuildOutcomeTests
{
    public static TheoryData<string, string, string, string> SeenBaseMembers => BaseMemberAccessibility.Refused();

    public static TheoryData<string> HiddenBaseMembers => BaseMemberAccessibility.Generated();

    /// <summary>
    /// A base's member the value object or set can see, protected, internal or public, stands in for the generated
    /// one, and is refused naming the base, as for a public one (#197, #205).
    /// </summary>
    [Theory]
    [MemberData(nameof(SeenBaseMembers))]
    public void A_base_member_the_type_can_see_is_refused(string accessibility, string id, string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(member) && error.Message.Contains(" in its base type "),
                                      $"{id} on {type}, {accessibility}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// A base's private member is hidden from the value object or set, so it stands in for nothing: the type builds,
    /// as it did on 2.3.0, where 2.3.1's base-record checks refused it (#230). So does a library's internal one.
    /// </summary>
    [Theory]
    [MemberData(nameof(HiddenBaseMembers))]
    public void A_base_member_the_type_cannot_see_is_not_refused(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}'"), consumer.Output);
}
