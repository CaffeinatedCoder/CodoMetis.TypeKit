namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="Fault{TFault}"/>: a reason and a sentence about one occurrence. A bare reason converts
/// to a fault without a detail, so comparing a fault with a reason compiled and was false whenever the
/// fault carried a detail; the mixed comparisons are compile errors that name <c>Reason</c>.
/// </summary>
public sealed class FaultTests
{
    private enum OrderFault { NotFound, NameTaken }

    private static string Consumer(string member) =>
        $$"""
          using CodoMetis.TypeKit;

          public enum OrderFault { NotFound, NameTaken }

          public static class Checks
          {
              public static {{member}}
          }
          """;

    [Theory]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault == OrderFault.NotFound;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault != OrderFault.NotFound;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => OrderFault.NotFound == fault;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => OrderFault.NotFound != fault;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault.Equals(OrderFault.NotFound);")]
    public void Comparing_a_fault_with_a_bare_reason_is_a_compile_error_naming_Reason(string member)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain("fault.Reason");
    }

    [Theory]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault.Reason == OrderFault.NotFound;")]
    [InlineData("bool Same(Fault<OrderFault> fault, Fault<OrderFault> other) => fault == other;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault == Fault.Of(OrderFault.NotFound, \"Order 7 is gone.\");")]
    public void Comparing_the_reason_or_two_faults_compiles(string member) =>
        ConsumerCompilation.Of(Consumer(member)).Problems().ShouldBeEmpty();

    [Fact]
    public void Two_faults_are_equal_by_reason_and_detail()
    {
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").ShouldBe(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").ShouldNotBe(Fault.Of(OrderFault.NotFound));
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").Reason.ShouldBe(OrderFault.NotFound);
    }
}
