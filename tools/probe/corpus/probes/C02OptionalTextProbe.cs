// Claim C02-optional-text (docs/text.md#required-and-optional): "For a required field, Blank is an error. For an optional one, a blank means 'no value', and AsOptionalText() and AsOptionalIdentifier() return a Result<Option<string>, TextFault>. Hidden is an error in both cases. A null string is Blank."
// Oracle: claim + differential (AsOptionalX is AsX with Blank turned into None). Expected: blank input (null, empty, spaces, tab/newline, no-break space) -> required Error(Blank), optional Success(None);
// ZWSP-only or word+ZWSP -> Error(Hidden) in both; a word -> Some/Success of the word. In globalization-invariant mode AsText/AsOptionalText throw PlatformNotSupportedException (documented, a refusal).
#pragma warning disable xUnit1051
using System.ComponentModel;
using System.Globalization;
using CodoMetis.TypeKit;
using Probes.Shapes;
namespace Probes;
public sealed class C02OptionalTextProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C02-optional-text");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(Check(r)));

    static string? Input(string k) => k switch
    {
        "null" => null, "empty" => "", "spaces" => "   ", "tab-newline" => "\t\r\n ", "word" => "word", "word+ZWSP" => "word\u200B",
        "nbsp-only" => "\u00A0\u00A0", "ZWSP-only" => "\u200B", _ => throw new ArgumentException(k),
    };

    static string? Check(Row r)
    {
        var c = r["text.culture"];
        try { CultureInfo.CurrentCulture = c switch { "de-DE" => new("de-DE"), "tr-TR" => new("tr-TR"), "ar-SA" => new("ar-SA"), _ => CultureInfo.InvariantCulture }; }
        catch (CultureNotFoundException) { CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; r.Substituted("text.culture", "globalization-invariant", "culture unavailable in this process"); }
        bool gi; try { gi = new CultureInfo("de-DE").NumberFormat.NumberDecimalSeparator == "."; } catch (CultureNotFoundException) { gi = true; }
        if (c == "globalization-invariant" && !gi) r.Substituted("text.culture", "invariant", "the process is not in globalization-invariant mode");
        if (c != "globalization-invariant" && gi) r.Substituted("text.culture", "globalization-invariant", "the process is in globalization-invariant mode");
        if (r["process.converter-lookup"] == "wrapped-type-converter-looked-up-first") _ = TypeDescriptor.GetConverter(typeof(string));

        var entry = r["entry"]; var kind = r["input"]; var s = Input(kind);
        var expected = kind switch { "word" => "ok", "word+ZWSP" or "ZWSP-only" => "Hidden", _ => "Blank" };
        // AsText/AsOptionalText refuse in globalization-invariant mode (not-promised as-text-refused-in-globalization-invariant-mode): the row runs on the identifier counterpart, and the refusal itself is checked without credit.
        if (gi && entry is "AsText" or "AsOptionalText") // (a normal process runs these rows as they are, under the invariant culture)
        {
            var refused = false;
            try { _ = entry == "AsText" ? s.AsText() : (object)s.AsOptionalText(); } catch (PlatformNotSupportedException) { refused = true; }
            if (!refused) return $"{entry} did not throw in globalization-invariant mode (documented PlatformNotSupportedException)";
            entry = entry == "AsText" ? "AsIdentifier" : "AsOptionalIdentifier";
            r.Substituted("entry", entry, "AsText/AsOptionalText refuse in globalization-invariant mode");
        }
        var optional = entry.StartsWith("AsOptional");
        var text = entry.EndsWith("Text");
        var invariantMode = gi;
        {
            string got;
            if (optional)
            {
                var res = text ? s.AsOptionalText() : s.AsOptionalIdentifier();
                got = res.Match(o => o.Match(v => "Some:" + v, () => "None"), f => "Error:" + f);
                var want = expected switch { "ok" => "Some:word", "Blank" => "None", _ => "Error:Hidden" };
                if (got != want) return $"{entry}({kind}) = {got}; expected {want}";
            }
            else
            {
                var res = text ? s.AsText() : s.AsIdentifier();
                got = res.Match(v => "ok:" + v, f => "Error:" + f);
                var want = expected switch { "ok" => "ok:word", _ => "Error:" + expected };
                if (got != want) return $"{entry}({kind}) = {got}; expected {want}";
            }
        }
        return null;
    }
}
