# 0036. A nullable value object admits null in the OpenAPI document wherever it appears

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, [OpenAPI](../openapi.md).

: as a property, as a container's element (`oneOf: [null, component]`, ASP.NET's own form),
and inlined (null added to `type`, or `oneOf` around an inlined enum).
