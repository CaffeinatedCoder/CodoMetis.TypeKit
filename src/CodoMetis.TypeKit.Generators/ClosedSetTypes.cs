using System.ComponentModel;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>What the fabric and the aspect know about closed sets: a class implementing <c>IClosedSet&lt;TSelf&gt;</c>, directly or through another interface.</summary>
[CompileTime]
internal static class ClosedSetTypes
{
    public static bool IsClosedSet(INamedType type) =>
        type.TypeKind == TypeKind.Class && Marker(type) is not null;

    /// <summary>The <c>IClosedSet&lt;TSelf&gt;</c> the type implements, or null.</summary>
    public static INamedType? Marker(INamedType type) =>
        type.AllImplementedInterfaces.FirstOrDefault(i => i.IsGeneric && i.Definition.Equals(TypeFactory.GetNamedType(typeof(IClosedSet<>))));

    /// <summary>The members: every public static readonly field of the set's own type, in declaration order.</summary>
    public static IReadOnlyList<IField> Members(INamedType type) =>
        [.. type.Fields.Where(f => f.IsStatic && f.Accessibility == Accessibility.Public && f.Type.Equals(type) && f.Writeability == Writeability.ConstructorOnly)];

    /// <summary>The set's name with its containing types, dotted: <c>Order.Status</c> for a set nested in <c>Order</c>.</summary>
    public static string NestingChain(INamedType set) =>
        set.DeclaringType is { } declaring ? NestingChain(declaring) + "." + set.Name : set.Name;

    /// <summary>The expression that makes <c>All</c>: the members in declaration order, wrapped read-only.</summary>
    public static string AllExpression(INamedType set, IReadOnlyList<IField> members) =>
        $"global::System.Array.AsReadOnly(new {ValueObjectTypes.SourceName(set)}[] {{ {string.Join(", ", members.Select(member => $"{ValueObjectTypes.SourceName(set)}.{member.Name}"))} }})";

    /// <summary>The names of what the generators introduce into the set itself; nothing else in it may have one (CMTK1011, CMTK1021).</summary>
    public static IReadOnlyList<string> GeneratedNames(INamedType set) =>
        ["Name", "QualifiedName", "All", "Match", "Parse", "TryParse", "ToString", "_name", "_all", $"{set.Name}JsonConverter", $"{set.Name}TypeConverter"];

    /// <summary>
    /// The name of <paramref name="member"/>'s parameter of <c>Match</c>: its own, with the first letter
    /// lower-cased. A keyword is escaped where the code is written, so <c>New</c> is <c>@new:</c> at a
    /// call. The analyzer's CMTK0012 names the arguments by the same rule.
    /// </summary>
    public static string ParameterName(IField member) => char.ToLowerInvariant(member.Name[0]) + member.Name[1..];

    /// <summary>
    /// Each member that collides with one declared before it, with that one and why: by
    /// <c>TryParse(name, ignoreCase: true)</c>'s comparison (<see cref="StringComparison.OrdinalIgnoreCase"/>),
    /// by <see cref="ParameterName"/>, the transform that names <c>Match</c>'s parameters, or by both.
    /// The reason holds <c>{0}</c> where the earlier member is named. The two transforms differ: the
    /// Kelvin sign (U+212A) lower-cases to <c>k</c>, so it names the same parameter as <c>K</c>, while
    /// ignoring case ordinally keeps the two apart (it upper-cases, and the sign has no upper case of
    /// its own).
    /// </summary>
    public static IEnumerable<(IField Member, IField Earlier, string Reason)> CaseCollisions(IReadOnlyList<IField> members)
    {
        for (var index = 1; index < members.Count; index++)
        {
            var member = members[index];

            foreach (var earlier in members.Take(index))
            {
                var parse = string.Equals(member.Name, earlier.Name, StringComparison.OrdinalIgnoreCase);
                var match = string.Equals(ParameterName(member), ParameterName(earlier), StringComparison.Ordinal);

                if (!parse && !match) continue;

                yield return (member, earlier,
                    parse && match ? "differs from {0} only in case, which Match's parameters and TryParse(name, ignoreCase: true) cannot tell apart"
                    : parse ? "differs from {0} only in case, which TryParse(name, ignoreCase: true) cannot tell apart"
                    : $"gives Match the parameter name '{ParameterName(member)}' that {{0}} gives it too");
                break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="member"/> is initialized with <c>new</c> of the set where it is declared,
    /// and with nothing else: <c>new()</c>, <c>new(1, null)</c>, <c>new OrderStatus()</c>, each with an
    /// optional object initializer. Not <c>= Open</c>, which makes one instance two members whose names
    /// overwrite each other, nor <c>new OrderStatus().Canonical()</c> or any other expression that only
    /// starts with a creation, nor a call, nor no initializer at all, which leaves the member null until
    /// a static constructor runs.
    /// </summary>
    /// <remarks>
    /// Read from the initializer's source text, since the code model exposes no syntax kind. The type
    /// after <c>new</c> is compared by its last name; a <c>using</c> alias of the set is refused, which
    /// is loud and names the member.
    /// </remarks>
    public static bool IsCreatedWhereDeclared(IField member, INamedType set)
    {
        if (member.InitializerExpression is not ISourceExpression source) return false;

        var text = WithoutComments(source.AsString).Trim();
        if (text.Length <= 3 || !text.StartsWith("new", StringComparison.Ordinal) || IsIdentifierPart(text[3])) return false;

        var at = Skip(text, 3);
        if (at < text.Length && text[at] != '(' && text[at] != '{')
        {
            var nameStart = at;
            while (at < text.Length && (IsIdentifierPart(text[at]) || text[at] is '.' or ':' or '@')) at++;

            var typeName = text.Substring(nameStart, at - nameStart);
            if (typeName.Substring(typeName.LastIndexOfAny(['.', ':']) + 1).TrimStart('@') != set.Name) return false;

            at = Skip(text, at);
        }

        // An argument list, an object initializer, or both in that order, and then the end of the text.
        var groups = 0;
        if (at < text.Length && text[at] == '(') { at = Skip(text, AfterGroup(text, at)); groups++; }
        if (at < text.Length && text[at] == '{') { at = Skip(text, AfterGroup(text, at)); groups++; }

        return groups > 0 && at == text.Length;
    }

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int Skip(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        return at;
    }

    /// <summary>The index after the bracket that closes the one at <paramref name="open"/>, past nested brackets and literals; the length when it never closes.</summary>
    private static int AfterGroup(string text, int open)
    {
        var depth = 0;
        for (var at = open; at < text.Length; at++)
        {
            switch (text[at])
            {
                case '(' or '{' or '[':
                    depth++;
                    break;
                case ')' or '}' or ']':
                    if (--depth == 0) return at + 1;
                    break;
                case '"' or '\'':
                    at = AfterLiteral(text, at);
                    break;
            }
        }

        return text.Length;
    }

    /// <summary>The index of the quote that ends the string or character literal starting at <paramref name="quote"/>; a verbatim string doubles its quotes.</summary>
    private static int AfterLiteral(string text, int quote)
    {
        var verbatim = text[quote] == '"' && quote > 0 && (text[quote - 1] == '@' || (quote > 1 && text[quote - 2] == '@'));
        for (var at = quote + 1; at < text.Length; at++)
        {
            if (!verbatim && text[at] == '\\') { at++; continue; }
            if (text[at] != text[quote]) continue;
            if (verbatim && at + 1 < text.Length && text[at + 1] == '"') { at++; continue; }
            return at;
        }

        return text.Length;
    }

    /// <summary>The text with its <c>//</c> and <c>/* */</c> comments blanked, literals left as they are.</summary>
    private static string WithoutComments(string text)
    {
        var chars = text.ToCharArray();
        for (var at = 0; at < chars.Length; at++)
        {
            if (chars[at] is '"' or '\'')
            {
                at = AfterLiteral(text, at);
            }
            else if (chars[at] == '/' && at + 1 < chars.Length && chars[at + 1] is '/' or '*')
            {
                var block = chars[at + 1] == '*';
                var end = block ? text.IndexOf("*/", at + 2, StringComparison.Ordinal) : text.IndexOf('\n', at);
                end = end < 0 ? chars.Length : block ? end + 2 : end;
                for (var blank = at; blank < end; blank++) chars[blank] = ' ';
                at = end - 1;
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// The members the generators introduce, written by hand: a property, field, event or nested type
    /// of a generated name, a method with a generated signature, an explicit implementation of
    /// <c>IClosedSet&lt;TSelf&gt;</c>, <c>IParsable&lt;TSelf&gt;</c> declared, or an attribute the
    /// generators put on the set. Each failed the aspect with a Metalama error naming no fix. An
    /// overload of another signature is the set's own and is kept, as is <c>ToString()</c>, the seam. A
    /// member of the set with a generated name is CMTK1021's, which says to rename it.
    /// </summary>
    /// <remarks>
    /// An explicit implementation of <c>IClosedSet&lt;TSelf&gt;</c> in an interface the set implements is the
    /// set's too: the generated <c>Name</c> and <c>TryParse</c> replaced an interface's without a word (#191,
    /// measured 2026-10-30). <c>IParsable&lt;TSelf&gt;</c> declared through an interface failed the aspect as
    /// declaring it does (LAMA0512).
    /// <para>
    /// A base class's count as the set's own (#205): its <c>Parse</c>, <c>TryParse</c>, <c>Match</c> or <c>All</c>,
    /// and an <c>IParsable&lt;TSelf&gt;</c> it implements, failed the aspect (LAMA0500, LAMA0503, LAMA0512) naming
    /// nothing the user wrote, in this project and from another (measured 2026-10-30). A private member of a base,
    /// such as a <c>_name</c> field, is out of the set's sight and builds.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> GeneratedMembersDeclaredByHand(INamedType set, IReadOnlyList<IField> members)
    {
        var text = TypeFactory.GetType(SpecialType.String);
        var boolean = TypeFactory.GetType(SpecialType.Boolean);
        var parsable = TypeFactory.GetNamedType(typeof(IParsable<>)).WithTypeArguments(set);
        var memberNames = new HashSet<string>(members.Select(member => member.ToDisplayString()));
        var generatedNames = GeneratedNames(set);
        var bases = ValueObjectDeclaration.SelfAndBases(set).Skip(1).ToList();

        IEnumerable<string> Declared(Func<INamedType, IEnumerable<IMethod>> find) =>
            ValueObjectDeclaration.SelfAndBases(set).SelectMany(type => find(type).Select(method => type.Equals(set)
                ? ValueObjectDeclaration.Describe(method)
                : $"{ValueObjectDeclaration.Describe(method)} in its base type {type.ToDisplayString()}"));

        List<string> found =
        [
            .. ValueObjectDeclaration.MembersNamed(set, methods: false, [.. generatedNames]).Where(declared => !memberNames.Contains(declared)),
            .. bases.SelectMany(type => type.Fields.Concat<IMember>(type.Properties).Concat(type.Events)
                                            .Where(member => member is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false } && member.Accessibility != Accessibility.Private && generatedNames.Contains(member.Name))
                                            .Select(member => $"{member.ToDisplayString()} in its base type {type.ToDisplayString()}")),
            .. Declared(type => type.Methods.Where(method => IsOwn(method) && method.Name == "Parse" && (SameParameters(method, text) || SameParameters(method, text, boolean)))),
            .. Declared(type => type.Methods.Where(method => IsOwn(method) && method.Name == "TryParse" && (SameParameters(method, text, set) || SameParameters(method, text, boolean, set)))),
            .. Declared(type => type.Methods.Where(method => IsOwn(method) && IsGeneratedMatch(method, members.Count))),
            .. ValueObjectDeclaration.ImplementationsOf(set, [TypeFactory.GetNamedType(typeof(IClosedSet<>))]),
            .. set.ImplementedInterfaces.Where(implemented => implemented.Equals(parsable)).Select(implemented => $"the interface {implemented.ToDisplayString()}"),
            .. set.ImplementedInterfaces.Where(implemented => implemented.AllImplementedInterfaces.Any(inherited => inherited.Equals(parsable))).Select(implemented => $"the interface {parsable.ToDisplayString()} in its interface {implemented.ToDisplayString()}"),
            .. bases.Where(type => type.ImplementedInterfaces.SelectMany(implemented => implemented.AllImplementedInterfaces.Prepend(implemented)).Any(inherited => inherited.Equals(parsable)))
                    .Select(type => $"the interface {parsable.ToDisplayString()} in its base type {type.ToDisplayString()}"),
        ];

        return
        [
            .. found.Distinct(),
            .. set.Attributes.Where(attribute => attribute.Type.Equals(TypeFactory.GetNamedType(typeof(JsonConverterAttribute)))
                                              || attribute.Type.Equals(TypeFactory.GetNamedType(typeof(TypeConverterAttribute)))
                                              || attribute.Type.Definition.Equals(TypeFactory.GetNamedType(typeof(GeneratedClosedSetAttribute<>))))
                             .Select(attribute => $"the attribute {attribute.Type.Name.Substring(0, attribute.Type.Name.Length - nameof(Attribute).Length)}"),
        ];
    }

    private static bool IsOwn(IMethod method) => method is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false };

    /// <summary>Parameter types equal, nullability aside: Metalama refuses to introduce a method beside one of the same signature.</summary>
    private static bool SameParameters(IMethod method, params IType[] types) =>
        method.Parameters.Count == types.Length
     && method.Parameters.Select(parameter => parameter.Type.ToNonNullable()).Zip(types, (declared, expected) => declared.Equals(expected.ToNonNullable())).All(same => same);

    /// <summary>The generated <c>Match&lt;TResult&gt;</c>: one <c>Func&lt;TResult&gt;</c> per member.</summary>
    private static bool IsGeneratedMatch(IMethod method, int memberCount) =>
        method is { Name: "Match", TypeParameters.Count: 1 }
     && method.Parameters.Count == memberCount
     && method.Parameters.All(parameter => parameter.Type is INamedType { IsGeneric: true } func
                                        && func.Definition.Equals(TypeFactory.GetNamedType(typeof(Func<>)))
                                        && func.TypeArguments[0].Equals(method.TypeParameters[0]));

    /// <summary>
    /// Whether a <c>ToString()</c> is written by hand: the seam, kept in place of the generated one. A base class's
    /// <c>sealed</c> one is the seam too, as for a value object: C# keeps it in every derived class, and generating
    /// one failed the aspect (LAMA0502, #205). A base class's one that is not sealed is refused instead
    /// (<see cref="ValueObjectDeclaration.UnsealedInheritedToString"/>).
    /// </summary>
    public static bool DeclaresToString(INamedType set) => ValueObjectDeclaration.DeclaresToString(set);
}
