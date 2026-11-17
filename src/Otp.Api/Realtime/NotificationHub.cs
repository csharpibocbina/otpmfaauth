using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Otp.Application.Abstractions;

namespace Otp.Api.Realtime;

public interface INotificationClient
{
    Task OtpSent(object payload);
    Task OtpDeliveryFailed(object payload);
    Task MfaVerified(object payload);
    Task MfaFailed(object payload);
}

/// <summary>Accepts both MFA-step and access tokens; each user joins a private group.</summary>
[Authorize]
public class NotificationHub : Hub<INotificationClient>
{
    public static string Group(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
        await base.OnConnectedAsync();
    }
}

public class SignalRNotificationPublisher(IHubContext<NotificationHub, INotificationClient> hub) : INotificationPublisher
{
    public Task NotifyAsync(Guid userId, string eventName, object payload, CancellationToken ct)
    {
        var group = hub.Clients.Group(NotificationHub.Group(userId));
        return eventName switch
        {
            "OtpSent" => group.OtpSent(payload),
            "OtpDeliveryFailed" => group.OtpDeliveryFailed(payload),
            "MfaVerified" => group.MfaVerified(payload),
            "MfaFailed" => group.MfaFailed(payload),
            _ => Task.CompletedTask,
        };
    }
}
