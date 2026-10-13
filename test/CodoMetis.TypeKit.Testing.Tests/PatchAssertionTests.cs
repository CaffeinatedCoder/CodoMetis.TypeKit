namespace CodoMetis.TypeKit.Testing.Tests;

/// <summary>
/// The patch assertions hand back the value a field carried, and otherwise fail naming the state the
/// field was in, never the value.
/// </summary>
public sealed class PatchAssertionTests
{
    /// <summary>A value whose text would show if a message printed it.</summary>
    private const string Secret = "s3cr3t-value";

    [Fact]
    public void A_value_is_handed_back() => Patch.Value(Secret).ShouldBeValue().ShouldBe(Secret);

    [Fact]
    public void A_clear_is_no_value_and_is_named() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Null<string>().ShouldBeValue())
              .Message.ShouldBe("Expected a value, but the Patch<String> is Null: the field was cleared.");

    [Fact]
    public void An_absent_field_is_no_value_and_is_named() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Absent<string>().ShouldBeValue())
              .Message.ShouldBe("Expected a value, but the Patch<String> is Absent: the field was not mentioned.");

    [Fact]
    public void A_clear_is_Null() => Patch.Null<string>().ShouldBeNull();

    [Fact]
    public void A_value_is_not_Null_and_is_not_printed() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Value(Secret).ShouldBeNull())
              .Message.ShouldBe("Expected Null, a clear, but the Patch<String> holds a value (not printed).");

    [Fact]
    public void An_absent_field_is_not_Null() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Absent<string>().ShouldBeNull())
              .Message.ShouldBe("Expected Null, a clear, but the Patch<String> is Absent: the field was not mentioned.");

    [Fact]
    public void An_absent_field_is_Absent() => Patch.Absent<string>().ShouldBeAbsent();

    [Fact]
    public void A_value_is_not_Absent_and_is_not_printed() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Value(Secret).ShouldBeAbsent())
              .Message.ShouldBe("Expected Absent, but the Patch<String> holds a value (not printed).");

    [Fact]
    public void A_clear_is_not_Absent() =>
        Should.Throw<TypeKitAssertionException>(() => Patch.Null<string>().ShouldBeAbsent())
              .Message.ShouldBe("Expected Absent, but the Patch<String> is Null: the field was cleared.");

    [Fact]
    public void A_struct_patch_hands_back_its_value() => Patch.Value(3).ShouldBeValue().ShouldBe(3);
}
