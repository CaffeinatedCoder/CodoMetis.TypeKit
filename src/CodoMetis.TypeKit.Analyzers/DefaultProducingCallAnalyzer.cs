using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0009: a call that returns the <c>default</c> of a value object, <c>Option</c>, <c>Result</c>
/// or <c>[RequireCustomInitialization]</c> struct when it has nothing to return.
/// </summary>
/// <remarks>
/// <para>
/// CMTK0001 sees <c>default</c> written out, and these calls write it for the caller:
/// <c>ProductCode.TryFrom(s).OrDefault()</c> is a validated value object that never passed
/// <c>Create</c>, and <c>ids.FirstOrDefault()</c> on an empty list an <c>OrderId</c> nobody made.
/// Reported: <c>Enumerable</c>, <c>Queryable</c> and <c>ImmutableArray</c> <c>FirstOrDefault</c>,
/// <c>LastOrDefault</c>, <c>SingleOrDefault</c>, <c>ElementAtOrDefault</c> and <c>DefaultIfEmpty</c>
/// without a default value; their asynchronous forms on <c>IAsyncEnumerable</c>
/// (<c>System.Linq.AsyncEnumerable</c>) and EF Core's on <c>IQueryable</c>, whose default comes back
/// in a task; <c>Find</c> and <c>FindLast</c> of <c>List&lt;T&gt;</c>, <c>Array</c> and
/// <c>ImmutableList&lt;T&gt;</c>; <c>Nullable&lt;T&gt;.GetValueOrDefault()</c> and
/// <c>GetValueOrDefault(dictionary, key)</c>, immutable dictionaries included, without a default value;
/// <c>Activator.CreateInstance&lt;T&gt;()</c>, <c>Activator.CreateInstance(typeof(T))</c> and
/// <c>RuntimeHelpers.GetUninitializedObject(typeof(T))</c>; and the package's own
/// <c>Option&lt;T&gt;.OrDefault()</c>.
/// </para>
/// <para>
/// Every method is resolved by symbol at compilation start, and a type the compilation does not
/// reference, such as EF Core's, adds nothing. An overload that takes the default value is not
/// reported: the caller chose it. The element type is judged as CMTK0001 judges a
/// <c>default</c>, type parameters constrained to a value object included. A warning, since code that
/// checks the sequence is not empty first is correct and the rule cannot tell.
/// </para>
/// <para>
/// In a project that declares value objects, Metalama runs analyzers on the source before weaving,
/// where the generated <c>TryFrom</c> does not bind and neither does <c>OrDefault</c> on it;
/// <c>X.TryFrom(…).OrDefault()</c> is recognised there by name.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultProducingCallAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.DefaultProducingCall,
        title: "Call produces a default instance",
        messageFormat: "'{0}' returns a default '{1}'{2}, an instance that passed no factory. {3}.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "FirstOrDefault, GetValueOrDefault, Option.OrDefault and their kind return default(T) when there is nothing to return. For a value object, Option, Result or [RequireCustomInitialization] struct that is an instance no factory produced, which CMTK0001 cannot see because 'default' is never written.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.DefaultProducingCall)
    );

    private const string TryFrom = "TryFrom";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCode.AnalysisFlags);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            var forms = Forms(start.Compilation, symbols);
            if (forms.Count == 0) return;

            start.RegisterOperationAction(operation => Analyze(operation, symbols, forms), OperationKind.Invocation);

            var orDefault = forms.FirstOrDefault(form => form.Value.Kind == Kind.OrDefault).Key;
            if (orDefault is not null) start.RegisterSyntaxNodeAction(node => AnalyzeUnbound(node, symbols, orDefault, forms[orDefault]), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(OperationAnalysisContext context, TypeKitSymbols symbols, Dictionary<IMethodSymbol, Form> forms)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method     = invocation.TargetMethod;

        if (!forms.TryGetValue(method.OriginalDefinition, out var form)) return;

        var produced = form.Produced switch
        {
            Produced.Returned     => method.ReturnType,
            Produced.TypeArgument => method.TypeArguments.FirstOrDefault(),
            _                     => (invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value as ITypeOfOperation)?.TypeOperand
        };

        if (produced is null) return;

        // FirstOrDefault<T> returns T?, which for an unconstrained T is T annotated, not Nullable<T>.
        if (produced is { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated }) produced = produced.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        if (symbols.DefaultRestriction(produced) is null) return;

        context.Report(Create(invocation.Syntax, method.Name, produced, form));
    }

    /// <summary>
    /// <c>ProductCode.TryFrom(s).OrDefault()</c> on a value object of the same project, where neither
    /// call binds (<see cref="TypeKitSymbols.GeneratedMemberCall"/>). A bound call is the operation path's.
    /// </summary>
    private static void AnalyzeUnbound(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols, IMethodSymbol orDefault, Form form)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation is not { Expression: MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax receiver } access, ArgumentList.Arguments.Count: 0 }
         || access.Name.Identifier.ValueText != orDefault.Name) return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not null) return;
        if (symbols.GeneratedMemberCall(receiver, TryFrom, context.SemanticModel, context.CancellationToken) is not { } valueObject) return;
        if (symbols.DefaultRestriction(valueObject) is null) return;

        context.Report(Create(invocation, orDefault.Name, valueObject, form));
    }

    private static Diagnostic Create(SyntaxNode call, string method, ITypeSymbol produced, Form form)
    {
        var type = produced.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return Diagnostic.Create(Rule, call.GetLocation(), method, type, form.Circumstance, string.Format(form.Advice, type));
    }

    /// <summary>
    /// Every method this rule reports, by its original definition, resolved from the compilation. An
    /// overload that takes a parameter of the type it returns takes the default value, and is left out.
    /// </summary>
    private static Dictionary<IMethodSymbol, Form> Forms(Compilation compilation, TypeKitSymbols symbols)
    {
        var forms = new Dictionary<IMethodSymbol, Form>(SymbolEqualityComparer.Default);

        const string nothingFound = " when nothing is found";
        const string pastTheEnd   = " for an index past the end";
        const string defaultValue = "Pass a default value";

        // FirstOrNone and friends would run a query on the client; a nullable projection is translated.
        const string nullableFirst = "Select a nullable first, as in Select(x => ({0}?)x), so that nothing found is null";

        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var queryable  = compilation.GetTypeByMetadataName("System.Linq.Queryable");

        // ImmutableArray's own FirstOrDefault and friends, which a call on an ImmutableArray binds to
        // before Enumerable's. They have no overload that takes a default value; Enumerable's does.
        var immutableArray = compilation.GetTypeByMetadataName("System.Linq.ImmutableArrayExtensions");

        foreach (var (name, advice, circumstance) in new[]
                 {
                     ("FirstOrDefault", "Use FirstOrNone, which returns an Option, or pass a default value", nothingFound),
                     ("LastOrDefault", "Use LastOrNone, which returns an Option, or pass a default value", nothingFound),
                     ("SingleOrDefault", defaultValue, nothingFound),
                     ("ElementAtOrDefault", "Use Skip(index).FirstOrNone(), which returns an Option", pastTheEnd),
                     ("DefaultIfEmpty", defaultValue, " for an empty sequence"),
                 })
        {
            var produced = name == "DefaultIfEmpty" ? Produced.TypeArgument : Produced.Returned;

            AddWithoutDefaultValue(forms, enumerable, name, new Form(Kind.Sequence, produced, circumstance, advice));
            AddWithoutDefaultValue(forms, immutableArray, name, new Form(Kind.Sequence, produced, circumstance, advice));
            AddWithoutDefaultValue(forms, queryable, name, new Form(Kind.Sequence, produced, circumstance, nullableFirst));
        }

        // The asynchronous forms, whose default comes back in a ValueTask<T> or Task<T>: T is their type
        // argument. System.Linq.AsyncEnumerable is .NET 10's, and the System.Linq.Async package declares a
        // class of the same name, so every match counts, as in TypeKitSymbols.Resolve. EF Core's are
        // there only where the project references it, and have no overload that takes a default value.
        var asyncEnumerables = compilation.GetTypesByMetadataName("System.Linq.AsyncEnumerable");
        var entityFramework  = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");

        foreach (var (name, advice, circumstance) in new[]
                 {
                     ("FirstOrDefaultAsync", defaultValue, nothingFound),
                     ("LastOrDefaultAsync", defaultValue, nothingFound),
                     ("SingleOrDefaultAsync", defaultValue, nothingFound),
                     ("ElementAtOrDefaultAsync", nullableFirst, pastTheEnd),
                     ("DefaultIfEmpty", defaultValue, " for an empty sequence"),
                 })
        {
            foreach (var asyncEnumerable in asyncEnumerables)
            {
                AddWithoutDefaultValue(forms, asyncEnumerable, name, new Form(Kind.Sequence, Produced.TypeArgument, circumstance, advice));
            }

            AddWithoutDefaultValue(forms, entityFramework, name, new Form(Kind.Sequence, Produced.TypeArgument, circumstance, nullableFirst));
        }

        // List<T>.Find and its kind return default(T) when nothing matches, as FirstOrDefault does.
        foreach (var (name, advice) in new[]
                 {
                     ("Find", "Use FirstOrNone(predicate), which returns an Option"),
                     ("FindLast", "Use LastOrNone(predicate), which returns an Option"),
                 })
        {
            foreach (var metadataName in new[]
                     {
                         "System.Collections.Generic.List`1",
                         "System.Array",
                         "System.Collections.Immutable.ImmutableList`1",
                         "System.Collections.Immutable.ImmutableList`1+Builder",
                     })
            {
                AddWithoutDefaultValue(forms, compilation.GetTypeByMetadataName(metadataName), name, new Form(Kind.Sequence, Produced.Returned, nothingFound, advice));
            }
        }

        AddWithoutDefaultValue(forms, compilation.GetSpecialType(SpecialType.System_Nullable_T), "GetValueOrDefault",
            new Form(Kind.Nullable, Produced.Returned, " for null", "Pass a default value, or check HasValue first"));

        // CollectionExtensions takes an IReadOnlyDictionary; ImmutableDictionary's takes an
        // IImmutableDictionary and binds first, and the immutable builders declare their own.
        foreach (var metadataName in new[]
                 {
                     "System.Collections.Generic.CollectionExtensions",
                     "System.Collections.Immutable.ImmutableDictionary",
                     "System.Collections.Immutable.ImmutableDictionary`2+Builder",
                     "System.Collections.Immutable.ImmutableSortedDictionary`2+Builder",
                 })
        {
            AddWithoutDefaultValue(forms, compilation.GetTypeByMetadataName(metadataName), "GetValueOrDefault",
                new Form(Kind.Dictionary, Produced.Returned, " for a missing key", "Use GetValueOrNone, which returns an Option, or pass a default value"));
        }

        const string factories = "Create it through one of its factories";
        var systemType = compilation.GetTypeByMetadataName("System.Type");

        foreach (var method in Methods(compilation.GetTypeByMetadataName("System.Activator"), "CreateInstance"))
        {
            // CreateInstance<T>(), and CreateInstance(Type) or (Type, bool nonPublic), which call no
            // constructor with arguments.
            if (method is { IsGenericMethod: true, Parameters.IsEmpty: true })
                forms[method] = new Form(Kind.Created, Produced.Returned, "", factories);
            else if (method.Parameters.Length is 1 or 2
                  && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, systemType)
                  && method.Parameters.Skip(1).All(parameter => parameter.Type.SpecialType == SpecialType.System_Boolean))
                forms[method] = new Form(Kind.Created, Produced.TypeOf, "", factories);
        }

        foreach (var method in Methods(compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeHelpers"), "GetUninitializedObject"))
        {
            forms[method] = new Form(Kind.Created, Produced.TypeOf, "", factories);
        }

        foreach (var method in symbols.OptionClassMethods("OrDefault"))
        {
            forms[method] = new Form(Kind.OrDefault, Produced.Returned, " for None", "Use Or(fallback) or Match, which say what None becomes");
        }

        return forms;
    }

    private static void AddWithoutDefaultValue(Dictionary<IMethodSymbol, Form> forms, INamedTypeSymbol? type, string name, Form form)
    {
        foreach (var method in Methods(type, name))
        {
            // The type that comes back when nothing is found: T of FirstOrDefault<T>, of Nullable<T> or of
            // List<T>.Find, TValue of GetValueOrDefault<TKey, TValue>, the element of DefaultIfEmpty's
            // sequence, the result of FirstOrDefaultAsync's task.
            var produced = method.ReturnType as ITypeParameterSymbol ?? method.TypeParameters.FirstOrDefault();
            if (produced is null || method.Parameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, produced))) continue;

            forms[method] = form;
        }
    }

    private static IEnumerable<IMethodSymbol> Methods(INamedTypeSymbol? type, string name) =>
        type?.GetMembers(name).OfType<IMethodSymbol>() ?? [];

    private enum Kind
    {
        Sequence,
        Nullable,
        Dictionary,
        Created,
        OrDefault
    }

    /// <summary>Where the produced type is read at a call.</summary>
    private enum Produced
    {
        /// <summary>The return type: <c>FirstOrDefault&lt;T&gt;</c>, <c>GetValueOrDefault</c>, <c>OrDefault</c>.</summary>
        Returned,

        /// <summary>
        /// The first type argument: <c>DefaultIfEmpty&lt;T&gt;</c>, which returns a sequence of it, and
        /// <c>FirstOrDefaultAsync&lt;T&gt;</c>, which returns a task of it.
        /// </summary>
        TypeArgument,

        /// <summary>The <c>typeof</c> passed first: <c>Activator.CreateInstance(typeof(T))</c>.</summary>
        TypeOf
    }

    /// <summary>How one method is reported.</summary>
    /// <param name="kind">Which family the method is.</param>
    /// <param name="produced">Where the produced type is read.</param>
    /// <param name="circumstance">When it returns the default, with a leading space, or empty when it always does.</param>
    /// <param name="advice">The replacement, with <c>{0}</c> for the type.</param>
    private sealed class Form(Kind kind, Produced produced, string circumstance, string advice)
    {
        public Kind Kind { get; } = kind;

        public Produced Produced { get; } = produced;

        public string Circumstance { get; } = circumstance;

        public string Advice { get; } = advice;
    }
}
