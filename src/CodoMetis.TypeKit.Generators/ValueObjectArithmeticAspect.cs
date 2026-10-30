using System.Numerics;
using CodoMetis.TypeKit.CompilerServices;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>An arithmetic operator interface, the operators it declares, and the helpers that call the wrapped type's.</summary>
[CompileTime]
internal sealed class ArithmeticOperator
{
    public required OperatorKind  Kind          { get; init; }
    public required OperatorKind? CheckedKind   { get; init; }
    public required string        Helper        { get; init; }
    public required string?       CheckedHelper { get; init; }

    /// <summary>Two operands, <c>TSelf</c> and <c>TOther</c>, or one.</summary>
    public required bool IsBinary { get; init; }

    /// <summary>The operator interface <paramref name="definition"/> is, or <see langword="null"/> for any other interface.</summary>
    public static ArithmeticOperator? Of(INamedType definition)
    {
        ArithmeticOperator Binary(OperatorKind kind, OperatorKind? checkedKind, string helper, string? checkedHelper) =>
            new() { Kind = kind, CheckedKind = checkedKind, Helper = helper, CheckedHelper = checkedHelper, IsBinary = true };

        ArithmeticOperator Unary(OperatorKind kind, OperatorKind? checkedKind, string helper, string? checkedHelper) =>
            new() { Kind = kind, CheckedKind = checkedKind, Helper = helper, CheckedHelper = checkedHelper, IsBinary = false };

        if (definition.Equals(typeof(IAdditionOperators<,,>).ToNamedType()))
            return Binary(OperatorKind.Addition, OperatorKind.CheckedAddition, nameof(GeneratedArithmetic.Add), nameof(GeneratedArithmetic.CheckedAdd));
        if (definition.Equals(typeof(ISubtractionOperators<,,>).ToNamedType()))
            return Binary(OperatorKind.Subtraction, OperatorKind.CheckedSubtraction, nameof(GeneratedArithmetic.Subtract), nameof(GeneratedArithmetic.CheckedSubtract));
        if (definition.Equals(typeof(IMultiplyOperators<,,>).ToNamedType()))
            return Binary(OperatorKind.Multiplication, OperatorKind.CheckedMultiply, nameof(GeneratedArithmetic.Multiply), nameof(GeneratedArithmetic.CheckedMultiply));
        if (definition.Equals(typeof(IDivisionOperators<,,>).ToNamedType()))
            return Binary(OperatorKind.Division, OperatorKind.CheckedDivision, nameof(GeneratedArithmetic.Divide), nameof(GeneratedArithmetic.CheckedDivide));
        if (definition.Equals(typeof(IModulusOperators<,,>).ToNamedType()))
            return Binary(OperatorKind.Modulus, null, nameof(GeneratedArithmetic.Remainder), null);
        if (definition.Equals(typeof(IUnaryNegationOperators<,>).ToNamedType()))
            return Unary(OperatorKind.UnaryNegation, OperatorKind.CheckedUnaryNegation, nameof(GeneratedArithmetic.Negate), nameof(GeneratedArithmetic.CheckedNegate));
        if (definition.Equals(typeof(IUnaryPlusOperators<,>).ToNamedType()))
            return Unary(OperatorKind.UnaryPlus, null, nameof(GeneratedArithmetic.Plus), null);

        return null;
    }
}

/// <summary>What one generated operator's template needs.</summary>
[CompileTime]
internal sealed class ArithmeticOperatorArguments
{
    /// <summary>The <c>GeneratedArithmetic</c> method, with its type arguments: the wrapped type's operator.</summary>
    public required IMethod Helper { get; init; }

    /// <summary>The right operand is the value object, so its wrapped value is passed; otherwise the operand itself.</summary>
    public required bool UnwrapsRight { get; init; }

    /// <summary>The private constructor, where the result is the value object, built from the wrapped type's result.</summary>
    public required IConstructor? Constructor { get; init; }

    /// <summary>A record class: an operand of its type can be null.</summary>
    public required bool ChecksNull { get; init; }
}

/// <summary>
/// The arithmetic operators of the operator interfaces a plain value object declares: <c>+</c>,
/// <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c> and unary <c>-</c> and <c>+</c>, each with its checked form where
/// the interface has one. Each is the wrapped type's own, called through <c>GeneratedArithmetic</c>.
/// </summary>
/// <remarks>
/// <para>
/// Opt in, never by default: an interface the value object declares, such as
/// <c>IAdditionOperators&lt;Money, Money, Money&gt;</c>, where the wrapped type implements the same
/// interface with the value object replaced by itself (<c>IAdditionOperators&lt;decimal, decimal,
/// decimal&gt;</c>). An operand or a result of another type is passed through as it is, so
/// <c>IMultiplyOperators&lt;Money, decimal, Money&gt;</c> scales and
/// <c>IDivisionOperators&lt;Money, Money, decimal&gt;</c> is a ratio. Where the wrapped type does not match,
/// nothing is generated and the declaration fails as it did before, with CS0535.
/// </para>
/// <para>
/// Never for a validated value object: an operator cannot return a <c>Result</c>, and a sum outside the
/// range <c>Create</c> accepts would have to throw or skip it.
/// </para>
/// <para>
/// An operator the value object already has is kept, with its checked form: one it declares for the
/// same operands, or an explicit implementation of the interface, its own or a base record's. Code that
/// implemented these interfaces by hand before compiles unchanged, so this is no refusal (decision 121).
/// </para>
/// </remarks>
internal sealed partial class ValueObjectArithmeticAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        if (state.Kind == ValueObjectKind.Validated)
        {
            builder.SkipAspect();
            return;
        }

        var target      = builder.Target;
        var valueType   = state.ValueType.GetTarget();
        var constructor = state.PrivateConstructor.GetTarget();
        var helpers     = typeof(GeneratedArithmetic).ToNamedType();

        // Two interfaces asking for one operator, as IDivisionOperators<Money, Money, Money> beside
        // IDivisionOperators<Money, Money, decimal> would, get it once; C# allows no second, and the
        // other interface stays to be implemented explicitly by hand.
        List<(OperatorKind Kind, IType? Right)> introduced = [];

        foreach (var declared in target.AllImplementedInterfaces)
        {
            if (ArithmeticOperator.Of(declared.Definition) is not { } @operator || !declared.TypeArguments[0].Equals(target))
                continue;

            var right  = @operator.IsBinary ? declared.TypeArguments[1] : null;
            var result = declared.TypeArguments[@operator.IsBinary ? 2 : 1];

            // The wrapped type's own interface: the value object replaced by the wrapped type throughout.
            IType Unwrapped(IType type) => type.Equals(target) ? valueType : type;

            IType[] matching = right is null ? [valueType, Unwrapped(result)] : [valueType, Unwrapped(right), Unwrapped(result)];

            if (!valueType.IsConvertibleTo(declared.Definition.MakeGenericInstance(matching)))
                continue;

            if (DeclaredByHand(target, declared, @operator, right) || introduced.Contains((@operator.Kind, right)))
                continue;

            introduced.Add((@operator.Kind, right));

            Introduce(@operator.Kind, @operator.Helper);

            if (@operator is { CheckedKind: { } checkedKind, CheckedHelper: { } checkedHelper })
                Introduce(checkedKind, checkedHelper);

            void Introduce(OperatorKind kind, string helperName)
            {
                var helper = helpers.Methods.Single(method => method.Name == helperName).MakeGenericInstance(matching);

                var tags = new ArithmeticOperatorArguments
                {
                    Helper       = helper,
                    UnwrapsRight = right is not null && right.Equals(target),
                    Constructor  = result.Equals(target) ? constructor : null,
                    ChecksNull   = target.IsReferenceType == true
                };

                builder.IntroduceMethod(
                    right is null ? nameof(UnaryOperatorTemplate) : nameof(BinaryOperatorTemplate),
                    IntroductionScope.Static,
                    OverrideStrategy.Ignore,
                    method =>
                    {
                        method.Parameters[0].Type = target;
                        if (right is not null) method.Parameters[1].Type = right;
                        method.ReturnType   = result;
                        method.OperatorKind = kind;
                        method.AddAttribute(CodeAnnotations.CompilerGenerated);
                    },
                    tags: tags);
            }
        }

        if (introduced.Count == 0)
            builder.SkipAspect();
    }

    /// <summary>
    /// The value object has the operator already, which is kept with its checked form: it declares one of
    /// either kind for the same operands, whatever it returns, or it or a base record implements the
    /// interface explicitly. A checked operator generated beside a hand-written unchecked one would do the
    /// wrapped type's arithmetic in a checked context and the user's everywhere else, and a plain one
    /// generated beside a hand-written checked one, which C# refuses alone (CS9025), the other way round.
    /// </summary>
    internal static bool DeclaredByHand(INamedType target, INamedType declared, ArithmeticOperator @operator, IType? right)
    {
        foreach (var method in target.Methods)
        {
            if (method is { IsImplicitlyDeclared: true } or { IsExplicitInterfaceImplementation: true }) continue;
            if (method.OperatorKind != @operator.Kind && method.OperatorKind != @operator.CheckedKind) continue;

            if (right is null ? method.Parameters is [{ } only] && only.Type.Equals(target)
                              : method.Parameters is [{ } left, { } other] && left.Type.Equals(target) && other.Type.Equals(right))
                return true;
        }

        return ValueObjectDeclaration.ImplementsExplicitly(target, declared);
    }
}
