using ApiGateway.Monitoring;

namespace ApiGateway.Tests;

public class RequestJournalTests
{
    [Theory]
    [InlineData("/api/auth/private-password")]
    [InlineData("/api/users/person@example.com")]
    [InlineData("/api/documents/secret-token")]
    [InlineData("/api/signatures/certificates/private-key")]
    public void UnrecognizedPathSegmentsAreNeverStored(string path)
    {
        var result=RequestJournal.Describe(path);
        Assert.NotNull(result);
        Assert.Equal("/api/[unmapped]",result.Value.Path);
        Assert.Null(result.Value.Resource);
    }
    [Theory]
    [InlineData("/api/admin/activity")]
    [InlineData("/api/notifications")]
    public void MonitoringReadsDoNotGenerateRecursiveJournalEntries(string path)=>Assert.Null(RequestJournal.Describe(path));
    [Fact]
    public void KnownResourceKeepsUuidAndServiceForCorrelation()
    {
        var id=Guid.NewGuid();var path=$"/api/documents/{id}/assign";
        var result=RequestJournal.Describe(path)!.Value;
        Assert.Equal("DocumentService",result.Service);Assert.Equal(id,result.Resource);Assert.Equal(path,result.Path);
    }
    [Theory]
    [InlineData("POST","/api/users/id/roles/id","AssignRole")]
    [InlineData("DELETE","/api/users/id/roles/id","RemoveRole")]
    [InlineData("POST","/api/signatures/personal-sign","PersonalSign")]
    [InlineData("POST","/api/auth/login","Login")]
    [InlineData("POST","/api/ocr/process","ProcessOCR")]
    [InlineData("POST","/api/documents/id/upload","UploadDocument")]
    [InlineData("POST","/api/documents/id/submit","Submit")]
    [InlineData("POST","/api/documents/id/dept-sign","DeptSign")]
    [InlineData("POST","/api/documents/id/submit-director","SubmitDirector")]
    [InlineData("POST","/api/documents/id/director-sign","DirectorSign")]
    [InlineData("POST","/api/documents/id/reject","Reject")]
    [InlineData("POST","/api/documents/id/publish","Publish")]
    [InlineData("POST","/api/documents/id/assign","Assign")]
    [InlineData("PATCH","/api/documents/id/ocr","UpdateOCR")]
    public void AuditIdentifiesBusinessOperation(string method,string path,string expected)
        =>Assert.Equal(expected,RequestJournal.Action(method,path));
}
