namespace MobyGuard.Pagination;

public interface IUriService
{
    public Uri GetPageUri(PaginationFilter filter, string route);
}
