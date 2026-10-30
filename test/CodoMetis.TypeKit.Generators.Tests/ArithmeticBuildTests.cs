namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// An operator interface the generators do not implement stays to be implemented, and the build fails with
/// C#'s own error naming the operator, as it did before they implemented any: on a validated value object, over
/// a wrapped type without that operator (a <see cref="string"/>, a <see cref="DateTime"/>, which implements none
/// of the operator interfaces), for the second of two interfaces that ask for one operator, and beside a
/// hand-written <c>checked</c> operator that lacks the plain one C# requires.
/// </summary>
/// <remarks>
/// In a consumer of its own: beside a compiler error, Metalama reported no error in the code it generated into a
/// declaration's own file (<see cref="EqualityOperatorBuildTests"/>), so these declarations would blind the shared
/// consumer. That the generated operators compile is shown by the test assembly, which declares them.
/// </remarks>
public sealed class ArithmeticBuildTests(ArithmeticBuildTests.Consumer consumer) : IClassFixture<ArithmeticBuildTests.Consumer>
{
    [Theory]
    [InlineData("ValidatedSum", "IAdditionOperators<ValidatedSum, ValidatedSum, ValidatedSum>.operator +(ValidatedSum, ValidatedSum)")]
    [InlineData("TextSum", "IAdditionOperators<TextSum, TextSum, TextSum>.operator +(TextSum, TextSum)")]
    [InlineData("Moment", "IAdditionOperators<Moment, TimeSpan, Moment>.operator +(Moment, TimeSpan)")]
    public void An_operator_the_generators_cannot_write_is_left_to_implement(string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == "CS0535" && error.Message.StartsWith($"'{type}' does not implement interface member '{member}'"), consumer.Output);

    /// <summary>C# allows one <c>operator /(TwoQuotients, TwoQuotients)</c>: it is generated once, for the first interface.</summary>
    [Fact]
    public void The_second_interface_asking_for_one_operator_is_left_to_implement() =>
        consumer.Errors.ShouldContain(error => error.Id == "CS0738" && error.Message.StartsWith("'TwoQuotients' does not implement interface member 'IDivisionOperators<TwoQuotients, TwoQuotients, decimal>.operator /(TwoQuotients, TwoQuotients)'"), consumer.Output);

    /// <summary>
    /// A hand-written <c>checked +</c> makes addition the user's: the <c>+</c> C# requires beside it is theirs to
    /// write. Generated, it would build, and add by the wrapped type's rules everywhere but a checked context.
    /// </summary>
    [Fact]
    public void A_hand_written_checked_operator_is_not_completed() =>
        consumer.Errors.ShouldContain(error => error.Id == "CS9025" && error.Message.Contains("OnlyChecked"), consumer.Output);

    /// <summary>Nothing but C#'s own errors: no aspect failed on these declarations (LAMA0041, LAMA0611).</summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones() =>
        consumer.Errors.Select(error => error.Id).Distinct().Order().ShouldBe(["CS0535", "CS0738", "CS9025"], ignoreOrder: false, customMessage: consumer.Output);

    /// <summary>The consumer, built once per test run.</summary>
    public sealed class Consumer : IAsyncLifetime
    {
        private const string Declarations =
            """
            using System.Numerics;
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            public enum Fault { Refused }

            public readonly partial record struct ValidatedSum : IValidatedValue<ValidatedSum, int, Fault>, IAdditionOperators<ValidatedSum, ValidatedSum, ValidatedSum>
            {
                public static Result<ValidatedSum, Fault> Create(int value) => new ValidatedSum(value);
            }

            public readonly partial record struct TextSum : IValue<string>, IAdditionOperators<TextSum, TextSum, TextSum>;

            public readonly partial record struct Moment : IValue<DateTime>, IAdditionOperators<Moment, TimeSpan, Moment>;

            public readonly partial record struct TwoQuotients : IValue<decimal>,
                IDivisionOperators<TwoQuotients, TwoQuotients, TwoQuotients>,
                IDivisionOperators<TwoQuotients, TwoQuotients, decimal>;

            public readonly partial record struct OnlyChecked : IValue<int>, IAdditionOperators<OnlyChecked, OnlyChecked, OnlyChecked>
            {
                public static OnlyChecked operator checked +(OnlyChecked left, OnlyChecked right) => From(checked(left.Value + right.Value));
            }
            """;

        private readonly string _directory = Path.Combine(BuildOutcomeTests.Consumer.RepositoryRoot(), "artifacts", $"consumer-{Guid.NewGuid():N}");

        public IReadOnlyList<BuildOutcomeTests.Diagnostic> Errors { get; private set; } = [];

        public string Output { get; private set; } = "";

        public async ValueTask InitializeAsync() =>
            (Output, Errors) = await BuildOutcomeTests.Consumer.Build(_directory, new Dictionary<string, string> { ["Declarations.cs"] = Declarations });

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
