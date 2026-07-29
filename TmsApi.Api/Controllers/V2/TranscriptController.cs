using MediatR;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using TmsApi.Application.Courses.Queries;

namespace TmsApi.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/transcripts")]
[ApiVersion("2.0")]
[Tags("Transcripts")]
public class TranscriptsController (IMediator mediator) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("transcripts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult RequestTranscript([FromBody] object? _)
    {
        // Stub: Exercise 5 will replace this with
        // enqueue + 202 Accepted + Location header.
        return Ok();
    }

    [HttpGet("search")]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> SearchCourses(
[FromQuery] string? term, CancellationToken ct)
    {
        var results = await mediator.Send(new SearchCoursesQuery(term), ct); return Ok(results);
    }
}