# 0106. A parameter of a value object over a described type is written inline, never as its component

Date: 2026-10-17
Status: accepted

Context: 2026-10-17, finding A3 of the code-analyst run of 2026-10-16 (AspNetCore): a value object over a type the
JSON options describe, such as `Money : IValue<decimal>` under `DecimalStringJsonConverter`, was described as its JSON
spelling (`"type":"string"`, whose pattern refuses `1.50`, which `TryParse` binds) as a minimal-API query, header and
form parameter, and as the items of a query or header array in minimal APIs and MVC. `ValueObjectSchemaTransformer`
asked ASP.NET for the wrapped type without the parameter's description, so `DescribedSchemaTransformer` could not tell
it was a parameter's. The fix asks for it as the parameter's. That settles the schema where value objects are inlined;
where they are components, it does not.

Where value objects are components, ASP.NET writes such a parameter as a reference to the value object's component.
That component is shared with every JSON position (a property, a body, a response, an element), and it is the schema of
the first use ASP.NET meets ([KB-027](../knowledge/KB-027-an-enum-component-is-its-first-use.md)). A parameter of
`Money` and a property of `Money` cannot both be right through one component: the parameter is the `decimal`
`TryParse` reads, the property the string the converter writes. Measured with the fix in place and the reference kept,
in one document: `ParameterMoney`, met first as a parameter, got the parameter's number as its component, and its JSON
property referred to it (6 rows of `A_value_object_s_JSON_positions_keep_the_described_spelling_beside_parameters_of_it`,
every components row); `ProbeAmount`, met first in JSON through the probe endpoints mapped before, got the string, and
its parameters referred to that (48 parameter rows, every components row of a value object outside the body).

Three ways were weighed:

- **Keep the reference.** Whichever position comes first is wrong for the other, by the order of the endpoints.
- **Write every value-object parameter inline.** Sound, but it changes the document of every application with a
  value-object parameter in component mode, where the parameter's schema and the component are the same.
- **Write inline only where they differ:** a parameter, or an element of one, of a value object that wraps a type the
  options describe, directly or through the value objects it wraps.

## Decision

**A value object over a described type is written inline outside the body.** As a route, query, header or form
parameter and as an element of a query or header array, where the host makes value objects components, its schema is
written in place, as ASP.NET writes a parameter of the underlying type, which has no component. Its JSON positions keep
referring to the component, which is what the converter writes. `ValueObjectSchemaTransformer` takes away the name
ASP.NET reads the component from (the `x-schema-id` metadata, ASP.NET's own key), and puts back on the schema the
default ASP.NET set aside for the reference (`x-ref-default`).

**Every other value object keeps its reference.** A value object over a type nothing describes is the same schema as
a parameter and in JSON, and stays a reference to its component, as before.

**The schema below a parameter is asked for without the parameter's own description.** The wrapped type and an element
are asked for with the parameter's source and type only (`Detached`): ASP.NET applies a parameter's default and its
validation attributes to the schema it is asked for, and they are already on the parameter's. Asked again, a value
object's default is written as the wrapped type, and ASP.NET throws (`InvalidCastException`, measured); an array's
attributes would be each element's.

## Consequences

- In component mode a parameter of `Money` reads `{"type":["number","string"],"format":"double",...}` in place, and a
  property of `Money` reads `{"$ref":"#/components/schemas/Money"}`, whose component is the string. Nothing about a
  parameter decides the component any more, whatever the order of the endpoints.
- The test is `DescribedParameterTests`: each parameter against the same parameter of `decimal` or `decimal?` in a host
  without the converter and without `AddTypeKit()`, in every references mode, and the JSON positions beside it.
- A value object whose own converter is replaced by a described one is unchanged: as a parameter it is what it was
  before, which this record does not decide.

## Evidence

- `DescribedParameterTests` at the fix: 270 rows green. With the source change reverted alone, 144 fail: every value
  object row of a minimal-API query, header, form and defaulted query, and of a query or header array in both hosts.
- With the fix and the reference kept (no inlining), 54 fail, all in component mode: the parameter rows that refer to
  the JSON component, and the JSON positions whose component became a parameter's.
- With the default not put back, 6 fail: the defaulted query parameter of a value object, components, minimal APIs.
- ASP.NET's keys, read from Microsoft.AspNetCore.OpenApi 10.0's source (`OpenApiConstants`, internal):
  `x-schema-id`, read by `ResolveReferenceForSchema` to make the component; `x-ref-default`, written by
  `ApplyDefaultValue` for a schema that will be a component and moved onto the reference by
  `AddOpenApiSchemaByReference`.
