using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

/// <summary>What a generated member is, for the check of a hand-written one beside it.</summary>
[CompileTime]
internal enum GeneratedMemberKind
{
    Method,
    Property,
    Field,
    NestedType
}

/// <summary>What a hand-written member of a generated method's signature is.</summary>
[CompileTime]
internal enum GeneratedMemberRole
{
    /// <summary>CMTK1011: the generators introduce it themselves.</summary>
    Refused,

    /// <summary>Kept in place of the generated one: <c>TryFrom</c>, <c>FromKnownGood</c>, <c>Revalidate</c>, <c>CompareTo(TSelf)</c>, <c>ToString()</c>.</summary>
    Seam,

    /// <summary>Answered by a rule of its own: <c>CompareTo(object)</c> by CMTK1008, a record's equality by the equality checks.</summary>
    Elsewhere
}

/// <summary>
/// A parameter of a generated method: its type, or a test for it where the type depends on the method (a
/// <c>Func&lt;TResult&gt;</c> of <c>Match</c>'s own type parameter), and whether it is by reference.
/// </summary>
[CompileTime]
internal sealed class GeneratedParameter
{
    private readonly IType? _type;
    private readonly Func<IType, IMethod, bool>? _matches;

    private GeneratedParameter(IType? type, Func<IType, IMethod, bool>? matches, bool byReference)
    {
        _type       = type;
        _matches    = matches;
        ByReference = byReference;
    }

    public bool ByReference { get; }

    public static GeneratedParameter Of(IType type) => new(type, null, byReference: false);

    public static GeneratedParameter Out(IType type) => new(type, null, byReference: true);

    public static GeneratedParameter Matching(Func<IType, IMethod, bool> matches) => new(null, matches, byReference: false);

    public bool Matches(IParameter parameter, IMethod method) =>
        parameter.RefKind != RefKind.None == ByReference
     && (_type is not null ? MemberIdentity.SameType(parameter.Type, _type) : _matches!(parameter.Type, method));
}

/// <summary>
/// One member the generators introduce: its name, kind and signature, which both its introduction and the check
/// of a hand-written member beside it read, and the contract it implements (decision 128). Made by
/// <see cref="GeneratedMembers"/>.
/// </summary>
[CompileTime]
internal sealed class GeneratedMember
{
    internal GeneratedMember(string name, GeneratedMemberKind kind, GeneratedMemberRole role, INamedType contract, string contractMember, int arity = 0, IReadOnlyList<GeneratedParameter>? parameters = null, bool clashesByName = true)
    {
        Name           = name;
        Kind           = kind;
        Role           = role;
        Contract       = contract;
        ContractMember = contractMember;
        Arity          = arity;
        Parameters     = parameters ?? [];
        ClashesByName  = clashesByName;
    }

    public string Name { get; }

    public GeneratedMemberKind Kind { get; }

    public GeneratedMemberRole Role { get; }

    /// <summary>The interface the member implements, or the marker that promises it.</summary>
    public INamedType Contract { get; }

    /// <summary>The contract member's name, or the generated member's own where the contract declares none.</summary>
    public string ContractMember { get; }

    public int Arity { get; }

    public IReadOnlyList<GeneratedParameter> Parameters { get; }

    /// <summary>
    /// A member of its name of another kind takes its place. Not for a value object's <c>ToString</c>: a record
    /// synthesizes <c>ToString()</c>, and a property of that name is C#'s own error.
    /// </summary>
    public bool ClashesByName { get; }

    public bool HasSignatureOf(IMethod method) => MemberIdentity.HasSignature(method, Name, Arity, Parameters);

    /// <summary>
    /// Names the introduced method, checks that its template gives it this signature, and marks it. Called last in
    /// the build delegate, once the aspect has set the parameter types: a template whose signature drifted from
    /// this one fails every build that generates it, rather than let the check look for another method.
    /// </summary>
    public void Mark(IMethodBuilder method)
    {
        method.Name = Name;

        if (Kind != GeneratedMemberKind.Method || !HasSignatureOf(method))
            throw new InvalidOperationException($"The generated {Name} has the signature {method.ToDisplayString()}, which is not the one GeneratedMembers declares for it.");

        AddAttributes(method);
    }

    /// <inheritdoc cref="Mark(IMethodBuilder)"/>
    public void Mark(IPropertyBuilder property)
    {
        property.Name = Name;

        if (Kind != GeneratedMemberKind.Property)
            throw new InvalidOperationException($"The generated {Name} is a property, which GeneratedMembers does not declare it as.");

        AddAttributes(property);
    }

    /// <summary>Checks that the introduced field has this name, and marks it.</summary>
    public void Mark(IFieldBuilder field)
    {
        if (Kind != GeneratedMemberKind.Field || field.Name != Name)
            throw new InvalidOperationException($"The generated field {field.Name} is not the {Name} GeneratedMembers declares.");

        AddAttributes(field);
    }

    /// <summary>Checks that the introduced nested type has this name, and marks it.</summary>
    public void Mark(INamedTypeBuilder type)
    {
        if (Kind != GeneratedMemberKind.NestedType || type.Name != Name)
            throw new InvalidOperationException($"The generated type {type.Name} is not the {Name} GeneratedMembers declares.");

        AddAttributes(type);
    }

    private void AddAttributes(IDeclarationBuilder declaration)
    {
        declaration.AddAttribute(CodeAnnotations.CompilerGenerated);
    }
}
