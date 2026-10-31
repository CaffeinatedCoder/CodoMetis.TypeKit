using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Every member the generators introduce carries <see cref="GeneratedMemberAttribute"/>, and no member written by hand
/// does, in this assembly and in the probes' (decision 128). It is how a member is told generated from hand-written
/// without a list of names, so a member introduced without it reads as the user's, and a check that asks would
/// refuse or keep it as one.
/// </summary>
public sealed class GeneratedMemberAttributeTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// What C# synthesizes for a record, which carries <c>[CompilerGenerated]</c> as the generated members do. Where the
    /// generators replace one in its own type (a value object's <c>ToString()</c>, and <c>Equals</c> and
    /// <c>GetHashCode()</c> over a base record), Metalama writes the override over the implicit declaration and applies
    /// no attribute to it, nor introduces one there (measured 2026-10-31), so it carries neither.
    /// </summary>
    private static bool IsRecordMember(MemberInfo member) =>
        member switch
        {
            MethodInfo method => (method.Name, method.GetParameters().Length) is ("ToString", 0) or ("Equals", 1) or ("GetHashCode", 0) or ("PrintMembers", 1)
                                                                               or ("op_Equality", 2) or ("op_Inequality", 2) or ("get_EqualityContract", 0),
            PropertyInfo property => property.Name == "EqualityContract",
            ConstructorInfo constructor => constructor.GetParameters() is [{ } original] && original.ParameterType == constructor.DeclaringType,
            _ => false
        };

    public static TheoryData<Type> Generated =>
    [
        .. new[] { typeof(ProbeId).Assembly, typeof(GeneratedMemberAttributeTests).Assembly }
           .SelectMany(assembly => assembly.GetTypes())
           .Where(type => type.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IValueObject<,>) || i.GetGenericTypeDefinition() == typeof(IClosedSet<>))))
           .Where(type => !type.IsInterface)
    ];

    [Fact]
    public void The_generated_types_are_found() => Generated.Count.ShouldBeGreaterThanOrEqualTo(80);

    /// <summary>
    /// The aspects put <c>[CompilerGenerated]</c> on what they introduce, and the attribute beside it: one without the
    /// other is a member introduced past <see cref="GeneratedMembers"/> or <c>CodeAnnotations.GeneratedMember</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Generated))]
    public void Every_generated_member_carries_the_attribute_and_no_hand_written_one_does(Type type)
    {
        var members = type.GetMembers(Declared)
                          .Where(member => member is not MethodInfo { IsSpecialName: true } method || method.Name.StartsWith("op_", StringComparison.Ordinal))
                          .Where(member => !member.Name.StartsWith('<'))
                          .Where(member => !IsRecordMember(member))
                          .Where(member => member is not FieldInfo field || !field.Name.EndsWith("k__BackingField", StringComparison.Ordinal));

        var wrong = members.Where(member => member.IsDefined(typeof(CompilerGeneratedAttribute), false) != member.IsDefined(typeof(GeneratedMemberAttribute), false))
                           .Select(member => $"{member.MemberType} {member}")
                           .ToList();

        wrong.ShouldBeEmpty($"{type} has members with [CompilerGenerated] and no [GeneratedMember], or the other way round");
    }

    /// <summary>The companion's extension methods are generated too, and name the property they read.</summary>
    [Fact]
    public void The_companion_s_methods_name_the_value_they_read()
    {
        var getValue = typeof(ProbeAmountExtensions).GetMethod(nameof(ProbeAmountExtensions.GetValue))!.GetCustomAttribute<GeneratedMemberAttribute>().ShouldNotBeNull();

        getValue.Contract.ShouldBe(typeof(IValueObject<ProbeAmount, decimal>));
        getValue.Member.ShouldBe(nameof(IValueObject<,>.Value));
    }

    /// <summary>A member a probe writes by hand, a seam among them, carries none: <c>Create</c>, and a hand-written <c>ToString()</c>.</summary>
    [Fact]
    public void A_hand_written_member_of_another_assembly_carries_none()
    {
        typeof(ProbeArticle).GetMethod(nameof(ProbeArticle.Create))!.IsDefined(typeof(GeneratedMemberAttribute), false).ShouldBeFalse();
        typeof(ProbeArticle).GetProperty(nameof(ProbeArticle.Value))!.IsDefined(typeof(GeneratedMemberAttribute), false).ShouldBeTrue();
    }
}
