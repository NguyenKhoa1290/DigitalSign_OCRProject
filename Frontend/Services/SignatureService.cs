using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class SignatureService
{
    private readonly ApiService _api;
    public SignatureService(ApiService api) => _api = api;

    // GET api/signatures/document/{docId} → lấy danh sách chữ ký của văn bản
    public async Task<List<SignatureDto>?> GetSignaturesAsync(Guid documentId)
        => await _api.GetAsync<List<SignatureDto>>($"api/signatures/document/{documentId}");

    // POST api/signatures/personal-sign → ký cá nhân (Trưởng phòng / BGH)
    public async Task<SignatureDto?> PersonalSignAsync(Guid documentId, string? comment = null)
        => await _api.PostAsync<SignatureDto>("api/signatures/personal-sign", new { DocumentId = documentId, Comment = comment });

    // POST api/signatures/legal-seal → đóng dấu pháp nhân (sau khi có đủ chữ ký)
    public async Task<SignatureDto?> LegalSealAsync(Guid documentId, string? comment = null)
        => await _api.PostAsync<SignatureDto>("api/signatures/legal-seal", new { DocumentId = documentId, Comment = comment });

    // GET api/signatures/document/{docId}/verify → xác minh chữ ký
    public async Task<SignatureVerifyResult?> VerifyAsync(Guid documentId)
        => await _api.GetAsync<SignatureVerifyResult>($"api/signatures/document/{documentId}/verify");

    // Backward-compat: gọi personal-sign hay legal-seal theo SignatureType
    public async Task<SignatureDto?> SignAsync(Guid documentId, SignRequestDto request)
    {
        if (request.SignatureType?.Equals("LegalSeal", StringComparison.OrdinalIgnoreCase) == true)
            return await LegalSealAsync(documentId, request.Comment);
        return await PersonalSignAsync(documentId, request.Comment);
    }
}

public class SignatureVerifyResult
{
    public bool AllValid { get; set; }
    public int TotalSignatures { get; set; }
    public int ValidSignatures { get; set; }
    public List<SignatureVerifyDetail> Details { get; set; } = new();
}

public class SignatureVerifyDetail
{
    public Guid SignatureId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? FailReason { get; set; }
}
