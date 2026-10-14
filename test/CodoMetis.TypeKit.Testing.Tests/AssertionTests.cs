namespace CodoMetis.TypeKit.Testing.Tests;

/// <summary>
/// Each assertion hands back the content it expects, and otherwise fails saying what the outcome was:
/// the error by name, never a value.
/// </summary>
public sealed class AssertionTests
{
    public enum Fault { NotFound, AlreadyShipped }

    /// <summary>A value whose text would show if a message printed it.</summary>
    private const string Secret = "s3cr3t-value";

    private static Result<string, Fault> Success => Secret;

    private static Result<string, Fault> Error => Result.Error(Fault.NotFound);

    private static Result<string, Fault> Uninitialized => (new Result<string, Fault>[1])[0];

    private static Result<Fault> CommandUninitialized => (new Result<Fault>[1])[0];

    [Fact]
    public void Some_hands_back_its_value() => Option.Some(Secret).ShouldBeSome().ShouldBe(Secret);

    [Fact]
    public void None_is_not_Some() =>
        Should.Throw<TypeKitAssertionException>(() => Option.None<string>().ShouldBeSome())
              .Message.ShouldBe("Expected Some, but the Option<String> is None.");

    [Fact]
    public void None_is_None() => Option.None<string>().ShouldBeNone();

    [Fact]
    public void Some_is_not_None_and_its_value_is_not_printed() =>
        Should.Throw<TypeKitAssertionException>(() => Option.Some(Secret).ShouldBeNone())
              .Message.ShouldBe("Expected None, but the Option<String> holds a value (not printed).");

    [Fact]
    public void A_success_hands_back_its_value() => Success.ShouldBeSuccess().ShouldBe(Secret);

    [Fact]
    public void An_error_is_no_success_and_is_named() =>
        Should.Throw<TypeKitAssertionException>(() => Error.ShouldBeSuccess())
              .Message.ShouldBe("Expected a success, but the Result<String, AssertionTests.Fault> is the error NotFound.");

    [Fact]
    public void An_error_hands_back_its_error() => Error.ShouldBeError().ShouldBe(Fault.NotFound);

    [Fact]
    public void A_success_is_no_error_and_its_value_is_not_printed() =>
        Should.Throw<TypeKitAssertionException>(() => Success.ShouldBeError())
              .Message.ShouldBe("Expected an error, but the Result<String, AssertionTests.Fault> is a success (its value is not printed).");

    [Fact]
    public void The_expected_error_passes() => Error.ShouldBeError(Fault.NotFound);

    [Fact]
    public void Another_error_is_named_beside_the_expected_one() =>
        Should.Throw<TypeKitAssertionException>(() => Error.ShouldBeError(Fault.AlreadyShipped))
              .Message.ShouldBe("Expected the error AlreadyShipped, but the Result<String, AssertionTests.Fault> is the error NotFound.");

    [Fact]
    public void A_success_is_not_the_expected_error() =>
        Should.Throw<TypeKitAssertionException>(() => Success.ShouldBeError(Fault.NotFound))
              .Message.ShouldNotContain(Secret);

    [Fact]
    public void A_command_success_passes() => Result<Fault>.Success().ShouldBeSuccess();

    [Fact]
    public void A_command_error_is_no_success_and_is_named() =>
        Should.Throw<TypeKitAssertionException>(() => Result<Fault>.Error(Fault.AlreadyShipped).ShouldBeSuccess())
              .Message.ShouldBe("Expected a success, but the Result<AssertionTests.Fault> is the error AlreadyShipped.");

    [Fact]
    public void A_command_error_hands_back_its_error() =>
        Result<Fault>.Error(Fault.AlreadyShipped).ShouldBeError().ShouldBe(Fault.AlreadyShipped);

    [Fact]
    public void A_command_success_is_no_error() =>
        Should.Throw<TypeKitAssertionException>(() => Result<Fault>.Success().ShouldBeError(Fault.NotFound))
              .Message.ShouldBe("Expected an error, but the Result<AssertionTests.Fault> is a success.");

    [Fact]
    public void A_command_error_other_than_the_expected_is_named() =>
        Should.Throw<TypeKitAssertionException>(() => Result<Fault>.Error(Fault.AlreadyShipped).ShouldBeError(Fault.NotFound))
              .Message.ShouldBe("Expected the error NotFound, but the Result<AssertionTests.Fault> is the error AlreadyShipped.");

    public static TheoryData<string> UninitializedAssertions => [.. Uninitialize.Keys];

    private static readonly Dictionary<string, Action> Uninitialize = new()
    {
        ["ShouldBeSuccess()"] = () => Uninitialized.ShouldBeSuccess(),
        ["ShouldBeError()"] = () => Uninitialized.ShouldBeError(),
        ["ShouldBeError(expected)"] = () => Uninitialized.ShouldBeError(Fault.NotFound),
        ["command ShouldBeSuccess()"] = () => CommandUninitialized.ShouldBeSuccess(),
        ["command ShouldBeError()"] = () => CommandUninitialized.ShouldBeError(),
        ["command ShouldBeError(expected)"] = () => CommandUninitialized.ShouldBeError(Fault.NotFound),
    };

    /// <summary>
    /// <c>default(Fault)</c> is <c>NotFound</c>: an assertion that read an uninitialized result as its error
    /// would pass <c>ShouldBeError(Fault.NotFound)</c> on it.
    /// </summary>
    [Theory]
    [MemberData(nameof(UninitializedAssertions))]
    public void An_uninitialized_result_fails_every_assertion_as_uninitialized(string assertion) =>
        Should.Throw<TypeKitAssertionException>(Uninitialize[assertion])
              .Message.ShouldContain("is uninitialized: a default instance, neither a success nor an error.");

    [Fact]
    public void The_assertions_are_enumerated() => Uninitialize.Count.ShouldBe(6);

    /// <summary>
    /// A type is named as C# writes it from outside: after its nesting chain, with each containing
    /// type's own type arguments. A type nested in a generic one has no arity of its own, and cutting
    /// its name at a backtick threw <see cref="ArgumentOutOfRangeException"/> instead of failing.
    /// </summary>
    [Fact]
    public void A_type_nested_in_a_generic_one_is_named_as_CSharp_writes_it() =>
        Should.Throw<TypeKitAssertionException>(() => Option.None<Outer<int>.Inner>().ShouldBeSome())
              .Message.ShouldBe("Expected Some, but the Option<AssertionTests.Outer<Int32>.Inner> is None.");

    [Fact]
    public void A_generic_type_nested_in_a_generic_one_takes_its_own_arguments() =>
        Should.Throw<TypeKitAssertionException>(() => Option.None<Outer<string>.Pair<int, Fault>[]>().ShouldBeSome())
              .Message.ShouldBe("Expected Some, but the Option<AssertionTests.Outer<String>.Pair<Int32, AssertionTests.Fault>[]> is None.");

    public static class Outer<T>
    {
        public sealed class Inner;

        public sealed class Pair<TFirst, TSecond>;
    }
}
