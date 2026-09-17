using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class SignatureService
{
    private readonly ApiService _api;
    public SignatureService(ApiService api) => _api = api;

    // GET api/signatures/document/{docId} → lấy danh sách chữ ký của văn bản
    public async Task<List<SignatureDto>?> GetSignaturesAsync(Guid documentId)
        => await _api.GetAsync<List<SignatureDto>>($"api/signatures/document/{documentId}");

    // POST api/signatures/personal-sign → ký nháy (Manager/Admin)
    public async Task<SignResultDto?> PersonalSignAsync(SignRequestDto request)
        => await _api.PostAsync<SignResultDto>("api/signatures/personal-sign", request);

    // POST api/signatures/legal-seal → ký pháp nhân (sau khi đã có chữ ký nháy)
    public async Task<SignResultDto?> LegalSealAsync(SignRequestDto request)
        => await _api.PostAsync<SignResultDto>("api/signatures/legal-seal", request);

    // GET api/signatures/document/{docId}/verify → xác minh chữ ký
    public async Task<SignatureVerifyResult?> VerifyAsync(Guid documentId)
        => await _api.GetAsync<SignatureVerifyResult>($"api/signatures/document/{documentId}/verify");

    // Gọi personal-sign hay legal-seal theo loại thao tác trên UI.
    public async Task<SignResultDto?> SignAsync(SignRequestDto request)
    {
        if (request.SignatureType?.Equals("LegalSeal", StringComparison.OrdinalIgnoreCase) == true ||
            request.SignatureType?.Equals("DirectorSign", StringComparison.OrdinalIgnoreCase) == true)
            return await LegalSealAsync(request);

        return await PersonalSignAsync(request);
    }
}

public class SignatureVerifyResult
{
    public Guid DocId { get; set; }
    public bool IsValid { get; set; }
    public bool AllValid => IsValid;
    public int TotalSignatures { get; set; }
    public int ValidSignatures => Signatures.Count(s => s.IsValid);
    public bool HasPersonalSignature { get; set; }
    public bool HasLegalSeal { get; set; }
    public List<SignatureVerifyDetail> Signatures { get; set; } = new();
    public List<SignatureVerifyDetail> Details
    {
        get => Signatures;
        set => Signatures = value ?? new();
    }
}

public class SignatureVerifyDetail
{
    public Guid SignatureId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public bool IsIntact { get; set; }
    public bool IsValid
    {
        get => IsIntact;
        set => IsIntact = value;
    }
    public string? FailReason { get; set; }
}
