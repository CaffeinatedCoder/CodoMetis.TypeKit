# Spike: can a `Result` become an HTTP response, with its faults in the OpenAPI document?

**Question.** Phase 12 puts "a `Result` as an HTTP response" into `CodoMetis.TypeKit.AspNetCore`: a
success as its value, a fault as RFC 9457 problem details with a status the host maps, and the
endpoint's responses in the OpenAPI document. The domain must not reference ASP.NET, and §9's refusal
to serialize a `Result` must stand. Which shape does ASP.NET Core 10 support for that, in minimal APIs
and MVC, under Native AOT, and what happens when a host forgets to map a fault type?

**Answer: shape A, an explicit `ToHttpResult()`, with the mapping read by a transformer and checked
at startup.** Measured 2026-10-11 with ASP.NET Core 10.0.12 and Microsoft.AspNetCore.OpenApi 10.0.12,
against the real `CodoMetis.TypeKit`, under JIT (RequestDelegateFactory, minimal APIs and MVC) and
published with Native AOT (Request Delegate Generator, minimal APIs). `result.ToHttpResult()` returns
`Results<Ok<T>, FaultProblem<TError>>` (or `Results<NoContent, …>` for `Result<TError>`), so ASP.NET's
own typed-results machinery writes the response and describes the success. The fault's statuses come
from a mapping the host registers, which an operation transformer reads when the document is built,
and a startup filter refuses to start a host with an endpoint whose fault type has no mapping.
Returning the `Result` itself from a handler (shape B) works on the wire with a filter, but documents
the wrong response and needs the `Result` in a Native AOT host's JSON context.

```bash
dotnet run --project spikes/ResultHttp                         # shapes A and B, mapped and unmapped; documents in out/
dotnet run --project spikes/ResultHttp -- --mvc                # with an MVC controller beside them
dotnet run --project spikes/ResultHttp -- --no-check           # without the startup check (finding 7)
SPIKE_CANDIDATE1=1 dotnet run --project spikes/ResultHttp -- --mvc   # findings 4 and 5: the statuses read in PopulateMetadata

# Native AOT (on Homebrew .NET: LIBRARY_PATH=/opt/homebrew/lib:/opt/homebrew/opt/openssl@3/lib)
dotnet publish spikes/ResultHttp -p:Aot=true -o spikes/ResultHttp/bin/aot
spikes/ResultHttp/bin/aot/ResultHttp                           # finding 10: shape B stops the host from starting
spikes/ResultHttp/bin/aot/ResultHttp --a-only                  # findings 11 and 12
spikes/ResultHttp/bin/aot/ResultHttp --result-in-context       # finding 10: shape B with the Result in the context
SPIKE_NO_PROBLEM_DETAILS=1 spikes/ResultHttp/bin/aot/ResultHttp --a-only   # finding 11: without AddProblemDetails()
```

## The candidate

`Domain.cs` holds what a domain project holds: `OrderFault`, `Order` and methods returning
`Result<Order, OrderFault>` and `Result<OrderFault>`, with no ASP.NET in sight. `Candidate.cs` is the
satellite:

- `services.AddFaultResponses<OrderFault>(fault => fault switch { NotFound => 404, … })` registers a
  `FaultResponses<TFault>`. For an enum the statuses the document lists are read from its members
  through `Enum.GetValues<TFault>()`, which needs no dynamic code.
- `ToHttpResult()` matches the result into `TypedResults.Ok(value)` / `TypedResults.NoContent()` or a
  `FaultProblem<TFault>`, an `IResult` that resolves the mapping from the request's services and writes
  `TypedResults.Problem(status, extensions: { fault })`. The problem names the fault, never the input.
- `FaultProblem<TFault>` is an `IEndpointMetadataProvider` that records only *which* fault type the
  endpoint answers with (`FaultResponsesMetadata<TFault>`, typed, so reading its mapping needs no
  reflection).
- `FaultResponsesOperationTransformer` adds a problem-details response per mapped status, from the
  application's services, and refers to one `ProblemDetails` component.
- `FaultResponsesStartupCheck`, an `IStartupFilter`, walks the endpoints once the pipeline is built and
  throws for every endpoint whose fault type has no mapping.

Shape B (`ShapeB.cs`) returns `Result<Order, OrderFault>` from the handler and turns it into the same
response in an endpoint filter. The filter is typed per endpoint here; a package would take the type
arguments from the result through a visitor, as `GeneratedValueObjectAttribute.Accept` does.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | Shape A answers a success with `200` and the value, `Result<TError>`'s success with `204`, and each fault with its mapped status and `application/problem+json` carrying `"fault":"AlreadyShipped"`; `async` handlers too | ✅ |
| 2 | Shape A's document: `200` with the value's `$ref`, or `204`, from ASP.NET's own `Ok<T>`/`NoContent` metadata, plus one problem response per mapped status (`403`, `404`, `409` for `OrderFault`) referring to a `ProblemDetails` component | ✅ |
| 3 | An MVC action returning `Results<Ok<Order>, FaultProblem<OrderFault>>` answers and is documented exactly as the minimal API is | ✅ |
| 4 | **Candidate 1, rejected**: reading the mapping in `PopulateMetadata` from `EndpointBuilder.ApplicationServices` works for minimal APIs (a `RouteEndpointBuilder` with the application's `ServiceProvider`), but MVC calls it while building its application model with an `InertEndpointBuilder` whose services are `EndpointBuilder+EmptyServiceProvider`: the mapping is never found, and `MapControllers()` throws even with the mapping registered | ✅ |
| 5 | Candidate 1, unmapped: minimal-API endpoints are built on the first request, not at startup, and a throwing `PopulateMetadata` fails endpoint building for the whole application. Every endpoint answers `500`, those without a `Result` and the document included, on every request | ✅ |
| 6 | Candidate 2 (metadata records only the fault type; the transformer reads the mapping): MVC and minimal APIs both documented (findings 2, 3). An operation transformer runs after ASP.NET hoisted schemas into components, so a schema from `GetOrCreateSchemaAsync` stays inline; `Document.AddComponent` plus an `OpenApiSchemaReference` gives the `$ref` ASP.NET's own metadata gives | ✅ |
| 7 | Candidate 2 without the startup check, unmapped: only a fault response fails (`500`, the exception naming the fault type and `AddFaultResponses`), and the document fails; successes are served. Loud only once a fault occurs | ✅ |
| 8 | A hosted service cannot do the startup check: hosted services start before the pipeline is built, and `EndpointDataSource` then lists 0 endpoints. An `IStartupFilter` sees all of them after `next(app)` (7 here, MVC included) and its exception fails `StartAsync` before the server listens, naming each endpoint and fault type | ✅ |
| 9 | Shape B under JIT: the filter turns the result into the same response, but the document describes the `200` as `ResultOfOrderAndOrderFault`, whose schema is the type's XML doc description, and lists no problem response; the startup check cannot see it, since nothing records its fault type. Without the filter, §9's refusal answers `500` with its message, and the document still promises a `Result` | ✅ |
| 10 | Shape B under Native AOT: the host does not start. The Request Delegate Generator asks for `JsonTypeInfo` of `Result<Order, OrderFault>` when the endpoint is built. With the result added to the host's context it starts and the filter answers correctly; the unfiltered endpoint is §9's refusal again | ✅ |
| 11 | Shape A under Native AOT: no trim or AOT warning in the publish. Every fault answers `500` (`JsonTypeInfo` for `ProblemDetails` missing, the document too) until `AddProblemDetails()` is called, which puts ASP.NET's problem-details context into the host's resolver chain. The candidate calls it in `AddFaultResponses` | ✅ |
| 12 | With it, Native AOT answers and documents exactly as JIT does (findings 1, 2), and the startup check refuses an unmapped fault type. `AddProblemDetails()` also adds a `traceId` to every problem, as ASP.NET does for its own | ✅ |

## Consequences for the design

- **Shape A, explicit.** `ToHttpResult()` at the endpoint, as decision 19 prefers concise to implicit:
  the handler's return type is ASP.NET's own `Results<…>`, so the success's schema, MVC and the
  Request Delegate Generator all work without anything of ours (findings 1–3, 11). Shape B is not
  offered: it documents a `Result` (finding 9) and makes a Native AOT host put one in its context
  (finding 10), the opposite of §9.
- **The mapping lives in the host**, registered per fault type, so the domain declares faults and
  never statuses. Metadata records only the fault type; the transformer and the startup filter read
  the mapping from the application's services (findings 4, 6, 8).
- **Refuse at startup.** An endpoint whose fault type has no mapping stops the host from starting,
  naming the endpoint and the fault type (finding 8). Without it the gap shows only once a fault
  occurs in production (finding 7), and failing in `PopulateMetadata` takes the whole application
  down instead (finding 5).
- **`AddFaultResponses` calls `AddProblemDetails()`** (finding 11): without it a Native AOT host cannot
  write a single fault.
- **Not measured here, for the implementation:** a fault type that is not an enum (its statuses are
  declared with the mapping); MVC's `ProblemDetailsFactory` and a host's `CustomizeProblemDetails`
  (they apply through `AddProblemDetails()`, unverified); `TypedResults.Created` and other successes;
  OpenAPI 3.0; whether the `fault` extension follows the host's enum converter.
