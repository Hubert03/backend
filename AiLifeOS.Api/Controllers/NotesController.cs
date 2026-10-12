using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notes")]
public sealed class NotesController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoteResponse>>> GetNotes(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var notes = await dbContext.Notes
            .AsNoTracking()
            .Where(note => note.UserId == user.Id)
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAtUtc ?? note.CreatedAtUtc)
            .Select(note => new NoteResponse(
                note.NoteId,
                note.Title,
                note.Content,
                note.Color,
                note.IsPinned,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(notes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> GetNote(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var note = await dbContext.Notes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.NoteId == id && item.UserId == user.Id, cancellationToken);
        return note is null ? NotFound() : Ok(ToResponse(note));
    }

    [HttpPost]
    public async Task<ActionResult<NoteResponse>> CreateNote(
        SaveNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }
        if (!IsValidColor(request.Color))
        {
            return BadRequest(new { message = "Color must be a six-digit hexadecimal color such as #F3F4F6." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var noteId = Guid.NewGuid();
        var title = request.Title.Trim();
        var color = request.Color.Trim().ToUpperInvariant();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.Note_Create @NoteId={noteId}, @UserId={user.Id}, @Title={title}, @Content={request.Content}, @Color={color}, @IsPinned={request.IsPinned}",
            cancellationToken);
        var note = await dbContext.Notes.AsNoTracking()
            .SingleAsync(item => item.NoteId == noteId && item.UserId == user.Id, cancellationToken);

        return CreatedAtAction(nameof(GetNote), new { id = note.NoteId }, ToResponse(note));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> UpdateNote(
        Guid id,
        SaveNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }
        if (!IsValidColor(request.Color))
        {
            return BadRequest(new { message = "Color must be a six-digit hexadecimal color such as #F3F4F6." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var note = await dbContext.Notes
            .SingleOrDefaultAsync(item => item.NoteId == id && item.UserId == user.Id, cancellationToken);
        if (note is null)
        {
            return NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        note.Title = request.Title.Trim();
        note.Content = request.Content;
        note.Color = request.Color.Trim().ToUpperInvariant();
        note.IsPinned = request.IsPinned;
        note.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(user.Id, note, "updated", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(note));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var note = await dbContext.Notes
            .SingleOrDefaultAsync(item => item.NoteId == id && item.UserId == user.Id, cancellationToken);
        if (note is null)
        {
            return NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Notes.Remove(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(user.Id, note, "deleted", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<bool> HasValidCsrfTokenAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool IsValidColor(string color) => HexColor.IsMatch(color.Trim());

    private Task<int> AddActivityAsync(
        Guid userId,
        NoteItem note,
        string action,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ActivityLog_Insert @UserId={userId}, @EntityType=N'note', @EntityId={note.NoteId}, @Action={action}, @Title={note.Title}",
            cancellationToken);

    private static NoteResponse ToResponse(NoteItem note) =>
        new(note.NoteId, note.Title, note.Content, note.Color, note.IsPinned, note.CreatedAtUtc, note.UpdatedAtUtc);
}

public sealed record SaveNoteRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, StringLength(50000)] string Content,
    [Required, StringLength(7), RegularExpression("^#[0-9A-Fa-f]{6}$")] string Color,
    bool IsPinned);

public sealed record NoteResponse(
    Guid NoteId,
    string Title,
    string Content,
    string Color,
    bool IsPinned,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
