namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// A hand-written <c>operator ==</c> or <c>!=</c> between two of a value object's type collides with the
/// record's own, which C# reports as CS0111 naming only <c>op_Equality</c>. CMTK1011 beside it names the
/// operator and says why a value object's equality is its wrapped value's.
/// </summary>
/// <remarks>
/// In a consumer of its own: beside a CS0111, Metalama reported no error in the code it generated into
/// a declaration's own file (a LAMA0611 that <see cref="BuildOutcomeTests"/> exists to catch went
/// unreported, measured 2026-10-13), so these declarations would blind the shared consumer.
/// </remarks>
public sealed class EqualityOperatorBuildTests(EqualityOperatorBuildTests.Consumer consumer) : IClassFixture<EqualityOperatorBuildTests.Consumer>
{
    [Theory]
    [InlineData("WithEqualityOperator", "WithEqualityOperator.operator ==(WithEqualityOperator, WithEqualityOperator), WithEqualityOperator.operator !=(WithEqualityOperator, WithEqualityOperator), so")]
    [InlineData("WithEqualityOperatorClass", "WithEqualityOperatorClass.operator ==(WithEqualityOperatorClass?, WithEqualityOperatorClass?), WithEqualityOperatorClass.operator !=(WithEqualityOperatorClass?, WithEqualityOperatorClass?), so")]
    public void A_hand_written_equality_operator_is_refused_by_name(string type, string members) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares {members}")
                                            && error.Message.Contains("a value object's equality is its wrapped value's"), consumer.Output);

    /// <summary>An operator between the value object and another type is the user's own, and C#'s, and is kept.</summary>
    [Fact]
    public void An_operator_with_another_type_is_not_refused() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'WithMixedOperator'"), consumer.Output);

    /// <summary>Nothing but the refusals and C#'s own CS0111, which only the two operator fixtures cause.</summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones()
    {
        consumer.Errors.Select(error => error.Id).Distinct().Order().ShouldBe(["CMTK1011", "CS0111"], ignoreOrder: false, customMessage: consumer.Output);
        consumer.Errors.Where(error => error.Id == "CS0111")
                .ShouldAllBe(error => error.Message.StartsWith("Type 'WithEqualityOperator") && (error.Message.Contains("'op_Equality'") || error.Message.Contains("'op_Inequality'")), consumer.Output);
    }

    /// <summary>The consumer, built once per test run.</summary>
    public sealed class Consumer : IAsyncLifetime
    {
        private const string Declarations =
            """
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            public readonly partial record struct WithEqualityOperator : IValue<int>
            {
                public static bool operator ==(WithEqualityOperator left, WithEqualityOperator right) => true;

                public static bool operator !=(WithEqualityOperator left, WithEqualityOperator right) => false;
            }

            public sealed partial record WithEqualityOperatorClass : IValue<string>
            {
                public static bool operator ==(WithEqualityOperatorClass? left, WithEqualityOperatorClass? right) => true;

                public static bool operator !=(WithEqualityOperatorClass? left, WithEqualityOperatorClass? right) => false;
            }

            public readonly partial record struct WithMixedOperator : IValue<int>
            {
                public static bool operator ==(WithMixedOperator left, int right) => left.Value == right;

                public static bool operator !=(WithMixedOperator left, int right) => left.Value != right;
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
