using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using TmsApi.Data;
using Microsoft.EntityFrameworkCore;
namespace RegistrarController;
[ApiController]
[Route("api/registrar")]
public class RegistrarController(TmsDbContext context) : ControllerBase
{

[HttpGet("active-gpa-gt-3")]
public async Task<IActionResult> ActiveGpaGT3()
    {
        var count = await context.Students.Where(s => s.IsActive && s.GPA >= 3.0m).CountAsync();
        return Ok(count);
    }
[HttpGet("mostenrollmentsdescending")]
public async Task<IActionResult> MostEnrollmentsDescending()
    {
        var list = await context.Courses
.Select(c => new
{
c.Title,
EnrollmentCount = c.Enrollments.Count
})
.OrderByDescending(x => x.EnrollmentCount)
.ToListAsync();

return Ok(list);
    }
[HttpGet("avg-gpa-percourse")]
public async Task<IActionResult> AvgGpaPerCourse()
    {
        var list = await context.Enrollments
.GroupBy(e => e.Course.Title)
.Select(g => new
{
Course = g.Key,
AverageGPA = g.Average(e => e.Student.GPA)
})
.ToListAsync();

return Ok(list);
    }

[HttpGet("zero-enrollments")]
public async Task<IActionResult> ZeroEnrollments()
    {
        var list = await context.Students
.Where(s => !s.Enrollments.Any())
.Select(s => s.Name)
.ToListAsync();
return Ok(list);
    }

[HttpGet("students")]
public async Task<IActionResult> GetStudents(
    int page = 1,
    CancellationToken cancellationToken = default)
{
    const int pageSize = 10;

    var students = await context.Students
        .OrderBy(s => s.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);

    return Ok(students);
}

[HttpGet("top-courses")]
public async Task<IActionResult> GetTopCourses(
    CancellationToken cancellationToken = default)
{
    var result = await context.Enrollments
        .GroupBy(e => new { e.Course.Id, e.Course.Title })
        .Select(g => new
        {
            CourseTitle = g.Key.Title,
            EnrollmentCount = g.Count()
        })
        .OrderByDescending(x => x.EnrollmentCount)
        .Take(5)
        .ToListAsync(cancellationToken);

    return Ok(result);
}
[HttpGet("nplusone")]
public async Task<IActionResult> NPlusOne()
{
    var students = await context.Students
        .AsNoTracking()
        .ToListAsync();

    foreach (var s in students)
    {
        var count = await context.Enrollments
            .AsNoTracking()
            .CountAsync(e => e.StudentId == s.Id);

        Console.WriteLine($"{s.Name}: {count} enrollments");
    }

    return Ok();
}
[HttpGet("nplusone-fixed")]
public async Task<IActionResult> NPlusOneFixed()
{
    var report = await context.Students
        .AsNoTracking()
        .Select(s => new
        {
            s.Name,
            EnrollmentCount = s.Enrollments.Count
        })
        .ToListAsync();

    return Ok(report);
}
}

