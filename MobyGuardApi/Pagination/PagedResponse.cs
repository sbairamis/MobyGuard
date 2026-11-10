namespace MobyGuardApi.Pagination;

public class PagedResponse<T>(T data, int pageNumber, int pageSize)
{
    public T Data { get; set; } = data;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public Uri CurrentPage { get; set; } = null!;
    public Uri FirstPage { get; set; } = null!;
    public Uri LastPage { get; set; } = null!;
    public int TotalPages { get; set; }
    public int TotalRecords { get; set; }
    public Uri? NextPage { get; set; }
    public Uri? PreviousPage { get; set; }
}
