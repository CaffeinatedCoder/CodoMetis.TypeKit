namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="Fault{T1, T2}"/> and <see cref="Fault{T1, T2, T3}"/>: one fault of one of several types, its
/// reason, detail and rule intact, so a boundary answers with its own faults and with those shared code
/// decides, without copying them into its own type (decision 78).
/// </summary>
public sealed class ComposedFaultTests
{
    private enum OrderFault { NotFound, NameTaken }

    private enum ConcurrencyFault { Conflict, Stale }

    private enum RemovalFault { StillReferenced }

    private enum Rule { Blank }

    private static string Consumer(string member) =>
        $$"""
          using System;
          using System.Collections.Generic;
          using System.Linq;
          using CodoMetis.TypeKit;

          public enum OrderFault { NotFound, NameTaken }

          public enum ConcurrencyFault { Conflict, Stale }

          public static class Checks
          {
              public static {{member}}
          }
          """;

    [Fact]
    public void A_fault_of_either_type_converts_and_is_matched_to_its_own_delegate()
    {
        Fault<OrderFault, ConcurrencyFault> own = Fault.Of(OrderFault.NameTaken, "Order 2 has that name.");
        Fault<OrderFault, ConcurrencyFault> shared = Fault.Of(ConcurrencyFault.Stale).WithRule(Rule.Blank);

        own.Match(first => $"order {first.Reason} {first.Detail}", second => "concurrency").ShouldBe("order NameTaken Order 2 has that name.");
        shared.Match(first => "order", second => $"concurrency {second.Reason} {second.Rule}").ShouldBe("concurrency Stale ComposedFaultTests.Rule.Blank");
    }

    [Fact]
    public void A_fault_of_any_of_three_types_is_matched_to_its_own_delegate()
    {
        Fault<OrderFault, ConcurrencyFault, RemovalFault>[] faults =
            [Fault.Of(OrderFault.NotFound), Fault.Of(ConcurrencyFault.Conflict), Fault.Of(RemovalFault.StillReferenced, "Invoice 9 refers to it.")];

        faults.Select(fault => fault.Match(first => $"1:{first.Reason}", second => $"2:{second.Reason}", third => $"3:{third.Reason}:{third.Detail}"))
              .ShouldBe(["1:NotFound", "2:Conflict", "3:StillReferenced:Invoice 9 refers to it."]);
    }

    [Fact]
    public void Of_names_the_type_where_a_fault_becomes_a_result()
    {
        Result<Fault<OrderFault, ConcurrencyFault>> own = Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NotFound, "Order 7 is gone.");
        Result<Fault<OrderFault, ConcurrencyFault>> shared = Fault<OrderFault, ConcurrencyFault>.Of(Fault.Of(ConcurrencyFault.Conflict));
        Result<Fault<OrderFault, ConcurrencyFault, RemovalFault>> third = Fault<OrderFault, ConcurrencyFault, RemovalFault>.Of(RemovalFault.StillReferenced);

        own.TryGetError(out var ownFault).ShouldBeTrue();
        ownFault.ShouldBe(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));
        shared.TryGetError(out var sharedFault).ShouldBeTrue();
        sharedFault.ShouldBe(Fault.Of(ConcurrencyFault.Conflict));
        third.TryGetError(out var thirdFault).ShouldBeTrue();
        thirdFault.ShouldBe(Fault.Of(RemovalFault.StillReferenced));
    }

    /// <summary>
    /// A fault of several types holds the fault intact, its cause included, whether it converts or is made
    /// by <c>Of</c>; and a cause takes no part in its equality, as in the fault's own (decision 52).
    /// </summary>
    [Fact]
    public void The_cause_is_kept_and_takes_no_part_in_equality()
    {
        var timeout = new TimeoutException();

        Fault<OrderFault, ConcurrencyFault> converted = Fault.Of(ConcurrencyFault.Stale, null, timeout);
        var first = Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NotFound, "Order 7 is gone.", timeout);
        var second = Fault<OrderFault, ConcurrencyFault>.Of(ConcurrencyFault.Conflict, null, timeout);
        var third = Fault<OrderFault, ConcurrencyFault, RemovalFault>.Of(RemovalFault.StillReferenced, null, timeout);

        converted.Match(f => f.Cause, f => f.Cause).ShouldBeSameAs(timeout);
        first.Match(f => f.Cause, f => f.Cause).ShouldBeSameAs(timeout);
        second.Match(f => f.Cause, f => f.Cause).ShouldBeSameAs(timeout);
        third.Match(f => f.Cause, f => f.Cause, f => f.Cause).ShouldBeSameAs(timeout);
        Fault<OrderFault, ConcurrencyFault, RemovalFault>.Of(OrderFault.NameTaken, null, timeout).Match(f => f.Cause, f => null, f => null).ShouldBeSameAs(timeout);
        Fault<OrderFault, ConcurrencyFault, RemovalFault>.Of(ConcurrencyFault.Stale, null, timeout).Match(f => null, f => f.Cause, f => null).ShouldBeSameAs(timeout);
        first.ShouldBe(Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NotFound, "Order 7 is gone."));
        first.GetHashCode().ShouldBe(Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NotFound, "Order 7 is gone.").GetHashCode());
        first.ToString().ShouldBe(Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NotFound, "Order 7 is gone.").ToString());
    }

    [Fact]
    public void Two_faults_are_equal_when_they_hold_the_same_type_s_equal_fault()
    {
        Fault<OrderFault, ConcurrencyFault> fault = Fault.Of(OrderFault.NotFound, "Order 7 is gone.");

        fault.ShouldBe(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));
        fault.ShouldNotBe(Fault.Of(OrderFault.NotFound));
        fault.ShouldNotBe(Fault.Of(ConcurrencyFault.Conflict, "Order 7 is gone."));
        (fault == Fault.Of(OrderFault.NotFound, "Order 7 is gone.")).ShouldBeTrue();
        fault.GetHashCode().ShouldBe(((Fault<OrderFault, ConcurrencyFault>)Fault.Of(OrderFault.NotFound, "Order 7 is gone.")).GetHashCode());
    }

    /// <summary>Same numeric value, two enum types: the type held tells them apart.</summary>
    [Fact]
    public void Faults_of_two_types_with_the_same_value_are_unequal()
    {
        Fault<OrderFault, ConcurrencyFault> order = Fault.Of(OrderFault.NotFound);
        Fault<OrderFault, ConcurrencyFault> concurrency = Fault.Of(ConcurrencyFault.Conflict);

        ((int)OrderFault.NotFound).ShouldBe((int)ConcurrencyFault.Conflict);
        order.ShouldNotBe(concurrency);
    }

    [Fact]
    public void It_prints_the_fault_it_holds()
    {
        ((Fault<OrderFault, ConcurrencyFault>)Fault.Of(OrderFault.NotFound, "Order 7 is gone.")).ToString()
            .ShouldBe("Fault { Reason = NotFound, Detail = Order 7 is gone. }");
        ((Fault<OrderFault, ConcurrencyFault, RemovalFault>)Fault.Of(ConcurrencyFault.Stale).WithRule(Rule.Blank)).ToString()
            .ShouldBe("Fault { Reason = Stale, Detail = , Rule = ComposedFaultTests.Rule.Blank }");
        default(Fault<OrderFault, ConcurrencyFault>).ToString().ShouldBe("Fault { Uninitialized }");
    }

    [Fact]
    public void A_default_one_holds_no_fault_and_refuses_to_be_matched()
    {
#pragma warning disable CMTK0001 // the default instance is the subject
        var two = default(Fault<OrderFault, ConcurrencyFault>);
        var three = default(Fault<OrderFault, ConcurrencyFault, RemovalFault>);
#pragma warning restore CMTK0001

        Should.Throw<InvalidOperationException>(() => two.Match(_ => 1, _ => 2)).Message
              .ShouldStartWith("This Fault<OrderFault, ConcurrencyFault> holds no fault: it is a default instance");
        Should.Throw<InvalidOperationException>(() => three.Match(_ => 1, _ => 2, _ => 3)).Message
              .ShouldStartWith("This Fault<OrderFault, ConcurrencyFault, RemovalFault> holds no fault");
    }

    /// <summary>
    /// A <c>default</c> fault of a class reason, a closed set's, holds none and does not convert. Over an enum
    /// its reason is the member valued 0, which nothing tells from a real one; CMTK0001 refuses writing it.
    /// </summary>
    [Fact]
    public void A_default_fault_of_a_class_reason_does_not_convert()
    {
#pragma warning disable CMTK0001 // the default instance is the subject
        var empty = default(Fault<string>);
#pragma warning restore CMTK0001

        Should.Throw<InvalidOperationException>(() => (Fault<string, ConcurrencyFault>)empty).Message
              .ShouldStartWith("This Fault<String> holds no fault");
        Should.Throw<InvalidOperationException>(() => Fault<OrderFault, ConcurrencyFault, string>.Of(empty));
    }

    [Fact]
    public void A_null_delegate_is_refused_whichever_type_the_fault_is_of()
    {
        Fault<OrderFault, ConcurrencyFault> fault = Fault.Of(ConcurrencyFault.Conflict);

        Should.Throw<ArgumentNullException>(() => fault.Match(null!, _ => 2)).ParamName.ShouldBe("first");
        Should.Throw<ArgumentNullException>(() => fault.Match(_ => 1, null!)).ParamName.ShouldBe("second");
    }

    [Theory]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault.Equals(OrderFault.NotFound);")]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault.Equals(ConcurrencyFault.Stale);")]
    public void Comparing_with_a_bare_reason_through_Equals_is_a_compile_error_naming_Reason(string member)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain("Reason");
    }

    [Theory]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault == OrderFault.NotFound;", "CS0019")]
    [InlineData("Fault<OrderFault, ConcurrencyFault> Make() => OrderFault.NotFound;", "CS0029")]
    [InlineData("bool Holds(List<Fault<OrderFault, ConcurrencyFault>> faults) => faults.Contains(OrderFault.NotFound);", "CS1503")]
    // One user-defined conversion where a value becomes a result: a fault needs two to become a Result of the composed fault.
    [InlineData("Result<Fault<OrderFault, ConcurrencyFault>> Make() => Fault.Of(OrderFault.NotFound);", "CS0029")]
    public void A_bare_reason_is_no_composed_fault_and_a_fault_needs_its_type_named_to_become_a_result(string member, string error) =>
        ConsumerCompilation.Of(Consumer(member)).ErrorIds().ShouldBe([error]);

    /// <summary>What the satellite README says of one set twice: loud where it is used, not silently one of the two.</summary>
    [Theory]
    [InlineData("Fault<OrderFault, OrderFault> Make() => Fault.Of(OrderFault.NotFound);", "CS0457")]
    [InlineData("Fault<OrderFault, OrderFault> Make() => Fault<OrderFault, OrderFault>.Of(OrderFault.NotFound);", "CS0121")]
    public void One_type_twice_is_ambiguous_where_a_fault_is_made(string member, string error) =>
        ConsumerCompilation.Of(Consumer(member)).ErrorIds().Distinct().ShouldBe([error]);

    [Theory]
    [InlineData("Fault<OrderFault, ConcurrencyFault> Make() => Fault.Of(OrderFault.NotFound);")]
    [InlineData("Result<Fault<OrderFault, ConcurrencyFault>> Make() => Fault<OrderFault, ConcurrencyFault>.Of(ConcurrencyFault.Stale, \"At version 4.\");")]
    [InlineData("Result<Fault<OrderFault, ConcurrencyFault>> Make(Fault<ConcurrencyFault> shared) => Fault<OrderFault, ConcurrencyFault>.Of(shared);")]
    [InlineData("Result<int, Fault<OrderFault, ConcurrencyFault>> Make(Fault<ConcurrencyFault> shared) => Result.Error<Fault<OrderFault, ConcurrencyFault>>(shared);")]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault == Fault.Of(OrderFault.NotFound);")]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault.Equals(Fault.Of(OrderFault.NotFound));")]
    [InlineData("bool Holds(List<Fault<OrderFault, ConcurrencyFault>> faults) => faults.Contains(Fault.Of(OrderFault.NotFound));")]
    [InlineData("bool Same(Fault<OrderFault, ConcurrencyFault> fault) => fault.Match(order => order.Reason == OrderFault.NotFound, _ => false);")]
    public void Converting_a_fault_and_comparing_whole_faults_compiles(string member) =>
        ConsumerCompilation.Of(Consumer(member)).Problems().ShouldBeEmpty();

    /// <summary>
    /// <c>fault.Equals(Fault.Of(reason))</c> binds to the record's own <c>Equals</c> through the conversion,
    /// not to <c>Equals(object)</c>, where a boxed <c>Fault&lt;T&gt;</c> is never equal.
    /// </summary>
    [Fact]
    public void Equals_with_a_fault_of_one_type_compares_the_faults()
    {
        Fault<OrderFault, ConcurrencyFault> fault = Fault.Of(OrderFault.NotFound);

        fault.Equals(Fault.Of(OrderFault.NotFound)).ShouldBeTrue();
    }
}
