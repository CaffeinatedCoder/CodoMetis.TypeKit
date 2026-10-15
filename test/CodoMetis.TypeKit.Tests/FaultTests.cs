namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="Fault{TFault}"/>: a reason and a sentence about one occurrence. A bare reason converted
/// to a fault without a detail, so comparing a fault with a reason, directly or through a collection, an
/// equality comparer or an assertion, compiled and was false whenever the fault carried a detail. No
/// reason converts now, and the mixed comparisons are compile errors that name <c>Reason</c>.
/// </summary>
public sealed class FaultTests
{
    private enum OrderFault { NotFound, NameTaken }

    private static string Consumer(string member) =>
        $$"""
          using System;
          using System.Collections.Generic;
          using System.Linq;
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
    [InlineData("Fault<OrderFault> Make() => OrderFault.NotFound;", "CS0029")]
    [InlineData("bool Same(Fault<OrderFault>? fault) => fault == OrderFault.NotFound;", "CS0019")]
    [InlineData("bool Holds(List<Fault<OrderFault>> faults) => faults.Contains(OrderFault.NotFound);", "CS1503")]
    [InlineData("bool Holds(HashSet<Fault<OrderFault>> faults) => faults.Contains(OrderFault.NotFound);", "CS1503")]
    [InlineData("bool Holds(Fault<OrderFault>[] faults) => faults.Contains(OrderFault.NotFound);", "CS1929")]
    [InlineData("bool Same(Fault<OrderFault> fault) => EqualityComparer<Fault<OrderFault>>.Default.Equals(fault, OrderFault.NotFound);", "CS0176")]
    public void A_bare_reason_is_not_a_fault(string member, string error)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));

        compilation.ErrorIds().ShouldBe([error], compilation.Problems());
    }

    [Theory]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault.Reason == OrderFault.NotFound;")]
    [InlineData("Fault<OrderFault> Make() => Fault.Of(OrderFault.NotFound);")]
    [InlineData("bool Holds(List<Fault<OrderFault>> faults) => faults.Exists(fault => fault.Reason == OrderFault.NotFound);")]
    [InlineData("bool Same(Fault<OrderFault> fault, Fault<OrderFault> other) => fault == other;")]
    [InlineData("bool Same(Fault<OrderFault> fault) => fault == Fault.Of(OrderFault.NotFound, \"Order 7 is gone.\");")]
    public void Comparing_the_reason_or_two_faults_compiles(string member) =>
        ConsumerCompilation.Of(Consumer(member)).Problems().ShouldBeEmpty();

    /// <summary>
    /// No detail is <c>null</c>, so a blank one is a mistake, and refused where it is made: a detail of
    /// spaces is written into a problem's <c>detail</c> as a sentence that says nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void A_blank_detail_is_refused(string detail)
    {
        var refusal = Should.Throw<ArgumentException>(() => Fault.Of(OrderFault.NotFound, detail));

        refusal.ParamName.ShouldBe("detail");
        refusal.ShouldNotBeOfType<ArgumentNullException>();
    }

    /// <summary>A reason that is a class, a closed set's member, can be null, and a fault of no reason is refused.</summary>
    [Fact]
    public void A_null_reason_is_refused()
    {
        var refusal = Should.Throw<ArgumentNullException>(() => Fault.Of<string>(null!));

        refusal.ParamName.ShouldBe("reason");
        refusal.Message.ShouldStartWith("A fault's reason");
        refusal.Message.ShouldNotContain("Option");
    }

    [Fact]
    public void A_fault_without_a_detail_has_none()
    {
        Fault.Of(OrderFault.NotFound).Detail.ShouldBeNull();
        Fault.Of(OrderFault.NotFound, null).Detail.ShouldBeNull();
    }

    [Fact]
    public void Two_faults_are_equal_by_reason_and_detail()
    {
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").ShouldBe(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").ShouldNotBe(Fault.Of(OrderFault.NotFound));
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").Reason.ShouldBe(OrderFault.NotFound);
    }

    [Flags]
    private enum Access { Read = 1, Write = 2 }

    private static class Billing
    {
        public enum Rule { Overdrawn }
    }

    /// <summary>A closed set written by hand, the contract alone, since this assembly runs no generator.</summary>
    private sealed class EmailRule : CodoMetis.TypeKit.ClosedSets.IClosedSet<EmailRule>
    {
        public static readonly EmailRule NoAt = new("NoAt");

        private EmailRule(string name) => Name = name;

        public string Name { get; }
        public string QualifiedName => $"{nameof(EmailRule)}.{Name}";
        public static IReadOnlyList<EmailRule> All => [NoAt];

        public static bool TryParse(string? name, out EmailRule member)
        {
            member = NoAt;
            return name == NoAt.Name;
        }
    }

    [Fact]
    public void A_fault_names_no_rule_unless_one_is_given() =>
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").Rule.ShouldBeNull();

    /// <summary>
    /// The rule is a key a client branches on, so it is qualified with its type, a nested one after its
    /// nesting chain: three types' <c>Blank</c> are three rules.
    /// </summary>
    [Fact]
    public void A_rule_is_named_by_its_type_and_member()
    {
        var fault = Fault.Of(OrderFault.NameTaken, "Order 7 is called that.").WithRule(Billing.Rule.Overdrawn);

        fault.Rule.ShouldBe("FaultTests.Billing.Rule.Overdrawn");
        fault.Reason.ShouldBe(OrderFault.NameTaken);
        fault.Detail.ShouldBe("Order 7 is called that.");
        Fault.Of(OrderFault.NameTaken).WithRule(EmailRule.NoAt).Rule.ShouldBe("EmailRule.NoAt");
        Fault.Of(OrderFault.NameTaken).WithRule(Access.Write).Rule.ShouldBe("FaultTests.Access.Write");
    }

    /// <summary>A fault prints as 2.0 printed it, which assertion messages quote, and its rule only where it names one.</summary>
    [Fact]
    public void A_fault_prints_its_rule_only_where_it_names_one()
    {
        Fault.Of(OrderFault.NotFound, "Order 7 is gone.").ToString().ShouldBe("Fault { Reason = NotFound, Detail = Order 7 is gone. }");
        Fault.Of(OrderFault.NotFound).WithRule(Access.Read).ToString().ShouldBe("Fault { Reason = NotFound, Detail = , Rule = FaultTests.Access.Read }");
    }

    [Fact]
    public void A_second_rule_replaces_the_first() =>
        Fault.Of(OrderFault.NameTaken).WithRule(Access.Read).WithRule(EmailRule.NoAt).Rule.ShouldBe("EmailRule.NoAt");

    /// <summary>A number no member names is no key: a combination of flags and a cast are refused where the rule is given.</summary>
    [Fact]
    public void A_value_no_member_names_is_no_rule()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Fault.Of(OrderFault.NotFound).WithRule(Access.Read | Access.Write)).ParamName.ShouldBe("rule");
        var refusal = Should.Throw<ArgumentOutOfRangeException>(() => Fault.Of(OrderFault.NotFound).WithRule((Billing.Rule)7));
        refusal.Message.ShouldStartWith("A rule is one of FaultTests.Billing.Rule's members");
        Should.Throw<ArgumentNullException>(() => Fault.Of(OrderFault.NotFound).WithRule((EmailRule)null!)).ParamName.ShouldBe("rule");
    }

    [Fact]
    public void Two_faults_are_equal_by_rule_too()
    {
        Fault.Of(OrderFault.NotFound).WithRule(Access.Read).ShouldBe(Fault.Of(OrderFault.NotFound).WithRule(Access.Read));
        Fault.Of(OrderFault.NotFound).WithRule(Access.Read).ShouldNotBe(Fault.Of(OrderFault.NotFound).WithRule(Access.Write));
        Fault.Of(OrderFault.NotFound).WithRule(Access.Read).ShouldNotBe(Fault.Of(OrderFault.NotFound));
    }

    private static class Box<T>
    {
        public enum Rule { Blank }
    }

    /// <summary>
    /// A rule's name has no namespace and no type arguments, so two types' rules can read alike; the
    /// faults stay apart by the rule's type, as the rules themselves are apart.
    /// </summary>
    [Fact]
    public void Two_rules_that_read_alike_from_different_types_are_unequal_faults()
    {
        AssertApart(Fault.Of(OrderFault.NotFound).WithRule(Shipping.Rule.Blank), Fault.Of(OrderFault.NotFound).WithRule(Invoicing.Rule.Blank));
        AssertApart(Fault.Of(OrderFault.NotFound).WithRule(Shipping.RuleSet.Blank), Fault.Of(OrderFault.NotFound).WithRule(Invoicing.RuleSet.Blank));
        AssertApart(Fault.Of(OrderFault.NotFound).WithRule(Box<int>.Rule.Blank), Fault.Of(OrderFault.NotFound).WithRule(Box<string>.Rule.Blank));

        static void AssertApart(Fault<OrderFault> one, Fault<OrderFault> other)
        {
            one.Rule.ShouldBe(other.Rule);
            one.ShouldNotBe(other);
            (one == other).ShouldBeFalse();
            one.ShouldBe(one with { });
        }
    }

    [Fact]
    public void A_second_rule_replaces_the_first_rules_type_too()
    {
        var replaced = Fault.Of(OrderFault.NotFound).WithRule(Shipping.Rule.Blank).WithRule(Invoicing.Rule.Blank);

        replaced.ShouldBe(Fault.Of(OrderFault.NotFound).WithRule(Invoicing.Rule.Blank));
        replaced.GetHashCode().ShouldBe(Fault.Of(OrderFault.NotFound).WithRule(Invoicing.Rule.Blank).GetHashCode());
        replaced.ShouldNotBe(Fault.Of(OrderFault.NotFound).WithRule(Shipping.Rule.Blank));
    }

    /// <summary>A rule is a member of a fixed set: a string or any other type is no rule, and does not compile, through <c>with</c> either.</summary>
    [Theory]
    [InlineData("Fault<OrderFault> Make() => Fault.Of(OrderFault.NotFound).WithRule(\"blank\");")]
    [InlineData("Fault<OrderFault> Make() => Fault.Of(OrderFault.NotFound).WithRule(42);")]
    [InlineData("Fault<OrderFault> Make(Fault<OrderFault> fault) => fault with { Rule = \"blank\" };")]
    public void A_rule_that_is_no_member_of_a_fixed_set_does_not_compile(string member) =>
        ConsumerCompilation.Of(Consumer(member)).ErrorIds().ShouldNotBeEmpty();
}
