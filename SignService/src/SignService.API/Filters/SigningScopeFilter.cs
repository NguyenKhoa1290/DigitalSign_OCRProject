using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SignService.Core.DTOs;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

namespace SignService.API.Filters;

public sealed class SigningScopeFilter(AppDbContext db, IWorkflowDirectory directory) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true) { await next(); return; }
        var request = context.ActionArguments.Values.OfType<SignRequestDto>().FirstOrDefault();
        object? routeId = null;
        foreach (var key in new[] { "docId", "id" })
            if (context.RouteData.Values.TryGetValue(key, out routeId)) break;
        var id = request?.DocId ?? (Guid.TryParse(routeId?.ToString(), out var parsed) ? parsed : Guid.Empty);
        if (id == Guid.Empty) { await next(); return; } // Certificate endpoints have no document scope.
        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub")!);
        var actor = await directory.GetUserAsync(actorId);
        if (!actor.IsActive) throw new IncomingAccessException();
        var document = await db.DocumentFiles.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id)
            ?? throw new DocumentNotFoundException(id);
        var admin = principal.IsInRole("Admin") && actor.Roles.Contains("Admin");
        var board = principal.IsInRole("BoardOfDirectors") && actor.Roles.Contains("BoardOfDirectors");
        var sameUnit = actor.DepartmentId.HasValue && actor.DepartmentId == document.DepartmentId;
        var canRead = admin || board || document.IsSchoolWide || sameUnit || document.CreatedById == actorId
            || document.AssignedToId == actorId || actor.DepartmentId.HasValue && actor.DepartmentId == document.ProcessingDepartmentId
            || await db.Signatures.AnyAsync(s => s.DocId == id && s.SignerId == actorId);
        if (!canRead) throw new IncomingAccessException();
        if (request != null)
        {
            // Ignore signer identity supplied by the browser, including a forged certificate owner.
            request.SignerId = actorId; request.SignerName = actor.FullName;
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
            if (!admin && action == "PersonalSign" && (!sameUnit || !actor.Roles.Contains("Manager")))
                throw new IncomingAccessException();
            var legacyUnproven = document.Status == "DeptSigned" && document.SignedPdfPath == null
                && !await db.Signatures.AnyAsync(s => s.DocId == id);
            if (action == "PersonalSign" && document.Status != "PendingDeptReview" && !legacyUnproven)
                throw new IncomingConflictException("Công văn chưa ở bước chờ Trưởng phòng ký nháy.");
            if (!admin && action == "LegalSeal" && !board) throw new IncomingAccessException();
            if (action == "LegalSeal" && document.Status != "PendingDirectorSign")
                throw new IncomingConflictException("Công văn chưa ở bước chờ Ban Giám hiệu ký.");
        }
        await next();
    }
}
