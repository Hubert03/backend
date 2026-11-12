namespace AiLifeOS.Api.Services;

public sealed class VapidOptions
{
    public string Subject { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        Uri.TryCreate(Subject, UriKind.Absolute, out var subject) &&
        subject.Scheme is "mailto" or "https";

    public string ConfigurationError =>
        string.IsNullOrWhiteSpace(PublicKey) ? "Vapid:PublicKey is missing." :
        string.IsNullOrWhiteSpace(PrivateKey) ? "Vapid:PrivateKey is missing." :
        !Uri.TryCreate(Subject, UriKind.Absolute, out var subject) ||
        subject.Scheme is not ("mailto" or "https")
            ? "Vapid:Subject must be an absolute mailto: or HTTPS URI."
            : "VAPID configuration is valid.";
}