using TmsApi.Domain.Entities;
using TmsApi.Application.Enrollments.Queries;
namespace TmsApi.Application.Interfaces;

public interface IEnrollmentRepository
{
    Task<bool> ExistsAsync(
        int studentId,
        string courseCode,
        CancellationToken ct);

    Task AddAsync(
        Enrollment enrollment,
        CancellationToken ct);
}