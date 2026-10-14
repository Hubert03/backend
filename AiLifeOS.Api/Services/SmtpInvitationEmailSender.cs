using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace AiLifeOS.Api.Services;

public sealed class SmtpInvitationEmailSender(
    IOptions<InvitationEmailOptions> options) : IInvitationEmailSender
{
    private readonly InvitationEmailOptions settings = options.Value;

    public async Task SendInvitationAsync(
        string email,
        string displayName,
        string invitationUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(settings.FromAddress) ||
            string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            throw new InvalidOperationException(
                "Invitation email is not configured. Configure Email:SmtpHost, Email:FromAddress, and Email:PublicBaseUrl.");
        }

        using var message = new MailMessage(settings.FromAddress, email)
        {
            Subject = "You are invited to AI LifeOS",
            IsBodyHtml = true,
            Body = $"""
                <p>Hello {HtmlEncoder.Default.Encode(displayName)},</p>
                <p>You have been invited to join an AI LifeOS team. Use the link below to create your account:</p>
                <p><a href="{HtmlEncoder.Default.Encode(invitationUrl)}">Accept your invitation</a></p>
                <p>This invitation expires in 48 hours and can only be used once.</p>
                """,
        };

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(settings.SmtpUser)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.SmtpUser, settings.SmtpPassword),
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
