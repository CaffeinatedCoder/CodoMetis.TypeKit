using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectArithmeticAspect
{
    // The wrapped type's operator, through GeneratedArithmetic: a call through a type parameter
    // constrained to the interface, which never boxes, reaches an explicit implementation, and adds two
    // bytes as bytes where `left.Value + right.Value` added them as ints. A result of the value object's
    // type is wrapped by the private constructor, as From does, since a plain value object has no rules.
    // A record class's operand can be null, and is refused naming it rather than dereferenced.

    [Template]
    public static dynamic? BinaryOperatorTemplate(dynamic? left, dynamic? right)
    {
        var tag = (ArithmeticOperatorArguments)meta.Tags.Source!;

        if (meta.CompileTime(tag.ChecksNull))
        {
            ArgumentNullException.ThrowIfNull(left);

            if (meta.CompileTime(tag.UnwrapsRight))
                ArgumentNullException.ThrowIfNull(right);
        }

        // Inlined, not held in a local: a local of the template is a local of the woven operator.
        var leftOperand  = Operand("left.Value", tag.Helper.Parameters[0]);
        var rightOperand = Operand(tag.UnwrapsRight ? "right.Value" : "right", tag.Helper.Parameters[1]);

        if (meta.CompileTime(tag.Constructor is not null))
            return tag.Constructor!.Invoke(tag.Helper.Invoke(leftOperand.Value, rightOperand.Value));

        return tag.Helper.Invoke(leftOperand.Value, rightOperand.Value);
    }

    [Template]
    public static dynamic? UnaryOperatorTemplate(dynamic? value)
    {
        var tag = (ArithmeticOperatorArguments)meta.Tags.Source!;

        if (meta.CompileTime(tag.ChecksNull))
            ArgumentNullException.ThrowIfNull(value);

        var operand = Operand("value.Value", tag.Helper.Parameters[0]);

        if (meta.CompileTime(tag.Constructor is not null))
            return tag.Constructor!.Invoke(tag.Helper.Invoke(operand.Value));

        return tag.Helper.Invoke(operand.Value);
    }

    /// <summary>An operand typed as the helper's parameter takes it, so the woven call needs no cast.</summary>
    [CompileTime]
    private static IExpression Operand(string code, IParameter parameter) => ExpressionFactory.Parse(code, parameter.Type, false);
}
