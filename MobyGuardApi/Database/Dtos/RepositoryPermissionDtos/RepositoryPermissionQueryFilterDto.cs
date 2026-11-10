using MobyGuardApi.Pagination;

namespace MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;

public class RepositoryPermissionQueryFilterDto : PaginationFilter
{
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? RepositoryPattern { get; set; }
}
