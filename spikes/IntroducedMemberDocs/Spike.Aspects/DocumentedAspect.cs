using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace Spike.Aspects;

/// <summary>
/// Introduces one member of each kind, each from a template that carries an XML documentation
/// comment, declaratively and programmatically, so the build shows which of them reach the
/// consumer's woven code and its documentation file.
/// </summary>
public sealed class DocumentedAspect : TypeAspect
{
    /// <summary>Declarative method: introduced by [Introduce].</summary>
    /// <returns>One.</returns>
    [Introduce]
    public static int DeclaredMethod() => 1;

    /// <summary>Declarative property: introduced by [Introduce].</summary>
    [Introduce]
    public static int DeclaredProperty => 2;

    /// <summary>Programmatic method: introduced by IntroduceMethod.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    [Template]
    public static int ProgrammaticMethodTemplate(int value) => value;

    /// <summary>Programmatic property: introduced by IntroduceProperty.</summary>
    [Template]
    public int ProgrammaticPropertyTemplate => 3;

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        builder.IntroduceMethod(nameof(ProgrammaticMethodTemplate), IntroductionScope.Static, buildMethod: m =>
        {
            m.Name          = "ProgrammaticMethod";
            m.Accessibility = Accessibility.Public;
        });

        builder.IntroduceProperty(nameof(ProgrammaticPropertyTemplate), buildProperty: p =>
        {
            p.Name          = "ProgrammaticProperty";
            p.Accessibility = Accessibility.Public;
        });

        builder.IntroduceClass("NestedConverter", buildType: t => t.Accessibility = Accessibility.Public);
    }
}
