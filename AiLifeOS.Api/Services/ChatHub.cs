using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AiLifeOS.Api.Services;

[Authorize]
public sealed class ChatHub : Hub
{
}
