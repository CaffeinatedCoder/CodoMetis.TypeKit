using System.Text.Json;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-038: System.Text.Json writes a <c>byte[]</c> as one base64 string and reads it back from one, where
/// every other collection of bytes, and an array of any other element, is a JSON array.
/// </summary>
public sealed class KB038
{
    public readonly record struct Octet(byte Value);

    [Fact]
    public void A_byte_array_is_a_base64_string() =>
        JsonSerializer.Serialize(new byte[] { 7, 255 }).ShouldBe("\"B/8=\"");

    [Fact]
    public void A_byte_array_reads_back_from_base64_and_not_from_a_JSON_array()
    {
        JsonSerializer.Deserialize<byte[]>("\"B/8=\"").ShouldBe(new byte[] { 7, 255 });
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<byte[]>("[7,255]"));
    }

    [Fact]
    public void A_list_of_bytes_is_a_JSON_array_of_numbers() =>
        JsonSerializer.Serialize(new List<byte> { 7, 255 }).ShouldBe("[7,255]");

    [Fact]
    public void An_array_of_another_element_over_a_byte_is_a_JSON_array() =>
        JsonSerializer.Serialize(new[] { new Octet(7) }).ShouldBe("""[{"Value":7}]""");
}
