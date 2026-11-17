using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otp.Application.Abstractions;
using Otp.Application.Handlers;
using Otp.Domain.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Otp.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "otp.events";
    public string DeadLetterExchange { get; set; } = "otp.events.dlx";
    public int MaxDeliveryAttempts { get; set; } = 3;
}

/// <summary>Shares one long-lived connection (auto-recovering) across publisher and consumers.</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    public RabbitMqOptions Options => options.Value;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.Host, Port = o.Port, UserName = o.User, Password = o.Password,
                VirtualHost = o.VirtualHost, AutomaticRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(ct);
            await using var ch = await _connection.CreateChannelAsync(cancellationToken: ct);
            await ch.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
            await ch.ExchangeDeclareAsync(o.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}

public class RabbitMqEventBus(RabbitMqConnection conn) : IEventBus
{
    public async Task PublishAsync<T>(T message, CancellationToken ct) where T : class, IIntegrationEvent
    {
        var connection = await conn.GetAsync(ct);
        // Publisher confirms: PublishAsync only returns once the broker has accepted the message.
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
            MessageId = Guid.NewGuid().ToString(),
            Type = typeof(T).Name,
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await channel.BasicPublishAsync(conn.Options.Exchange, T.RoutingKey, mandatory: false, props, body, ct);
    }
}

public interface ISubscription
{
    string Queue { get; }
    string RoutingKey { get; }
    Task DispatchAsync(IServiceProvider sp, ReadOnlyMemory<byte> body, CancellationToken ct);
}

public class Subscription<TEvent, THandler> : ISubscription
    where TEvent : class, IIntegrationEvent
    where THandler : IEventHandler<TEvent>
{
    public string Queue { get; } = $"{(typeof(THandler).Name)}.{TEvent.RoutingKey}";
    public string RoutingKey => TEvent.RoutingKey;

    public async Task DispatchAsync(IServiceProvider sp, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<TEvent>(body.Span) ?? throw new InvalidOperationException("Empty message");
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventHandler<TEvent>>().HandleAsync(evt, ct);
    }
}

/// <summary>Declares topology and runs each subscription. Failures retry in-process, then dead-letter.</summary>
public class RabbitMqConsumerService(
    RabbitMqConnection conn,
    IEnumerable<ISubscription> subscriptions,
    IServiceProvider services,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Broker may still be starting (e.g. docker compose): retry connecting.
        IConnection connection;
        while (true)
        {
            try { connection = await conn.GetAsync(stoppingToken); break; }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ not reachable, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        var o = conn.Options;
        foreach (var sub in subscriptions)
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            await channel.BasicQosAsync(0, 10, false, stoppingToken);
            var dlq = sub.Queue + ".dlq";
            await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(dlq, o.DeadLetterExchange, dlq, cancellationToken: stoppingToken);
            await channel.QueueDeclareAsync(sub.Queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = o.DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = dlq,
                }, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(sub.Queue, o.Exchange, sub.RoutingKey, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await sub.DispatchAsync(services, ea.Body, stoppingToken);
                        await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                        return;
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Handler for {Queue} failed (attempt {Attempt})", sub.Queue, attempt);
                        if (attempt >= o.MaxDeliveryAttempts)
                        {
                            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false, stoppingToken); // -> DLQ
                            return;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), stoppingToken);
                    }
                }
            };
            await channel.BasicConsumeAsync(sub.Queue, autoAck: false, consumer, stoppingToken);
            logger.LogInformation("Consuming {Queue} <- {Key}", sub.Queue, sub.RoutingKey);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
