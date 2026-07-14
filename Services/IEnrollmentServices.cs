using TmsApi.Dtos;

namespace TmsApi.Services;

public interface IEnrollmentServices
{
    Task<EnrollmentResponseDto?> GetByIdAsync(
        int courseId,
        int id,
        CancellationToken ct);

    Task<EnrollmentResponseDto> CreateAsync(
        int courseId,
        EnrollStudentRequest request,
        CancellationToken ct);

    Task<IReadOnlyList<EnrollmentResponseDto>> GetByCourseAsync(
        int courseId,
        CancellationToken ct);
    Task<bool> ExistsAsync(int courseId, int studentId, CancellationToken ct);
}