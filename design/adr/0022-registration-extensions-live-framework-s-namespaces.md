# 0022. Registration extensions live in the framework's namespaces

Date: 2026-09-28
Status: accepted

Context: 2026-09-28.

: `UseTypeKit()` in
`Microsoft.EntityFrameworkCore`, `AddEntityFrameworkTypeKit()` and `OpenApiOptions.AddTypeKit()` in
`Microsoft.Extensions.DependencyInjection`, as EF's providers and `AddOpenApi` do, so the one line a
host adds needs no `using` (the docs audit found every setup sample missing one). Types stay in
`CodoMetis.TypeKit.*`. Moving them after 1.0.0 would break every host.
