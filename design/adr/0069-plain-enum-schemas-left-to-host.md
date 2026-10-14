# 0069. A plain enum's schema stays the host's

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a review of an application moving its own OpenAPI transformers onto the package;
[decision 11](0011-openapi-schema-transformer.md), [discovery](../discovery.md).

ASP.NET Core writes an enum that `JsonStringEnumConverter` reads as `{"enum": [...]}` with no `type`,
by design (dotnet/aspnetcore#61303), and an application repaired it with a document transformer of
its own, which the review proposed `AddTypeKit()` take over.

`AddTypeKit()` describes the types the package recognises by their interfaces: value objects, closed
sets, patches and fault responses. A plain enum is none of them, and a transformer for it would
change the document of every enum in a host that added `AddTypeKit()` for its value objects, with no
line to draw against the next framework schema an application repairs, a decimal's or a date's.
Where an enum reaches the document through a TypeKit type, as a value object's wrapped type or a
patch's argument, the package already describes it as the host does. The application keeps its
transformer; a closed set is the package's way to a string enum with a `type`.
