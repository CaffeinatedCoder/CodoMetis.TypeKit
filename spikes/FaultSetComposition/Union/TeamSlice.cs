using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using FaultSetComposition.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Refusal = FaultSetComposition.Union.Fault<FaultSetComposition.Union.TeamFault, FaultSetComposition.Shared.ConcurrencyFault>;
using RemovalRefusal = FaultSetComposition.Union.Fault<FaultSetComposition.Union.TeamFault, FaultSetComposition.Shared.ConcurrencyFault, FaultSetComposition.Shared.RemovalFault>;

namespace FaultSetComposition.Union;

public static class TeamService
{
    public static Result<Guid, Refusal> Register(string? name, CommandOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result.Error(Refusal.Of(TeamFault.NameRequired));
        if (name == "taken") return Result.Error(Refusal.Of(TeamFault.NameInUse));
        if (outcome != CommandOutcome.Applied) return Result.Error<Refusal>(SharedFaults.Translate(outcome, "The team"));
        return Guid.CreateVersion7();
    }

    public static Result<Refusal> Rename(Guid id, string? name, CommandOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(name)) return Refusal.Of(TeamFault.NameRequired);
        if (id == Guid.Empty) return Refusal.Of(TeamFault.NotFound);
        if (name == "taken") return Refusal.Of(TeamFault.NameInUse);
        return outcome == CommandOutcome.Applied ? Result<Refusal>.Success() : (Refusal)SharedFaults.Translate(outcome, "The team");
    }

    public static Result<RemovalRefusal> Remove(Guid id, RemovalOutcome outcome) =>
        outcome switch
        {
            RemovalOutcome.Removed => Result<RemovalRefusal>.Success(),
            RemovalOutcome.NotFound => RemovalRefusal.Of(TeamFault.NotFound),
            _ => (RemovalRefusal)SharedFaults.Translate(outcome),
        };
}

public static class TeamEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/union/teams");
        group.MapPost("/", Register);
        group.MapPut("/{id:guid}", Rename);
        group.MapDelete("/{id:guid}", Remove);
    }

    private static Results<Created, FaultProblem<TeamFault>, FaultProblem<ConcurrencyFault>> Register(TeamRequest request, CommandOutcome outcome = CommandOutcome.Applied) =>
        TeamService.Register(request.Name, outcome).ToHttpResult(id => TypedResults.Created($"/union/teams/{id}"));

    private static Results<NoContent, FaultProblem<TeamFault>, FaultProblem<ConcurrencyFault>> Rename(Guid id, TeamRequest request, CommandOutcome outcome = CommandOutcome.Applied) =>
        TeamService.Rename(id, request.Name, outcome).ToHttpResult();

    private static Results<NoContent, FaultProblem<TeamFault>, FaultProblem<ConcurrencyFault>, FaultProblem<RemovalFault>> Remove(Guid id, RemovalOutcome outcome = RemovalOutcome.Removed) =>
        TeamService.Remove(id, outcome).ToHttpResult();
}
