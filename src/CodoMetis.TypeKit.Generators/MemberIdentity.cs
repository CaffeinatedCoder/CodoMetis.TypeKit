using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// The one answer to "is this member the user wrote the one a generator introduces?", for every check of a
/// hand-written member (decision 128): C#'s overloading rules, and what Metalama refuses to introduce beside.
/// </summary>
/// <remarks>
/// <para>
/// A method's signature is its name, its arity, and each parameter's type without its nullable annotation and
/// whether it is by reference: a generic <c>Parse&lt;TFormat&gt;(string, IFormatProvider?)</c>, a
/// <c>From(in int)</c> and a <c>TryParse</c> whose last parameter is by value are other methods, which the
/// generated ones sit beside (#226). <c>ref</c> or <c>in</c> against <c>out</c> is no overload in C# (CS0663),
/// and nullability none at all, so both are the same method. <c>int?</c> is <c>Nullable&lt;int&gt;</c>, another
/// type, so <c>TryFrom(int?)</c> is another method than <c>TryFrom(int)</c>. The return type and whether the
/// method is static are not part of it.
/// </para>
/// <para>
/// Between kinds, a member that is not a method takes its name from every member of the type, and from a base
/// type's for a method: Metalama introduced no method beside a property or field of its name in the type or a
/// base (LAMA0506), and C# no member beside a method of its name in the type (CS0102), while a base's method of
/// a generated property's name is hidden by it and builds (measured 2026-10-31).
/// </para>
/// <para>
/// Only what the value object can see counts: a base's private member is hidden from it, and the generated one
/// sits beside it (#230).
/// </para>
/// </remarks>
[CompileTime]
internal static class MemberIdentity
{
    /// <summary>
    /// <paramref name="declared"/> and <paramref name="expected"/> are one parameter type: equal without a nullable
    /// reference annotation. A nullable value type is <c>Nullable&lt;T&gt;</c>, which stays another type.
    /// </summary>
    public static bool SameType(IType declared, IType expected) => WithoutAnnotation(declared).Equals(WithoutAnnotation(expected));

    private static IType WithoutAnnotation(IType type) => type.IsReferenceType == false ? type : type.ToNonNullable();

    /// <summary><paramref name="method"/> has the signature <paramref name="name"/>, <paramref name="arity"/> and <paramref name="parameters"/>.</summary>
    public static bool HasSignature(IMethod method, string name, int arity, IReadOnlyList<GeneratedParameter> parameters) =>
        method.Name == name
     && method.TypeParameters.Count == arity
     && method.Parameters.Count == parameters.Count
     && method.Parameters.Zip(parameters, (declared, expected) => expected.Matches(declared, method)).All(same => same);

    /// <summary>A method without type parameters whose parameters are all by value, as every generated method is but <c>TryParse</c>, <c>TryFormat</c> and <c>Match</c>.</summary>
    public static bool IsByValueAndNotGeneric(IMethod method) =>
        method.TypeParameters.Count == 0 && method.Parameters.All(parameter => parameter.RefKind == RefKind.None);

    /// <summary>
    /// The members of <paramref name="type"/>, the value object or one of its bases, that <paramref name="seenFrom"/>
    /// can see and that take <paramref name="generated"/>'s place: a method of its signature, unless it is a seam,
    /// which is kept, or a refusal of its own answers it; a member of its name of another kind.
    /// </summary>
    /// <remarks>
    /// An explicit implementation is never one: the interface mapping answers it (<see cref="ValueObjectDeclaration.ImplementationsOf"/>).
    /// Neither is what the compiler declares implicitly, which a hand-written member cannot be.
    /// </remarks>
    public static IEnumerable<IMemberOrNamedType> DeclaredInItsPlace(INamedType type, INamedType seenFrom, GeneratedMember generated)
    {
        var own = type.Equals(seenFrom);

        foreach (var member in type.Fields.Concat<IMember>(type.Properties).Concat(type.Events))
        {
            if (member is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false } && member.Name == generated.Name && member.IsAccessibleFrom(seenFrom) && generated.ClashesByName)
                yield return member;
        }

        foreach (var nested in type.Types)
        {
            if (nested.Name == generated.Name && nested.IsAccessibleFrom(seenFrom) && generated.ClashesByName)
                yield return nested;
        }

        foreach (var method in type.Methods)
        {
            if (method is { IsImplicitlyDeclared: true } or { IsExplicitInterfaceImplementation: true } || method.Name != generated.Name || !method.IsAccessibleFrom(seenFrom)) continue;

            if (generated.Kind == GeneratedMemberKind.Method
                    ? generated.Role == GeneratedMemberRole.Refused && generated.HasSignatureOf(method)
                    : own)
                yield return method;
        }
    }
}
