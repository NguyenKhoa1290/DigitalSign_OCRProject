namespace IdentityService.Core.Common;

/// <summary>
/// Kết quả phân trang chung dùng cho tất cả các danh sách có phân trang trong hệ thống.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của từng phần tử trong danh sách.</typeparam>
public class PagedResult<T>
{
    /// <summary>Danh sách các phần tử trong trang hiện tại.</summary>
    public IEnumerable<T> Items { get; private set; } = Enumerable.Empty<T>();

    /// <summary>Tổng số bản ghi trong toàn bộ tập dữ liệu (không phân trang).</summary>
    public int TotalCount { get; private set; }

    /// <summary>Số trang hiện tại (bắt đầu từ 1).</summary>
    public int Page { get; private set; }

    /// <summary>Số bản ghi tối đa trên mỗi trang.</summary>
    public int PageSize { get; private set; }

    /// <summary>Tổng số trang dựa trên TotalCount và PageSize.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>Cho biết có trang tiếp theo hay không.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Cho biết có trang trước đó hay không.</summary>
    public bool HasPreviousPage => Page > 1;

    // Hàm khởi tạo private để kiểm soát việc tạo instance
    private PagedResult() { }

    /// <summary>
    /// Tạo một PagedResult với dữ liệu đã được phân trang.
    /// </summary>
    /// <param name="items">Danh sách các phần tử của trang hiện tại.</param>
    /// <param name="totalCount">Tổng số bản ghi trong cơ sở dữ liệu.</param>
    /// <param name="page">Số trang hiện tại (bắt đầu từ 1).</param>
    /// <param name="pageSize">Số bản ghi mỗi trang.</param>
    /// <returns>Instance PagedResult được khởi tạo đầy đủ.</returns>
    public static PagedResult<T> Create(IEnumerable<T> items, int totalCount, int page, int pageSize)
    {
        return new PagedResult<T>
        {
            Items = items ?? Enumerable.Empty<T>(),
            TotalCount = totalCount,
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 10 : pageSize
        };
    }
}
