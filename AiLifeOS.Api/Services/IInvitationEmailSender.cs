namespace AiLifeOS.Api.Services;

public interface IInvitationEmailSender
{
    Task SendInvitationAsync(
        string email,
        string displayName,
        string invitationUrl,
        CancellationToken cancellationToken);
}
