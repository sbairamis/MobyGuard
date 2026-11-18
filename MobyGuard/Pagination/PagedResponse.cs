namespace MobyGuard.Pagination;

public class PagedResponse<T>(T data, int pageNumber, int pageSize)
{
    public T Data { get; set; } = data;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public int TotalPages { get; set; }
    public int TotalRecords { get; set; }
}
