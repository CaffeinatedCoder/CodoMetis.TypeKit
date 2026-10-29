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
/// CMTK0003: a <c>Result</c> or <c>Option</c> that a call returns, or a collection of them that the
/// statement made, dropped by an expression statement, awaited or not, by a conversion of its
/// <c>Task</c> to a plain <c>Task</c>, a method group's included, by <c>Wait</c>, or by a loop that
/// never reads them.
/// </summary>
/// <remarks>
/// <para>
/// A dropped result is an error nobody sees: <c>Email.Create(input);</c> validates and forgets the
/// verdict, <c>orders.Cancel(id);</c> may have failed. <c>_ = …</c> says the drop is intended.
/// CA1806 can require using a return value only per method name, not per return type.
/// </para>
/// <para>
/// A <c>Task&lt;Result&lt;…&gt;&gt;</c> converts implicitly to <c>Task</c>, which drops the result as
/// silently: <c>Task Cancel(Guid id) =&gt; orders.CancelAsync(id);</c>, <c>return svc.DoAsync();</c>
/// in a method returning <c>Task</c>, <c>Func&lt;Task&gt; f = () =&gt; svc.DoAsync();</c>. The implicit
/// conversion of a call's task is reported; an explicit <c>(Task)</c> cast says the drop is intended.
/// A call reached through a branch, <c>await (dry ? CancelAsync(id) : ShipAsync(id));</c>, a
/// <c>switch</c> expression or <c>held ?? CancelAsync(id)</c>, is each branch's call, reported at the branch. <c>ConfigureAwait</c> and
/// <c>WaitAsync</c> hand on the task they are called on: through them a call's task is still the
/// call's, and a task the caller holds, <c>await held.ConfigureAwait(false);</c>, drops nothing.
/// A <c>ValueTask&lt;T&gt;</c> has no conversion to <c>ValueTask</c>. A method group converts the same
/// way to a delegate that returns <c>Task</c>: <c>Func&lt;Guid, Task&gt; f = orders.CancelAsync;</c>, or
/// <c>bus.Subscribe&lt;Guid&gt;(orders.CancelAsync)</c> for a <c>Func&lt;T, Task&gt;</c> parameter. A cast
/// to the delegate type says that drop is intended.
/// </para>
/// <para>
/// A collection of results is dropped as silently: <c>await Task.WhenAll(ids.Select(orders.CancelAsync));</c>
/// awaits a <c>Result[]</c> nobody reads, and <c>ids.Select(orders.Cancel).ToList();</c> builds a list
/// of them and forgets it. A collection is an array, or a type that is or implements
/// <c>ICollection&lt;T&gt;</c> or <c>IReadOnlyCollection&lt;T&gt;</c>, of results, of tasks of them or of
/// key-value pairs whose value is one: its elements exist. A tuple that holds a result is held to the
/// same rule. A lazy <c>IEnumerable&lt;T&gt;</c> has run nothing, and CA1806 reports a LINQ query
/// that is dropped. The collection is reported when the statement made the results in it
/// (<see cref="MadeResults"/>). One that only passes on results the statement was given, as
/// <c>await Task.WhenAll(first, second)</c> over tasks in variables or <c>pending.ToList()</c> do,
/// leaves them where they were and is silent. A lazy sequence a call returns without being given any
/// result, <c>orders.CancelEach(ids)</c>, makes its results as it is enumerated, so materialising it
/// in the statement made them too.
/// </para>
/// <para>
/// <c>orders.CancelAsync(id).Wait();</c> blocks until the result exists and hands it to nobody, and
/// <c>Task.WaitAll(ids.Select(orders.CancelAsync))</c> converts the tasks it made to plain ones, as a
/// single task's conversion does. A <c>foreach</c> or <c>await foreach</c> over results its header made,
/// whose body never reads the iteration variable, drains them: an iteration variable named <c>_</c>
/// says the drop is intended.
/// </para>
/// <para>
/// <c>Tap</c>, <c>TapAsync</c>, <c>TapError</c> and <c>TapErrorAsync</c> return their receiver unchanged,
/// and so do the <c>Task</c> continuations of those names, so <c>result.Tap(log);</c> or
/// <c>await pending.TapAsync(log);</c> on a variable drops nothing the caller does not still hold. Nor
/// does a chain of them, <c>result.Tap(log).TapError(warn);</c>, or one on what a held task completes
/// with, <c>(await pending).Tap(log);</c>. On anything else, such as <c>Find(id).Tap(log);</c>, the
/// result is gone and is reported.
/// </para>
/// <para>
/// In a project that declares value objects, Metalama runs analyzers on the source before weaving,
/// where the generated <c>TryFrom</c> and <c>Revalidate</c> do not bind. An ignored
/// <c>X.TryFrom(…)</c> or <c>x.Revalidate()</c> is recognised there by name
/// (<see cref="TypeKitSymbols.GeneratedMemberCall"/>) as a statement, as the body of a method, local
/// function or accessor that returns nothing, or as the body of a lambda that returns nothing. A call
/// chained onto one, such as <c>X.TryFrom(s).Tap(log)</c>, does not bind either and is not seen.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredOutcomeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.IgnoredOutcome,
        title: "Result or Option ignored",
        messageFormat: "The {0} that '{1}' returns is ignored, so its outcome is never seen. Handle it, or discard it with '_ =' if nothing depends on it.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Result carries an error and an Option an absence; dropping either, or a collection of them, as a statement, by converting its Task to a plain Task, by waiting for it, or by a loop that never reads them, means nobody decides what happens then. Assign it to '_', or name a loop's variable '_', to make an intended drop explicit.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.IgnoredOutcome)
    );

    private const string TryFrom = "TryFrom";

    private const string Revalidate = "Revalidate";

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

            var tasks = new Tasks(
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"),
                start.Compilation.GetSpecialType(SpecialType.System_Collections_Generic_ICollection_T),
                start.Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IReadOnlyCollection_T),
                start.Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T),
                start.Compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1"),
                start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1"),
                start.Compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2"));

            start.RegisterOperationAction(operation => Analyze(operation, symbols, tasks), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(operation => AnalyzeConversion(operation, symbols, tasks), OperationKind.Conversion);
            start.RegisterOperationAction(operation => AnalyzeMethodGroup(operation, symbols, tasks), OperationKind.DelegateCreation);
            start.RegisterOperationAction(operation => AnalyzeWait(operation, symbols, tasks), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzeLoop(operation, symbols, tasks), OperationKind.Loop);
            start.RegisterSyntaxNodeAction(
                node => AnalyzeUnbound(node, symbols),
                SyntaxKind.ExpressionStatement,
                SyntaxKind.ArrowExpressionClause,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression);
        });
    }

    private static void Analyze(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var expression = ((IExpressionStatementOperation)context.Operation).Operation;

        // Only a value a call produced: an assignment or an increment is a statement for its effect.
        var awaited = expression is IAwaitOperation { Operation: var inner } ? inner : null;
        var dropped = Unwrap(awaited ?? expression);

        // await LoadAsync().ConfigureAwait(false): the result is LoadAsync's, and so is the name; on a
        // task still held, await held.WaitAsync(t), nothing is dropped.
        var passedOn = tasks.PassedOn(dropped);

        foreach (var invocation in Producers(dropped, tasks))
        {
            // The statement's own call keeps the statement's location, and its type unless a
            // pass-through stands between them; a branch's call keeps its own.
            var own  = ReferenceEquals(invocation, passedOn);
            var type = own && ReferenceEquals(invocation, dropped) ? expression.Type : awaited is null ? invocation.Type : tasks.Awaited(invocation.Type);

            var outcome = DroppedOutcome(type, invocation, symbols, tasks) ?? DroppedAssertion(invocation, awaited is not null, tasks);
            if (outcome is null) continue;

            context.Report(Diagnostic.Create(Rule, (own ? expression : invocation).Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
        }
    }

    /// <summary>
    /// The task that fails a test, dropped, or awaited where <paramref name="awaited"/> says so (the caller
    /// of a conversion to a plain <c>Task</c> and <c>Wait</c> wait for it too): <c>pending.ShouldBeSuccess();</c>
    /// asserts nothing, since the assertion fails the test only through its task (<see cref="IsPendingAssertion"/>).
    /// Nor does <c>Task.WhenAll(first.ShouldBeNone(), second.ShouldBeNone());</c> or <c>Task.Run(() =&gt;
    /// pending.ShouldBeNone());</c>, whose task faults when an assertion fails, or <c>Task.WhenAny</c> of them,
    /// awaited or not, whose task completes with the first to finish and leaves every failure unobserved, when
    /// the statement made an assertion it hands on (<see cref="Tasks.PassesOnFailure"/>). Awaited, a <c>WhenAll</c>
    /// or a <c>Run</c> observes only a failure it was handed as a fault: <c>await Task.WhenAll(Task.WhenAny(…))</c>
    /// completes with the faulted assertion as its value (<see cref="HandsOn"/>'s depth).
    /// </summary>
    private static string? DroppedAssertion(IInvocationOperation invocation, bool awaited, Tasks tasks)
    {
        var method = invocation.TargetMethod;

        var dropped = IsPendingAssertion(method, tasks) ? !awaited
                    : tasks.PassesOnFailure(method) is { } faults
                   && invocation.Arguments.Any(argument => MadeAssertions(argument.Value, tasks).Any(made => !awaited || !faults || made.Depth > 0));

        if (!dropped) return null;

        // Task.WhenEach(first.ShouldBeNone(), second.ShouldBeNone()) yields them unobserved.
        return tasks.ElementOfSequence(invocation.Type) is { } element ? $"sequence of {element.Name}s" : invocation.Type?.Name ?? "Task";
    }

    /// <summary>
    /// The assertions <paramref name="value"/> makes and hands on the task of, <see cref="Producers"/>' way, as
    /// an element of an array or a collection, or from a lambda or a method group a call that returns tasks was
    /// given: <c>new[] { a.ShouldBeNone(), b.ShouldBeNone() }</c>, <c>ids.Select(id =&gt; Find(id).ShouldBeNone())</c>,
    /// <c>pending.Select(OptionAssertions.ShouldBeNone)</c>, <c>ids.Select(async id =&gt; await Find(id).ShouldBeNone())</c>.
    /// An assertion assigned or discarded in it keeps its own task, and one awaited fails only the
    /// <c>async</c> lambda it is in, which is handed on in its place. Each comes with the depth
    /// <see cref="HandsOn"/> finds it at.
    /// </summary>
    private static IEnumerable<(IOperation Assertion, string Name, int Depth)> MadeAssertions(IOperation value, Tasks tasks)
    {
        foreach (var operation in value.DescendantsAndSelf())
        {
            var method = operation switch
            {
                IInvocationOperation call            => call.TargetMethod,
                IMethodReferenceOperation reference => reference.Method,
                _                                   => null,
            };

            if (method is not null && IsPendingAssertion(method, tasks) && HandsOn(operation, value, method, tasks) is { } depth) yield return (operation, method.Name, depth);
        }
    }

    /// <summary>
    /// How deep <paramref name="root"/> holds the failure of <paramref name="operation"/>'s assertion, or
    /// <see langword="null"/> where it is not handed on, or observed on the way: 0 where root's task faults with
    /// it, as a <c>WhenAll</c>'s or a <c>Run</c>'s does; one more for each task that completes with it as a value
    /// instead, a <c>WhenAny</c>'s or a <c>WhenEach</c>'s, or an <c>async</c> lambda's that returns it; one fewer
    /// for each <c>await</c> on the way that unwraps such a task. An <c>await</c> of the faulting task fails the
    /// <c>async</c> lambda it is in, which is followed in its place.
    /// </summary>
    private static int? HandsOn(IOperation operation, IOperation root, IMethodSymbol assertion, Tasks tasks)
    {
        var depth = 0;

        for (var current = operation; !ReferenceEquals(current, root);)
        {
            var parent = current.Parent!;

            // await Task.WhenAny(pending.ShouldBeNone(), …) completes with the assertion's task, still unobserved;
            // await pending.ShouldBeNone() fails the async lambda it is in, unless a catch on the way keeps the failure.
            if (parent is IAwaitOperation)
            {
                if (depth > 0)
                {
                    depth--;
                    current = parent;
                }
                else if (EnclosingAsyncFunction(current, root, assertion) is { } function) current = function;
                else return null;

                continue;
            }

            var handsOn = parent switch
            {
                IConversionOperation or IConditionalOperation or ICoalesceOperation or ISwitchExpressionArmOperation or ISwitchExpressionOperation => true,
                IArrayInitializerOperation or IArrayCreationOperation or ICollectionExpressionOperation or IArgumentOperation                     => true,
                IReturnOperation or IBlockOperation or IAnonymousFunctionOperation or IDelegateCreationOperation or ITranslatedQueryOperation     => true,
                IInvocationOperation call when ReferenceEquals(call.Instance, current)                                                              => tasks.PassesOn(call),
                IInvocationOperation call                                                                                                           => tasks.HoldsTasks(call.Type),
                _                                                                                                                                   => false,
            };

            if (!handsOn) return null;

            // async () => Task.WhenAny(…) and Task.WhenAny(…) complete with the tasks they were given.
            if (parent is IAnonymousFunctionOperation { Symbol.IsAsync: true }
             || parent is IInvocationOperation combinator && !ReferenceEquals(combinator.Instance, current) && tasks.PassesOnFailure(combinator.TargetMethod) == false)
                depth++;

            current = parent;
        }

        return depth;
    }

    /// <summary>
    /// The <c>async</c> lambda or anonymous method inside <paramref name="root"/> that <paramref name="operation"/>
    /// fails, with no <c>catch</c> between them that keeps the failure of <paramref name="assertion"/> (<see cref="Keeps"/>).
    /// A <c>catch</c> keeps only what its <c>try</c> block throws, not what its handlers or its <c>finally</c> do.
    /// </summary>
    private static IOperation? EnclosingAsyncFunction(IOperation operation, IOperation root, IMethodSymbol assertion)
    {
        for (IOperation? child = operation, current = operation.Parent; current is not null && !ReferenceEquals(current, root); child = current, current = current.Parent)
        {
            switch (current)
            {
                case ITryOperation attempt when ReferenceEquals(child, attempt.Body) && attempt.Catches.Any(clause => Keeps(clause, assertion)):
                    return null;
                case IAnonymousFunctionOperation function:
                    return function.Symbol.IsAsync ? function : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="clause"/> takes the failure of <paramref name="assertion"/> and keeps it: its type can
    /// hold the <c>TypeKitAssertionException</c> the assertion throws, and its handler can complete, where
    /// <c>catch { throw; }</c>, a log and <c>throw;</c>, or a <c>throw new …</c> hand the failure on. A constant
    /// <c>false</c> filter leaves the handler unreachable, so it never completes either. A filter that is no
    /// constant, <c>when (Expected(e))</c>, may take it and counts as taking it: an Error reports only a failure that
    /// is lost whatever the filter answers.
    /// </summary>
    private static bool Keeps(ICatchClauseOperation clause, IMethodSymbol assertion)
    {
        if (TypeKitSymbols.AssertionException(assertion.ContainingAssembly) is { } thrown && !IsOrDerivesFrom(thrown, clause.ExceptionType)) return false;

        return clause.Handler is not { Syntax: BlockSyntax handler, SemanticModel: { } model }
            || model.AnalyzeControlFlow(handler) is not { Succeeded: true, EndPointIsReachable: false, ExitPoints.IsEmpty: true };
    }

    private static bool IsOrDerivesFrom(ITypeSymbol? type, ITypeSymbol? ancestor)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, ancestor)) return true;
        }

        return false;
    }

    /// <summary>
    /// An assertion of CodoMetis.TypeKit.Testing on a <c>Task</c> or <c>ValueTask</c> of the value it
    /// asserts on, <c>await service.FindAsync(id).ShouldBeSome()</c>: it awaits the value and fails the
    /// test through the task it returns, so that task dropped unawaited passes whatever the value is.
    /// It is judged as every dropped call is, through <see cref="Producers"/>: passed through
    /// <c>ConfigureAwait</c> or <c>WaitAsync</c>, or reached through a branch of a conditional, a
    /// <c>??</c> or a <c>switch</c> expression. Every such assertion is new in 2.3, so reporting its drop
    /// breaks no build that worked before (design/adr/0118).
    /// </summary>
    private static bool IsPendingAssertion(IMethodSymbol method, Tasks tasks) =>
        TypeKitSymbols.IsTestingAssembly(method.ContainingAssembly)
     && (method.ReducedFrom ?? method).Parameters is { Length: > 0 } parameters
     && tasks.Awaited(parameters[0].Type) is not null;

    /// <summary>
    /// The calls whose values <paramref name="operation"/> drops as one: the call itself, or each
    /// branch's call of a conditional or a <c>switch</c> expression, through implicit conversions,
    /// null-conditional accesses and the task's <c>ConfigureAwait</c> and <c>WaitAsync</c>, which hand on
    /// the task they are called on. A branch that is no call, a variable or a literal, drops nothing of
    /// its own.
    /// </summary>
    private static IEnumerable<IInvocationOperation> Producers(IOperation operation, Tasks tasks)
    {
        switch (tasks.PassedOn(operation))
        {
            case IInvocationOperation invocation:
                yield return invocation;
                break;
            case IConditionalOperation { Type: not null, WhenTrue: { } whenTrue, WhenFalse: { } whenFalse }:
                foreach (var producer in Producers(whenTrue, tasks)) yield return producer;
                foreach (var producer in Producers(whenFalse, tasks)) yield return producer;
                break;
            case ICoalesceOperation { Value: { } value, WhenNull: { } whenNull }:
                foreach (var producer in Producers(value, tasks)) yield return producer;
                foreach (var producer in Producers(whenNull, tasks)) yield return producer;
                break;
            case ISwitchExpressionOperation chosen:
                foreach (var arm in chosen.Arms)
                {
                    foreach (var producer in Producers(arm.Value, tasks)) yield return producer;
                }

                break;
        }
    }

    /// <summary>
    /// What a value of <paramref name="type"/>, produced by <paramref name="invocation"/>, drops: a
    /// <c>Result</c> or <c>Option</c> or a task of one, or a collection or a tuple of them that the
    /// statement made, awaited or not.
    /// </summary>
    private static string? DroppedOutcome(ITypeSymbol? type, IInvocationOperation invocation, TypeKitSymbols symbols, Tasks tasks)
    {
        if ((symbols.OutcomeName(type) ?? tasks.PendingOutcome(type, symbols)) is { } outcome)
            return ReturnsItsStoredReceiver(invocation, symbols, tasks) ? null : outcome;

        // Task.WhenEach(first.CancelAsync(), second.CancelAsync()) yields the tasks it was given, results and all.
        if (tasks.PassesOnFailure(invocation.TargetMethod) == false && tasks.SequenceOutcome(type, symbols) is { } element)
            return DroppedHolder($"sequence of {element}s", invocation, symbols, tasks);

        return DroppedHolder(tasks.HolderOutcome(type, symbols) ?? tasks.HolderOutcome(tasks.Awaited(type), symbols), invocation, symbols, tasks);
    }

    private static string? DroppedCollection(string? element, IInvocationOperation invocation, TypeKitSymbols symbols, Tasks tasks) =>
        DroppedHolder(element is null ? null : $"collection of {element}s", invocation, symbols, tasks);

    /// <summary><paramref name="holder"/>, a collection or a tuple of results, when the statement made the results in it.</summary>
    private static string? DroppedHolder(string? holder, IInvocationOperation invocation, TypeKitSymbols symbols, Tasks tasks) =>
        holder is not null && MadeResults(invocation, symbols, tasks) ? holder : null;

    /// <summary>
    /// Whether the results in the collection <paramref name="invocation"/> returns were made in the
    /// statement, and so exist nowhere else: the call was given no result to pass on and made them
    /// itself, or a call in the statement returns a result or a task of one, or a method group of such
    /// a method is passed on (<c>ids.Select(orders.CancelAsync)</c>), or a call that was given no result
    /// returns a lazy sequence of them, which makes them as it is enumerated.
    /// </summary>
    /// <remarks>
    /// <c>Task.WhenAll(first, second)</c> over tasks in variables, and <c>pending.ToList()</c>, are given
    /// results that are still held and make none. <c>Tap</c> on a held receiver returns it.
    /// </remarks>
    private static bool MadeResults(IInvocationOperation invocation, TypeKitSymbols symbols, Tasks tasks)
    {
        if (!Given(invocation, symbols)) return true;

        // held.WaitAsync(t) hands on a task that is still held; a call under one is a descendant of its own.
        return invocation.Descendants().Any(operation => operation switch
        {
            IInvocationOperation call when tasks.PassesOn(call) => false,
            IInvocationOperation call           => (symbols.OutcomeName(call.Type) ?? tasks.PendingOutcome(call.Type, symbols)) is not null && !ReturnsItsStoredReceiver(call, symbols, tasks)
                                                || tasks.SequenceOutcome(call.Type, symbols) is not null && !Given(call, symbols),
            IMethodReferenceOperation reference => (symbols.OutcomeName(reference.Method.ReturnType) ?? tasks.PendingOutcome(reference.Method.ReturnType, symbols)) is not null,
            _                                   => false
        });
    }

    /// <summary>Whether the call was handed something that can carry a result, as its receiver or an argument.</summary>
    private static bool Given(IInvocationOperation invocation, TypeKitSymbols symbols) =>
        Tasks.Mentions(invocation.Instance?.Type, symbols)
     || invocation.TargetMethod.Parameters.Any(parameter => Tasks.Mentions(parameter.Type, symbols));

    /// <summary>
    /// A call's <c>Task</c> of a result, or of a collection of results it made, converted implicitly to
    /// a plain <c>Task</c>, which has no result to hand on, unless it is a tap on a result or task the
    /// caller still holds; or tasks of results the statement made,
    /// converted to plain tasks, an array, a sequence or a span of them, as <c>Task.WaitAll</c> takes.
    /// </summary>
    private static void AnalyzeConversion(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var conversion = (IConversionOperation)context.Operation;

        if (!conversion.IsImplicit) return;

        var toPlainTask = SymbolEqualityComparer.Default.Equals(conversion.Type, tasks.Plain);
        if (!toPlainTask && !tasks.HoldsPlainTasks(conversion.Type)) return;

        // Task.WaitAll(new[] { first.CancelAsync(), second.CancelAsync() }) converts the array of tasks whole:
        // each element's call is converted as if alone.
        if (!toPlainTask && Unwrap(conversion.Operand) is IArrayCreationOperation { Initializer.ElementValues: var elements })
        {
            foreach (var element in elements)
            foreach (var invocation in Producers(element, tasks))
            {
                if (ReturnsItsStoredReceiver(invocation, symbols, tasks) || tasks.PendingOutcome(invocation.Type, symbols) is not { } pending) continue;

                context.Report(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), pending, invocation.TargetMethod.Name));
            }

            return;
        }

        // The call, or each branch's call where a conditional or a switch expression was converted whole.
        // A pass-through between the conversion and the call, LoadAsync().WaitAsync(t), is reported whole.
        var passedOn = tasks.PassedOn(conversion.Operand);

        foreach (var invocation in Producers(conversion.Operand, tasks))
        {
            // held.TapAsync(log) returns the task of a result the caller still holds.
            // Task Check() => Task.WhenAny(a.ShouldBeNone(), b.ShouldBeNone()); is awaited as a task that never faults.
            var outcome = toPlainTask
                ? (ReturnsItsStoredReceiver(invocation, symbols, tasks) ? null : tasks.PendingOutcome(invocation.Type, symbols))
               ?? DroppedHolder(tasks.HolderOutcome(tasks.Awaited(invocation.Type), symbols), invocation, symbols, tasks)
               ?? DroppedAssertion(invocation, awaited: true, tasks)
                : DroppedCollection(tasks.PendingOutcome(tasks.ElementOfSequence(invocation.Type), symbols), invocation, symbols, tasks);

            if (outcome is null) continue;

            var reported = ReferenceEquals(invocation, passedOn) ? conversion.Operand : invocation;
            context.Report(Diagnostic.Create(Rule, reported.Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
        }
    }

    /// <summary>
    /// <c>Wait</c> on a task of a result, or of a collection of results, that the expression made:
    /// <c>orders.CancelAsync(id).Wait()</c> blocks until the result exists and hands it to nobody,
    /// whether the statement drops what <c>Wait</c> returns or an <c>if</c> reads it. A task still held,
    /// or a tap on one, keeps its result. Each branch of a conditional, a <c>??</c> or a <c>switch</c>
    /// expression is its own call, as in a dropped statement. <c>Wait</c> on a <c>Task.WhenAny</c> of
    /// assertions waits for the first and never sees a failure, and so does <c>Task.WaitAny</c>.
    /// </summary>
    private static void AnalyzeWait(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var wait = (IInvocationOperation)context.Operation;

        if (wait.TargetMethod is not { Name: "Wait" or "WaitAny" } method || !SymbolEqualityComparer.Default.Equals(method.ContainingType, tasks.Plain)) return;

        // Task.WaitAny(a.ShouldBeNone(), b.ShouldBeNone()) returns an index and leaves every failure
        // unobserved; Task.WaitAll throws them. Tasks of results are its conversions' (AnalyzeConversion).
        if (method.IsStatic)
        {
            if (method.Name != "WaitAny") return;

            foreach (var argument in wait.Arguments.Where(argument => argument.Parameter?.Ordinal == 0))
            foreach (var (assertion, name, _) in MadeAssertions(argument.Value, tasks))
                context.Report(Diagnostic.Create(Rule, assertion.Syntax.GetLocation(), "Task", name));

            return;
        }

        if (method.Name != "Wait" || wait.Instance is null) return;

        // The task's own call is reported at the Wait, each branch's call at the branch.
        var passedOn = tasks.PassedOn(wait.Instance);

        foreach (var invocation in Producers(wait.Instance, tasks))
        {
            if (ReturnsItsStoredReceiver(invocation, symbols, tasks)) continue;

            var outcome = tasks.PendingOutcome(invocation.Type, symbols)
                       ?? DroppedHolder(tasks.HolderOutcome(tasks.Awaited(invocation.Type), symbols), invocation, symbols, tasks)
                       ?? DroppedAssertion(invocation, awaited: true, tasks);
            if (outcome is null) continue;

            context.Report(Diagnostic.Create(Rule, (ReferenceEquals(invocation, passedOn) ? wait : invocation).Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
        }
    }

    /// <summary>
    /// A <c>foreach</c> or <c>await foreach</c> over results, or tasks of them, that its header made,
    /// whose body never reads the iteration variable: <c>await foreach (var outcome in
    /// orders.CancelEachAsync(ids)) { }</c> runs every cancellation and drops every result. So does one
    /// over assertions the header made, <c>Task.WhenEach</c> of them or a <c>Select</c>, whose failures nobody
    /// sees. A variable
    /// named <c>_</c> says the drop is intended; a collection held in a variable, a parameter or a field
    /// keeps its results.
    /// </summary>
    private static void AnalyzeLoop(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        if (context.Operation is not IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation { Symbol: { Name: not "_" } variable } } loop) return;
        if (Unwrap(loop.Collection) is not IInvocationOperation invocation) return;

        // foreach (var check in pending.Select(p => p.ShouldBeNone())) { } makes every assertion and observes none.
        var outcome = (symbols.OutcomeName(variable.Type) ?? tasks.PendingOutcome(variable.Type, symbols)) is { } element
            ? MadeResults(invocation, symbols, tasks) ? $"sequence of {element}s" : null
            : tasks.IsTask(variable.Type) && MadeAssertions(invocation, tasks).Any() ? $"sequence of {variable.Type.Name}s" : null;
        if (outcome is null) return;

        if (loop.Body.Descendants().OfType<ILocalReferenceOperation>().Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, variable))) return;

        context.Report(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
    }

    /// <summary>
    /// A method group that returns a <c>Task</c> of a result, converted to a delegate that returns a
    /// plain <c>Task</c>: every call through the delegate drops the result, as the lambda
    /// <c>id =&gt; orders.CancelAsync(id)</c> would. A cast to the delegate type says the drop is intended.
    /// </summary>
    private static void AnalyzeMethodGroup(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var creation = (IDelegateCreationOperation)context.Operation;

        if (creation.Target is not IMethodReferenceOperation reference || creation.Syntax is CastExpressionSyntax) return;
        if (creation.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } || !SymbolEqualityComparer.Default.Equals(invoke.ReturnType, tasks.Plain)) return;

        // A method that answers a collection or a tuple of results makes them on every call through the delegate.
        var returned = reference.Method.ReturnType;
        var outcome  = tasks.PendingOutcome(returned, symbols) ?? tasks.HolderOutcome(tasks.Awaited(returned), symbols);
        if (outcome is null) return;

        context.Report(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), outcome, reference.Method.Name));
    }

    /// <summary>
    /// <c>Email.TryFrom(input);</c> and <c>code.Revalidate();</c> on a value object of the same
    /// project, which do not bind where Metalama runs analyzers
    /// (<see cref="TypeKitSymbols.GeneratedMemberCall"/>): as a statement, or as the whole body of a
    /// method or lambda that returns nothing. A bound call is the operation path's.
    /// </summary>
    private static void AnalyzeUnbound(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var model = context.SemanticModel;
        var token = context.CancellationToken;

        var dropped = context.Node switch
        {
            ExpressionStatementSyntax statement => statement.Expression,
            ArrowExpressionClauseSyntax arrow when model.GetDeclaredSymbol(arrow.Parent!, token) is IMethodSymbol { ReturnsVoid: true } => arrow.Expression,
            LambdaExpressionSyntax { ExpressionBody: { } body } lambda when model.GetSymbolInfo(lambda, token).Symbol is IMethodSymbol { ReturnsVoid: true } => body,
            _ => null
        };

        if (dropped is not InvocationExpressionSyntax invocation || model.GetSymbolInfo(invocation, token).Symbol is not null) return;

        if (symbols.GeneratedMemberCall(invocation, TryFrom, model, token) is not null)
            context.Report(Diagnostic.Create(Rule, invocation.GetLocation(), "Option", TryFrom));
        else if (symbols.GeneratedInstanceMemberCall(invocation, Revalidate, model, token) is not null)
            context.Report(Diagnostic.Create(Rule, invocation.GetLocation(), "Result", Revalidate));
    }

    private static IOperation Unwrap(IOperation operation) =>
        operation switch
        {
            IConditionalAccessOperation { WhenNotNull: var whenNotNull } => Unwrap(whenNotNull),
            IConversionOperation { IsImplicit: true, Operand: var operand } => Unwrap(operand),
            _ => operation
        };

    /// <summary>
    /// A call that returns its receiver unchanged, on a receiver the caller still holds: the
    /// outcome's own <c>Tap</c>, <c>TapAsync</c>, <c>TapError</c> and <c>TapErrorAsync</c>, and the
    /// <c>Task</c> continuations <c>TapAsync</c> and <c>TapErrorAsync</c>, which return the task's result as it was;
    /// and <c>Task.WhenAny</c> of tasks the caller still holds, which completes with one of them.
    /// </summary>
    private static bool ReturnsItsStoredReceiver(IInvocationOperation invocation, TypeKitSymbols symbols, Tasks tasks)
    {
        var method = invocation.TargetMethod;

        if (method is { IsStatic: false, Name: "Tap" or "TapAsync" or "TapError" or "TapErrorAsync" } && symbols.OutcomeName(method.ContainingType) is not null)
            return IsStored(invocation.Instance, symbols, tasks);

        // pending.TapAsync(log) binds to the extension block's member, with the task as its instance;
        // Result.TapAsync(pending, log) to its implementation, with the task as the first argument.
        if (method.Name is "TapAsync" or "TapErrorAsync" && symbols.IsDeclaredByResultClass(method))
            return IsStored(invocation.Instance ?? invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value, symbols, tasks);

        // Task.WhenAny(first, second) completes with one of the tasks it was given, results and all.
        if (tasks.PassesOnFailure(method) == false)
            return invocation.Arguments.All(argument => Elements(argument.Value).All(element => IsStored(element, symbols, tasks)));

        return false;
    }

    /// <summary>The elements of an array or a collection written out, such as a <c>params</c> argument's, or the value itself.</summary>
    private static IEnumerable<IOperation> Elements(IOperation value) =>
        Unwrap(value) switch
        {
            IArrayCreationOperation { Initializer.ElementValues: var elements } => elements,
            ICollectionExpressionOperation { Elements: var elements }           => elements,
            var other                                                           => [other],
        };

    /// <summary>
    /// Whether the caller still holds <paramref name="receiver"/>: a local, a parameter or a field; a
    /// tap that returned such a receiver, so that <c>r.Tap(log).TapError(warn)</c> holds at every link;
    /// or what a task so held completes with, awaited in the chain, <c>(await pending).Tap(log)</c>.
    /// <c>ConfigureAwait</c> and <c>WaitAsync</c> hand on the task they are called on, so
    /// <c>pending.WaitAsync(t).TapAsync(log)</c> is a tap on <c>pending</c>.
    /// </summary>
    private static bool IsStored(IOperation? receiver, TypeKitSymbols symbols, Tasks tasks) =>
        receiver is not null && tasks.PassedOn(receiver) switch
        {
            ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation => true,
            IInvocationOperation tap                                                             => ReturnsItsStoredReceiver(tap, symbols, tasks),
            IAwaitOperation { Operation: var awaited }                                           => IsStored(awaited, symbols, tasks),
            _                                                                                    => false,
        };

    /// <summary>The task and collection types, resolved once per compilation.</summary>
    private sealed class Tasks(
        INamedTypeSymbol? plain,
        INamedTypeSymbol? generic,
        INamedTypeSymbol? genericValueTask,
        INamedTypeSymbol? collection,
        INamedTypeSymbol? readOnlyCollection,
        INamedTypeSymbol? sequence,
        INamedTypeSymbol? asyncSequence,
        INamedTypeSymbol? readOnlySpan,
        INamedTypeSymbol? keyValuePair)
    {
        public INamedTypeSymbol? Plain { get; } = plain;

        /// <summary>
        /// What <paramref name="operation"/> hands on, through implicit conversions, null-conditional
        /// accesses and any number of <see cref="PassesOn"/> calls: <c>held.ConfigureAwait(false)</c> is
        /// <c>held</c>, <c>LoadAsync().WaitAsync(t)</c> the <c>LoadAsync()</c> call.
        /// </summary>
        public IOperation PassedOn(IOperation operation)
        {
            var current = Unwrap(operation);
            while (current is IInvocationOperation { Instance: { } instance } call && PassesOn(call)) current = Unwrap(instance);

            return current;
        }

        /// <summary>
        /// <c>ConfigureAwait</c> and <c>WaitAsync</c> of <c>Task</c>, <c>Task&lt;T&gt;</c> or
        /// <c>ValueTask&lt;T&gt;</c>, every overload: each completes as the task does, with its own result,
        /// and makes none. A plain <c>Task</c> carries no result, but it can be the one an assertion of
        /// CodoMetis.TypeKit.Testing returns (<see cref="IsPendingAssertion"/>).
        /// </summary>
        public bool PassesOn(IInvocationOperation call) =>
            call.TargetMethod is { IsStatic: false, Name: "ConfigureAwait" or "WaitAsync" } method
         && (SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, generic)
          || SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, genericValueTask)
          || SymbolEqualityComparer.Default.Equals(method.ContainingType, Plain));

        /// <summary>
        /// For a method that hands on the tasks it is given: <see langword="true"/> for <c>Task.WhenAll</c> and
        /// <c>Task.Run</c>, whose task faults when one of them has; <see langword="false"/> for
        /// <c>Task.WhenAny</c>, whose task completes with the first and never faults, and <c>Task.WhenEach</c>,
        /// which yields them as they complete, faulted or not; <see langword="null"/> for any other method.
        /// </summary>
        public bool? PassesOnFailure(IMethodSymbol method) =>
            method is { IsStatic: true, Name: "WhenAll" or "WhenAny" or "WhenEach" or "Run" } && SymbolEqualityComparer.Default.Equals(method.ContainingType, Plain)
                ? method.Name is "WhenAll" or "Run"
                : null;

        /// <summary>Whether a value of <paramref name="type"/> is a task, or a collection or a sequence of them.</summary>
        public bool HoldsTasks(ITypeSymbol? type) =>
            IsTask(type) || IsTask(ElementOfSequence(type));

        public bool IsTask(ITypeSymbol? type) => SymbolEqualityComparer.Default.Equals(type, Plain) || Awaited(type) is not null;

        /// <summary>
        /// A <c>Task</c> or <c>ValueTask</c> of a result, not awaited: the result is dropped with it. So is a task
        /// of one, such as the <c>Task&lt;Task&lt;Result&lt;…&gt;&gt;&gt;</c> of <c>Task.WhenAny</c>, which completes
        /// with a task of a result.
        /// </summary>
        public string? PendingOutcome(ITypeSymbol? type, TypeKitSymbols symbols) => symbols.OutcomeName(Awaited(type)) ?? symbols.OutcomeName(Awaited(Awaited(type)));

        /// <summary>What a <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c> completes with, or <see langword="null"/> for any other type.</summary>
        public ITypeSymbol? Awaited(ITypeSymbol? type) =>
            type is INamedTypeSymbol { TypeArguments.Length: 1 } named
         && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic) || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, genericValueTask))
                ? named.TypeArguments[0]
                : null;

        /// <summary>
        /// What a value of <paramref name="type"/> holds results in, as the message names it: a
        /// <see cref="CollectionOutcome">collection</see> (<c>collection of Results</c>) or a
        /// <see cref="TupleOutcome">tuple</see> (<c>tuple holding a Result</c>) of them.
        /// </summary>
        public string? HolderOutcome(ITypeSymbol? type, TypeKitSymbols symbols) =>
            CollectionOutcome(type, symbols) is { } element ? $"collection of {element}s"
          : TupleOutcome(type, symbols) is { } held       ? $"tuple holding {(held is "Option" ? "an" : "a")} {held}"
          : null;

        /// <summary>
        /// <c>Result</c> or <c>Option</c> when <paramref name="type"/> is a collection whose elements
        /// exist, of results, of tasks of them or of key-value pairs whose value is one: an array, or a
        /// type that is or implements <c>ICollection&lt;T&gt;</c> or <c>IReadOnlyCollection&lt;T&gt;</c>, a
        /// dictionary included. A lazy sequence is none.
        /// </summary>
        private string? CollectionOutcome(ITypeSymbol? type, TypeKitSymbols symbols)
        {
            var element = type switch
            {
                IArrayTypeSymbol array => array.ElementType,
                INamedTypeSymbol named => ElementOf(named),
                _                      => null
            };

            return ElementOutcome(element, symbols);
        }

        /// <summary>
        /// <c>Result</c> or <c>Option</c> when <paramref name="type"/> is a tuple, nullable or not, with
        /// an element that is one, a task of one, or a tuple holding one.
        /// </summary>
        private string? TupleOutcome(ITypeSymbol? type, TypeKitSymbols symbols)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) type = nullable.TypeArguments[0];
            if (type is not INamedTypeSymbol { IsTupleType: true } tuple) return null;

            return tuple.TupleElements
                        .Select(element => symbols.OutcomeName(element.Type) ?? PendingOutcome(element.Type, symbols) ?? TupleOutcome(element.Type, symbols))
                        .FirstOrDefault(outcome => outcome is not null);
        }

        /// <summary>
        /// <c>Result</c> or <c>Option</c> when <paramref name="type"/> is a sequence, lazy or not, of
        /// results, of tasks of them or of key-value pairs whose value is one: an array, or a type that
        /// is or implements <c>IEnumerable&lt;T&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c>.
        /// </summary>
        public string? SequenceOutcome(ITypeSymbol? type, TypeKitSymbols symbols) => ElementOutcome(ElementOfSequence(type), symbols);

        /// <summary>A collection's element that is a result, a task of one, or a <c>KeyValuePair</c> whose value is either.</summary>
        private string? ElementOutcome(ITypeSymbol? element, TypeKitSymbols symbols)
        {
            if (element is INamedTypeSymbol { TypeArguments.Length: 2 } pair && SymbolEqualityComparer.Default.Equals(pair.OriginalDefinition, keyValuePair))
                element = pair.TypeArguments[1];

            return symbols.OutcomeName(element) ?? PendingOutcome(element, symbols);
        }

        /// <summary>The element of an array, or of a type that is or implements <c>IEnumerable&lt;T&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c>.</summary>
        public ITypeSymbol? ElementOfSequence(ITypeSymbol? type) =>
            type switch
            {
                IArrayTypeSymbol array => array.ElementType,
                INamedTypeSymbol named => ElementOf(named, sequence, asyncSequence),
                _                      => null
            };

        /// <summary>
        /// Whether <paramref name="type"/> holds plain tasks, which have no result to hand on: an array
        /// of <c>Task</c>, a sequence of them, or a <c>ReadOnlySpan&lt;Task&gt;</c>.
        /// </summary>
        public bool HoldsPlainTasks(ITypeSymbol? type) =>
            type switch
            {
                IArrayTypeSymbol array => SymbolEqualityComparer.Default.Equals(array.ElementType, Plain),
                INamedTypeSymbol { TypeArguments.Length: 1 } named => SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], Plain)
                                                                   && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlySpan) || ElementOfSequence(named) is not null),
                _ => false
            };

        private ITypeSymbol? ElementOf(INamedTypeSymbol type) => ElementOf(type, collection, readOnlyCollection);

        private static ITypeSymbol? ElementOf(INamedTypeSymbol type, INamedTypeSymbol? first, INamedTypeSymbol? second)
        {
            foreach (var candidate in type.AllInterfaces.Insert(0, type))
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, first)
                 || SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, second))
                    return candidate.TypeArguments[0];
            }

            return null;
        }

        /// <summary>
        /// Whether a value of <paramref name="type"/> can carry a result: it is one, or an array's
        /// element, a type argument, or an interface's type argument carries one
        /// (<c>ReadOnlySpan&lt;Task&lt;Result&lt;…&gt;&gt;&gt;</c>, <c>Func&lt;Guid, Option&lt;…&gt;&gt;</c>, a
        /// class that implements <c>IEnumerable&lt;Result&lt;…&gt;&gt;</c>).
        /// </summary>
        public static bool Mentions(ITypeSymbol? type, TypeKitSymbols symbols) =>
            Carries(type, symbols) || type is INamedTypeSymbol named && named.AllInterfaces.Any(candidate => Carries(candidate, symbols));

        private static bool Carries(ITypeSymbol? type, TypeKitSymbols symbols) =>
            symbols.OutcomeName(type) is not null
         || type is IArrayTypeSymbol array && Carries(array.ElementType, symbols)
         || type is INamedTypeSymbol named && named.TypeArguments.Any(argument => Carries(argument, symbols));
    }
}
