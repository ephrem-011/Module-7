using MediatR;
using TmsApi.Application.Dtos;
using TmsApi.Application.Interfaces;

namespace TmsApi.Application.Courses.Queries;

public class SearchCoursesQueryHandler(
    ICourseService courseService)
    : IRequestHandler<SearchCoursesQuery, List<CourseResponseDto>>
{
    public async Task<List<CourseResponseDto>> Handle(
        SearchCoursesQuery request,
        CancellationToken ct)
    {
        return await courseService.GetAllAsync(ct);
    }
}