# Spike: what keeps a sensitive value object out of logs?

**Question.** Phase 12 lists "sensitive value objects": a marker that keeps a value out of `ToString()`,
the debugger and logs. Where does a value object's value reach printed text today, what does the
`ToString()` seam the generators already keep (a hand-written `ToString()`, after which no formatting
interface is generated) cover, and what would a marker add?

**Answer: the seam covers almost everything; a marker would add nothing.** Measured 2026-10-11 against
value objects woven by the real generators, with Microsoft.Extensions.Logging 10.0.12,
Microsoft.Extensions.Telemetry and Compliance 10.10.0, and Serilog 4.4.0. A value object that declares
`ToString()` (`MaskedEmail`) is masked in interpolation, `string.Format`, `Convert.ToString`, a record's
`ToString()`, every Microsoft.Extensions.Logging path (templates, the JSON console formatter,
`[LoggerMessage]`, `[LogProperties]`) and Serilog's `{x}` (findings 1–3). It still reaches text
through Serilog's `{@x}`, which reads the public `Value` (finding 4); a destructuring policy that logs
every value object as what it prints closes that (finding 5). Classifying the type for
Microsoft.Extensions.Compliance does not work: the logging generator refuses the method (finding 6).

```bash
dotnet run --project spikes/SensitiveValues                     # every sink; LEAK marks the address in the text
dotnet build spikes/SensitiveValues -p:ClassifiedType=true      # finding 6: LOGGEN035, then CS8795
```

`Email` prints its value, as generated; `MaskedEmail` declares `ToString() => "MaskedEmail(***)"`;
`ClassifiedEmail` also carries a `DataClassificationAttribute` on the type. `Policy.cs` is the
prototype Serilog destructuring policy.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | With the seam, `ToString()`, interpolation, `string.Format` and `Convert.ToString` print the mask; no `IFormattable` is generated. JSON still writes the value, as the wire must. The generated `TypeConverter` also converts to the value: it is a conversion, as JSON is, not a display | ✅ |
| 2 | A record holding it prints the mask (`Signup { Email = ann@…, Masked = MaskedEmail(***) }`), since `PrintMembers` calls the value object's `ToString()` | ✅ |
| 3 | Microsoft.Extensions.Logging prints the mask everywhere it was measured: a message template (message and state), a record in a template, the JSON console formatter's `State`, `[LoggerMessage]`, and `[LogProperties]` on a record (`signup.Masked=MaskedEmail(***)`). A `DataClassificationAttribute` on the *parameter* with `EnableRedaction()` erases it, as documented | ✅ |
| 4 | Serilog: `{Masked}` prints the mask, but `{@Masked}` destructures the public properties, `MaskedEmail { Value: "ann@example.com" }`, and so does `{@Signup}` for every value object inside. Every value object, masked or not, is logged as `{ Value: … }` there | ✅ |
| 5 | A destructuring policy that turns any value object (recognised by `GeneratedValueObjectAttribute<,>`) into a scalar of its `ToString()`: `{@Masked}` and `{@Signup}` print the mask, and an unmasked value object prints its value as a plain string instead of `{ Value: … }` | ✅ |
| 6 | A `DataClassificationAttribute` on the value object's *type*: the logging generator reports LOGGEN035 ("has a sensitive field/property in its type") and generates nothing for the method, which then fails with CS8795 | ✅ |
| 7 | The debugger: no value object declares `[DebuggerDisplay]` or hides `Value`, so the debugger shows `ToString()`, the mask, and expands `Value`. [§9](../../design/option-result.md) already holds that the debugger is not a log | ✅ |

## Consequences

- **No marker.** It would generate the one `ToString()` a user writes today, and the seam already
  reaches every formatting path (findings 1–3). What phase 12 wanted is a README section: declare
  `ToString()` to mask a value, JSON and the type converter still carry it (they are conversions), and
  the debugger still shows it.
- **Not a classification on the type** (finding 6). A host that redacts with
  Microsoft.Extensions.Compliance classifies the logging method's parameter, as Microsoft documents.
- **Serilog's `{@x}` is the one leak** (finding 4), and not only of masked values: every value object
  destructures as `{ Value: … }`. A destructuring policy closes it (finding 5) but needs a Serilog
  dependency, which no shipping package has: a satellite of its own, or a README snippet.
- **Not measured:** OpenTelemetry's log exporter, Application Insights, NLog; Serilog's policy under
  Native AOT (Serilog destructures by reflection anyway).
