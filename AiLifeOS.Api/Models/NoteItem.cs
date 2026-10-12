namespace AiLifeOS.Api.Models;

public sealed class NoteItem
{
    public Guid NoteId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Color { get; set; } = "#f3f4f6";
    public bool IsPinned { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
