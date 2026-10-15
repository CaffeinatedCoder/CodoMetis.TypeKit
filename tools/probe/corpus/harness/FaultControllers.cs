// Controllers answering with faults; the Plain* copies have no [ApiController] (http.host=controller-without-ApiController).
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
namespace Probes;

[ApiController]
public sealed class MvcAController : ControllerBase
{
    [HttpGet("c/two")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Two(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/wo"), WithoutFaults<HFault>(nameof(HFault.Missing), nameof(HFault.Bad))]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Wo(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/fs"), FaultStatus<HFault>(nameof(HFault.Clash), 422)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Fs(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/three")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>, FaultProblem<SFault>> Three(int e) => Errors.Three(e).ToHttpResult();
    [HttpGet("c/declared404"), ProducesResponseType<ProblemDetails>(404)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Declared404(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/declared"), ProducesResponseType<ProblemDetails>(429)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Declared(int e) => Errors.Two(e).ToHttpResult();
}
[ApiController, FaultStatus<HFault>(nameof(HFault.Denied), 451), WithoutFaults<HFault>(nameof(HFault.Down))]
public sealed class MvcBController : ControllerBase
{
    [HttpGet("d/two")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Two(int e) => Errors.Two(e).ToHttpResult();
}


public sealed class PlainAController : ControllerBase
{
    [HttpGet("c/two")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Two(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/wo"), WithoutFaults<HFault>(nameof(HFault.Missing), nameof(HFault.Bad))]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Wo(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/fs"), FaultStatus<HFault>(nameof(HFault.Clash), 422)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Fs(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/three")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>, FaultProblem<SFault>> Three(int e) => Errors.Three(e).ToHttpResult();
    [HttpGet("c/declared404"), ProducesResponseType<ProblemDetails>(404)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Declared404(int e) => Errors.Two(e).ToHttpResult();
    [HttpGet("c/declared"), ProducesResponseType<ProblemDetails>(429)]
    public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Declared(int e) => Errors.Two(e).ToHttpResult();
}
[FaultStatus<HFault>(nameof(HFault.Denied), 451), WithoutFaults<HFault>(nameof(HFault.Down))]
public sealed class PlainBController : ControllerBase
{
    [HttpGet("d/two")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<GFault>> Two(int e) => Errors.Two(e).ToHttpResult();
}

