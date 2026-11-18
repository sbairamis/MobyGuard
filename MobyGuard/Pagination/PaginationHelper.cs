namespace MobyGuard.Pagination;

public class PaginationHelper
{
    public static PagedResponse<List<T>> CreatePagedResponse<T>(
        List<T> pagedData,
        PaginationFilter validFilter,
        int totalRecords)
    {
        var response = new PagedResponse<List<T>>(pagedData, validFilter.PageNumber, validFilter.PageSize);
        var totalPages = (double)totalRecords / validFilter.PageSize;
        var roundedTotalPages = Convert.ToInt32(Math.Ceiling(totalPages));

        response.TotalPages = roundedTotalPages;
        response.TotalRecords = totalRecords;
        return response;
    }
}
