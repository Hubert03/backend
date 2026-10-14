namespace AiLifeOS.Api.Services;

public sealed class InvitationEmailOptions
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
}
