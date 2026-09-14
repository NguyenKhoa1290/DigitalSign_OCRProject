namespace SignService.Core.Exceptions;

public class SignServiceException : Exception
{
    public SignServiceException(string message) : base(message) { }
}

public class DocumentNotFoundException : SignServiceException
{
    public DocumentNotFoundException(Guid docId) : base($"Không tìm thấy văn bản với ID: {docId}") { }
}

public class CertificateNotFoundException : SignServiceException
{
    public CertificateNotFoundException(Guid userId) : base($"Người dùng {userId} chưa được cấp chứng thư số. Vui lòng liên hệ Admin.") { }
}

public class CertificateExpiredException : SignServiceException
{
    public CertificateExpiredException() : base("Chứng thư số đã hết hạn. Vui lòng liên hệ Admin để gia hạn.") { }
}

public class AlreadySignedException : SignServiceException
{
    public AlreadySignedException(string signatureType) : base($"Văn bản đã có chữ ký loại '{signatureType}'.") { }
}

public class PdfSigningException : SignServiceException
{
    public PdfSigningException(string message) : base($"Lỗi ký số PDF: {message}") { }
}
