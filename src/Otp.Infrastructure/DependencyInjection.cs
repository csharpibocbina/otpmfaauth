using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Otp.Application;
using Otp.Application.Abstractions;
using Otp.Application.Handlers;
using Otp.Domain.Events;
using Otp.Infrastructure.Messaging;
using Otp.Infrastructure.Persistence;
using Otp.Infrastructure.Security;
using Otp.Infrastructure.Vonage;

namespace Otp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration config)
    {
        s.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt"))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters").ValidateOnStart();
        s.Configure<VonageOptions>(config.GetSection("Vonage"));
        s.Configure<RabbitMqOptions>(config.GetSection("RabbitMq"));
        s.Configure<MfaOptions>(config.GetSection("Mfa"));
        s.Configure<LockoutOptions>(config.GetSection("Lockout"));

        s.AddSingleton(TimeProvider.System);
        s.AddDbContext<OtpDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("Supabase"))
            .UseSnakeCaseNamingConvention());
        s.AddScoped<IUserRepository, UserRepository>();
        s.AddScoped<IVerificationRepository, VerificationRepository>();

        s.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        s.AddSingleton<ITokenService, JwtTokenService>();
        s.AddHttpClient<IVonageVerifyClient, VonageVerifyClient>(c => c.Timeout = TimeSpan.FromSeconds(15));

        s.AddSingleton<RabbitMqConnection>();
        s.AddSingleton<IEventBus, RabbitMqEventBus>();
        s.AddSingleton<ISubscription, Subscription<OtpRequested, OtpRequestedHandler>>();
        s.AddSingleton<ISubscription, Subscription<OtpSent, NotificationHandler>>();
        s.AddSingleton<ISubscription, Subscription<OtpDeliveryFailed, NotificationHandler>>();
        s.AddSingleton<ISubscription, Subscription<MfaVerified, NotificationHandler>>();
        s.AddSingleton<ISubscription, Subscription<MfaFailed, NotificationHandler>>();
        s.AddHostedService<RabbitMqConsumerService>();
        return s;
    }
}
