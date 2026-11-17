using Microsoft.Extensions.DependencyInjection;
using Otp.Application.Handlers;
using Otp.Application.Services;
using Otp.Domain.Events;

namespace Otp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s)
    {
        s.AddScoped<IAuthService, AuthService>();
        s.AddScoped<IMfaService, MfaService>();
        s.AddScoped<IEventHandler<OtpRequested>, OtpRequestedHandler>();
        s.AddScoped<NotificationHandler>();
        s.AddScoped<IEventHandler<OtpSent>>(sp => sp.GetRequiredService<NotificationHandler>());
        s.AddScoped<IEventHandler<OtpDeliveryFailed>>(sp => sp.GetRequiredService<NotificationHandler>());
        s.AddScoped<IEventHandler<MfaVerified>>(sp => sp.GetRequiredService<NotificationHandler>());
        s.AddScoped<IEventHandler<MfaFailed>>(sp => sp.GetRequiredService<NotificationHandler>());
        return s;
    }
}
