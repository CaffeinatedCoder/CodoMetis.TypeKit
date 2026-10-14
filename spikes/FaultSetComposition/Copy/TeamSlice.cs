using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using FaultSetComposition.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FaultSetComposition.Copy;

public static class TeamService
{
    public static Result<Guid, Fault<TeamFault>> Register(string? name, CommandOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result.Error(Fault.Of(TeamFault.NameRequired));
        if (name == "taken") return Result.Error(Fault.Of(TeamFault.NameInUse));
        if (outcome != CommandOutcome.Applied) return Result.Error(TeamFault.From(SharedFaults.Translate(outcome, "The team")));
        return Guid.CreateVersion7();
    }

    public static Result<Fault<TeamFault>> Rename(Guid id, string? name, CommandOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(name)) return Fault.Of(TeamFault.NameRequired);
        if (id == Guid.Empty) return Fault.Of(TeamFault.NotFound);
        if (name == "taken") return Fault.Of(TeamFault.NameInUse);
        return outcome == CommandOutcome.Applied ? Result<Fault<TeamFault>>.Success() : TeamFault.From(SharedFaults.Translate(outcome, "The team"));
    }

    public static Result<Fault<TeamFault>> Remove(Guid id, RemovalOutcome outcome) =>
        outcome switch
        {
            RemovalOutcome.Removed => Result<Fault<TeamFault>>.Success(),
            RemovalOutcome.NotFound => Fault.Of(TeamFault.NotFound),
            _ => TeamFault.From(SharedFaults.Translate(outcome)),
        };
}

public static class TeamEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/copy/teams");
        group.MapPost("/", Register);
        group.MapPut("/{id:guid}", Rename);
        group.MapDelete("/{id:guid}", Remove);
    }

    private static Results<Created, FaultProblem<TeamFault>> Register(TeamRequest request, CommandOutcome outcome = CommandOutcome.Applied) =>
        TeamService.Register(request.Name, outcome).ToHttpResult(id => TypedResults.Created($"/copy/teams/{id}"));

    private static Results<NoContent, FaultProblem<TeamFault>> Rename(Guid id, TeamRequest request, CommandOutcome outcome = CommandOutcome.Applied) =>
        TeamService.Rename(id, request.Name, outcome).ToHttpResult();

    private static Results<NoContent, FaultProblem<TeamFault>> Remove(Guid id, RemovalOutcome outcome = RemovalOutcome.Removed) =>
        TeamService.Remove(id, outcome).ToHttpResult();
}
