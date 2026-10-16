// Claim C21-openapi-schema-parity (README.md#what-you-get), surfaces http, json, family http.
// Obligations (claims.json), each held by the one comparison below in the rows its axes select, and each with a seed of its own in the
// registry entry (decision 104):
//   the-underlying-types-schema: "The JSON, the database column and the OpenAPI schema are the same as the underlying type's."
//   nullable-admits-null-as-the-wrapped-does (http.nullability=nullable): "Its OpenAPI schema is the one ASP.NET Core publishes for the underlying type."
//   container-elements (a list's, an array's, a read-only list's, an immutable array's element, a dictionary's value): the same sentence.
//   dictionary-key (shape.position=dictionary-key): the same sentence.
//   every-document-version (openapi.OpenApiVersion OpenApi2_0, OpenApi3_0; the default is 3.1): the same sentence.
// Oracle: differential (HttpBaseline.Schema, the schema half BaseProbe used to run). On the shared host under the row's options, every
// operation that takes or returns the row's shape with the value object (the minimal schema endpoint, the minimal and the MVC binding
// endpoint of the row's http.binding) has the parameters, request body and 200 response of its twin with the wrapped type, every
// reference resolved, so a component compares by its content. Only the schemas are compared, never whether a parameter is required;
// a class value object is compared with the wrapped type's Nullable<T>, and where nullability is spelled two ways the markers go
// (schema:* notes). Not promised, and excluded from the rows: a closed set (a string enum), and a converter of the application's own for
// the value object or the wrapped type (claims.json not-promised).
// Expected: no failure for any row; a document ASP.NET alone refuses the same way has nothing to compare.
using Probes.Shapes;
namespace Probes;
public sealed class C21OpenapiSchemaParityProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C21OpenapiSchemaParityProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static Task<string?> Check(Row r) => HttpBaseline.Schema(r, Registry.Of(r));
}
