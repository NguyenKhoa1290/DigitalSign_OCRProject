using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

namespace SignService.Infrastructure.Services;

public sealed class SigningDocumentPreparer(AppDbContext db, IMinioService storage, IHttpClientFactory clients) : ISigningDocumentPreparer
{
    private static readonly SemaphoreSlim ConversionGate = new(1,1);
    public async Task<byte[]> PrepareAsync(Guid documentId)
    {
        var document = await db.DocumentFiles.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId)
            ?? throw new DocumentNotFoundException(documentId);
        var path = document.SignedPdfPath ?? document.MinioPath;
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return await storage.DownloadFileAsync(ObjectName(path));
        if (document.SigningStartedAt.HasValue)
            throw new IncomingConflictException("Hồ sơ đã khóa ký nhưng thiếu bản PDF đã ký. Cần quản trị viên đối soát.");
        byte[] word;
        if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            word = await storage.DownloadFileAsync(ObjectName(path));
        else
        {
            JsonObject? raw;
            try { raw = JsonNode.Parse(document.OcrDataRaw ?? "null") as JsonObject; }
            catch { throw new SignServiceException("Kết quả OCR không hợp lệ. Chưa thể ký ảnh."); }
            if (raw != null && raw["sourcePath"] == null && Guid.TryParse(raw["jobId"]?.ToString(),out var job))
            {
                // Older saved OCR omitted sourcePath. Bind only through its
                // successful, document-scoped job; never infer from a filename.
                var jobPath = await db.Database.SqlQuery<string>($"SELECT \"SourcePath\" AS \"Value\" FROM \"OcrJobs\" WHERE \"Id\"={job} AND \"DocumentId\"={documentId} AND \"Status\"='Succeeded'").SingleOrDefaultAsync();
                if (jobPath == path) raw["sourcePath"] = path;
            }
            var active = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM \"OcrJobs\" WHERE \"DocumentId\"={documentId} AND \"Status\" IN ('Queued','Running')) AS \"Value\"").SingleAsync();
            if (active) throw new IncomingConflictException("OCR đang xử lý. Hãy đợi kết quả cuối trước khi ký.");
            ValidateOcr(raw, path);
            if (raw!["editedDocument"] is JsonObject edited)
            {
                var editedPath = edited["minioPath"]?.ToString();
                if (edited["sourcePath"]?.ToString() != path || string.IsNullOrWhiteSpace(editedPath)
                    || !System.Text.RegularExpressions.Regex.IsMatch(editedPath, @"^documents/[0-9a-f-]+\.docx$"))
                    throw new IncomingConflictException("Bản Word OCR hiệu chỉnh không khớp tệp nguồn.");
                word = await storage.DownloadFileAsync(ObjectName(editedPath));
            }
            else
            {
                using var client = clients.CreateClient("signing-word-export");
                using var response = await client.PostAsJsonAsync("api/ocr/internal/export-layout", new { layout = raw["layout"] });
                if (!response.IsSuccessStatusCode) throw new SignServiceException("Chưa xuất được bản Word OCR để ký. Hãy thử lại.");
                word = await response.Content.ReadAsByteArrayAsync();
            }
        }
        return await ConvertWordAsync(word);
    }

    public static void ValidateOcr(JsonObject? raw, string path)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"\.(png|jpe?g|webp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new SignServiceException("Chỉ ký PDF, Word hoặc nội dung OCR của ảnh.");
        if (raw?["provider"]?.ToString() != "gemini" || raw["layout"]?["blocks"] is not JsonArray { Count: > 0 })
            throw new SignServiceException("Ảnh chưa có nội dung OCR. Hãy chạy OCR và kiểm tra nội dung trước khi ký.");
        if (raw["sourcePath"]?.ToString() != path)
            throw new IncomingConflictException("Nội dung OCR không khớp ảnh hiện tại. Hãy chạy OCR lại trước khi ký.");
    }

    public static async Task<byte[]> ConvertWordAsync(byte[] word)
    {
        if (word.Length is 0 or > 50 * 1024 * 1024) throw new SignServiceException("Bản Word không hợp lệ hoặc vượt 50 MB.");
        // Conversion never runs macros or resolves external document content.
        try
        {
            using var archive = new ZipArchive(new MemoryStream(word));
            if (archive.GetEntry("word/document.xml") == null || archive.Entries.Sum(e => e.Length) > 100L * 1024 * 1024
                || archive.Entries.Count > 3000 || archive.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException();
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                if (XDocument.Load(stream).Descendants().Any(e => e.Name.LocalName == "Relationship"
                    && (string?)e.Attribute("TargetMode") == "External" && !((string?)e.Attribute("Type") ?? "").EndsWith("/hyperlink")))
                    throw new InvalidDataException();
            }
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException)
        { throw new SignServiceException("Bản Word hỏng hoặc chứa nội dung liên kết ngoài không được phép ký."); }
        var directory = Path.Combine(Path.GetTempPath(), "hau-sign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!await ConversionGate.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Directory.Delete(directory,true);
            throw new SignServiceException("Bộ chuyển Word sang PDF đang bận. Hãy thử ký lại sau ít phút.");
        }
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "content.docx"), word);
            var start = new ProcessStartInfo("soffice") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-env:UserInstallation=" + new Uri(Path.Combine(directory, "profile")).AbsoluteUri,
                "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to", "pdf:writer_pdf_Export", "--outdir", directory, Path.Combine(directory, "content.docx") }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new SignServiceException("Không khởi động được bộ chuyển Word sang PDF.");
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new SignServiceException("Chuyển Word sang PDF quá thời gian. Chưa ký tài liệu."); }
            await Task.WhenAll(output, errors);
            var file = Path.Combine(directory, "content.pdf");
            if (process.ExitCode != 0 || !File.Exists(file)) throw new SignServiceException("Không chuyển được bản Word sang PDF. Chưa ký tài liệu.");
            return await File.ReadAllBytesAsync(file);
        }
        finally { try { Directory.Delete(directory, true); } finally { ConversionGate.Release(); } }
    }
    private static string ObjectName(string path) => path.StartsWith("documents/", StringComparison.Ordinal) ? path[10..] : path;
}
