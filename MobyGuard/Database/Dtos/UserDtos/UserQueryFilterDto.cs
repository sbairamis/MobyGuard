using MobyGuard.Pagination;

namespace MobyGuard.Database.Dtos.UserDtos;

public class UserQueryFilterDto : PaginationFilter
{
    public string? Username { get; set; }
    public int? UserTypeId { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
