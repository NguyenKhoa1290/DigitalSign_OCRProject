using System.Security.Cryptography;
using System.Text;
using Npgsql;
using DocumentService.Core.Exceptions;

namespace DocumentService.API.Services;

public sealed class PublicDocumentLinks(NpgsqlDataSource source)
{
    public static async Task InitializeAsync(NpgsqlDataSource source)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS "PublicDocumentLinks" (
                "DocumentId" uuid PRIMARY KEY REFERENCES "Documents"("Id") ON DELETE CASCADE,
                "TokenHash" text NOT NULL UNIQUE,
                "SignedPdfPath" text NOT NULL,
                "CreatedById" uuid NOT NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now()
            );
            """);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> CreateAsync(Guid id, Guid actor, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var document = new NpgsqlCommand("""
            SELECT d."SignedPdfPath" FROM "Documents" d
            WHERE d."Id"=@id AND NOT d."IsDeleted" AND d."Status"='Published'
              AND d."SignedPdfPath" IS NOT NULL AND EXISTS (
                SELECT 1 FROM "Signatures" s WHERE s."DocId"=d."Id"
                AND s."SignatureType"='LegalSeal' AND s."SignedFilePath"=d."SignedPdfPath")
            FOR UPDATE OF d
            """, connection, transaction);
        document.Parameters.AddWithValue("id", id);
        var path = await document.ExecuteScalarAsync(token) as string;
        if (path == null) throw new DocumentConflictException("Chỉ tạo link cho công văn đã phát hành và có PDF ký pháp nhân.");
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var create = new NpgsqlCommand("""
            INSERT INTO "PublicDocumentLinks" ("DocumentId","TokenHash","SignedPdfPath","CreatedById")
            VALUES (@id,@hash,@path,@actor)
            ON CONFLICT ("DocumentId") DO UPDATE SET "TokenHash"=EXCLUDED."TokenHash",
                "SignedPdfPath"=EXCLUDED."SignedPdfPath", "CreatedById"=EXCLUDED."CreatedById", "CreatedAt"=now()
            """, connection, transaction);
        create.Parameters.AddWithValue("id", id);
        create.Parameters.AddWithValue("hash", Hash(secret));
        create.Parameters.AddWithValue("path", path);
        create.Parameters.AddWithValue("actor", actor);
        await create.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return $"api/public/documents/{id}?key={secret}";
    }

    public async Task RevokeAsync(Guid id, CancellationToken token)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var document = new NpgsqlCommand("SELECT 1 FROM \"Documents\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction);
        document.Parameters.AddWithValue("id", id);
        await document.ExecuteScalarAsync(token);
        await using var command = new NpgsqlCommand("DELETE FROM \"PublicDocumentLinks\" WHERE \"DocumentId\"=@id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<(string Path, string FileName)?> FindAsync(Guid id, string? key, CancellationToken token)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) return null;
        await using var command = source.CreateCommand("""
            SELECT l."SignedPdfPath", d."FileName" FROM "PublicDocumentLinks" l
            JOIN "Documents" d ON d."Id"=l."DocumentId"
            WHERE l."DocumentId"=@id AND l."TokenHash"=@hash
              AND NOT d."IsDeleted" AND d."Status"='Published'
              AND d."SignedPdfPath"=l."SignedPdfPath" AND EXISTS (
                SELECT 1 FROM "Signatures" s WHERE s."DocId"=d."Id"
                AND s."SignatureType"='LegalSeal' AND s."SignedFilePath"=l."SignedPdfPath")
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("hash", Hash(key));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var name = reader.IsDBNull(1) ? "cong-van" : Path.GetFileNameWithoutExtension(reader.GetString(1));
        return (reader.GetString(0), name + ".pdf");
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
