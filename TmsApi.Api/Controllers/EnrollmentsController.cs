using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TmsApi.Application.Enrollments.Commands;
using TmsApi.Application.Enrollments.Queries;
using TmsApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TmsApi.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using TmsApi.Application.Hubs;
using TmsApi.Application.Interfaces;
using TmsApi.Api.Hubs;

[ApiController]
[Route("api/v{version:apiVersion}/enrollments")]
[ApiVersion("2.0")]
public class EnrollmentsController(IMediator mediator, TmsDbContext context, IEnrollmentServices enrollmentService, IHubContext<EnrollmentHub, ITmsHubClient> hubContext) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Enroll(
    EnrollStudentCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.Match<IActionResult>(
        onSuccess: created => CreatedAtAction(
        nameof(GetSchedule),
        new { studentId = created.StudentId },
        created),
        onFailure: error =>
        {
            var status = error.Code switch
            {
                "course_not_found" => StatusCodes.Status404NotFound,
                "course_full" or "already_enrolled" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Problem(
    statusCode: status,
    title: "Enrollment rejected",
    detail: error.Message,
    type: $"https://tms.local/errors/{error.Code}");
        });
    }
    [HttpGet("{studentId}/schedule")]
    public async Task<IActionResult> GetSchedule(
    int studentId, CancellationToken ct)
    {
        var schedule = await mediator.Send(
        new GetStudentScheduleQuery(studentId), ct);
        return Ok(schedule);
    }
[HttpGet]
public async Task<IActionResult> GetAll(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken ct = default)
{
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 50);

    var baseQuery = context.Enrollments
        .AsNoTracking();

    var totalCount = await baseQuery.CountAsync(ct);

    var rows = await baseQuery
        .OrderByDescending(e => e.EnrolledAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(e => new
        {
            e.Id,
            e.StudentId,
            StudentName = e.Student.Name,
            e.CourseId,
            CourseName = e.Course.Title,
            e.EnrolledAt
        })
        .ToListAsync(ct);

    var totalPages = (int)Math.Ceiling(
        totalCount / (double)pageSize);

    return Ok(new
    {
        data = rows,
        meta = new
        {
            totalCount,
            page,
            pageSize,
            totalPages,
            hasNext = page < totalPages,
            hasPrevious = page > 1
        }
    });
}

[HttpPost("{id}/approve")]
public async Task<IActionResult> Approve(
    int id,
    CancellationToken ct)
{
    var approved = await enrollmentService.ApproveAsync(id, ct);

    if (!approved)
        return NotFound();

    await hubContext.Clients.All
        .ReceiveEnrollmentStatusUpdated(
            id.ToString(),
            "Approved");

    return NoContent();
}
}
