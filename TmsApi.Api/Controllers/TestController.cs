using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using TmsApi.Infrastructure.Services;
using TmsApi.Application.Interfaces;
using TmsApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace TmsApi.Api.Controllers;
[ApiController]
[Route("api/test")]
public class TestController(TmsDbContext context) : ControllerBase
{
[HttpGet("deferred")]
public IActionResult TestDeferred()
{
Console.WriteLine("\n>>> STEP 1: Building the query object (nodatabase contact)...");
var query = context.Students.Where(s => s.GPA >= 3.0m);
Console.WriteLine(">>> STEP 2: Appending a sorting clause...");var orderedQuery = query.OrderBy(s => s.Name);
Console.WriteLine(">>> STEP 3: Materializing query into a C#List...");
var results = orderedQuery.ToList(); // Execution is triggeredhere
Console.WriteLine(">>> STEP 4: Materialization finished. Listpopulated.\n");
return Ok(results);
}

// Non-translatable helper method
private static bool IsHonorRoll(decimal gpa)
{
    return gpa >= 3.5m;
}

[HttpGet("translation-fail")]
public IActionResult TestTranslationFail()
{
    Console.WriteLine("\n>>> STEP 1: Running non-translatable query...");

    try
    {
        var students = context.Students.Where(s => s.GPA >= 3.5m).ToList();

        return Ok(students);
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> EXCEPTION CAUGHT: {ex.Message}\n");

        return BadRequest(new
        {
            Message = ex.Message
        });
    }
}
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
}

