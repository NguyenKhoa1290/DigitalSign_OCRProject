namespace DocumentService.Core.Exceptions;

/// <summary>Base exception cho DocumentService</summary>
public class DocumentServiceException : Exception
{
    public DocumentServiceException(string message) : base(message) { }
    public DocumentServiceException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Không tìm thấy văn bản</summary>
public class DocumentNotFoundException : DocumentServiceException
{
    public DocumentNotFoundException(Guid id)
        : base($"Không tìm thấy văn bản với ID: {id}") { }

    public DocumentNotFoundException(string code)
        : base($"Không tìm thấy văn bản với mã: {code}") { }
}

/// <summary>Chuyển trạng thái workflow không hợp lệ</summary>
public class InvalidWorkflowTransitionException : DocumentServiceException
{
    public InvalidWorkflowTransitionException(string fromStatus, string toStatus)
        : base($"Không thể chuyển trạng thái từ '{fromStatus}' sang '{toStatus}'.") { }
}

/// <summary>Người dùng không có quyền thực hiện thao tác này trên văn bản</summary>
public class UnauthorizedDocumentAccessException : DocumentServiceException
{
    public UnauthorizedDocumentAccessException()
        : base("Bạn không có quyền thực hiện thao tác này.") { }

    public UnauthorizedDocumentAccessException(string message)
        : base(message) { }
}
