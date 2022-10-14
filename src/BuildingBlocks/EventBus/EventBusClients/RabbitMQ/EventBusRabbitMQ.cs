using Autofac;
using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Contracts;
using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Events;
using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Extensions;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Serilog;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.EventBusClients.RabbitMQ;
public class EventBusRabbitMQ : IEventBus, IDisposable
{
    private readonly IRabbitMQPersistentConnection _persistentConnection;
    private readonly ILogger _logger;
    private readonly IEventBusSubscriptionsManager _subsManager;
    private readonly ILifetimeScope _autofac;
    private readonly int _retryCount;

    private IModel _consumerChannel;
    private string _queueName;

    const string BROKER_NAME = "myfintechapp_event_bus";
    const string AUTOFAC_SCOPE_NAME = "myfintechapp_event_bus";

    public EventBusRabbitMQ(
        IRabbitMQPersistentConnection persistentConnection,
        ILogger logger,
        IEventBusSubscriptionsManager subsManager,
        ILifetimeScope autofac,
        string queueName = null,
        int retryCount = 5)
    {
        _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
        _autofac = autofac;
        _queueName = queueName;
        _retryCount = retryCount;

        _consumerChannel = CreateConsumerChannel();
        _autofac = autofac;
        _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
    }

    private void SubsManager_OnEventRemoved(object sender, string eventName)
    {
        if (!_persistentConnection.IsConnected)
        {
            _persistentConnection.TryConnect();
        }

        using var channel = _persistentConnection.CreateModel();
        channel.QueueUnbind(queue: _queueName,
            exchange: BROKER_NAME,
            routingKey: eventName);

        if (_subsManager.IsEmpty)
        {
            _queueName = string.Empty;
            _consumerChannel.Close();
        }
    }
    
    private IModel CreateConsumerChannel()
    {
        if (!_persistentConnection.IsConnected)
        {
            _persistentConnection.TryConnect();
        }

        _logger.Verbose("Creating RabbitMQ consumer channel");

        var channel = _persistentConnection.CreateModel();

        channel.ExchangeDeclare(exchange: BROKER_NAME, type: ExchangeType.Direct);

        channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

        channel.CallbackException += (sender, ea) =>
        {
            _consumerChannel.Dispose();
            _consumerChannel = CreateConsumerChannel();
            StartBasicConsume();
        };

        return channel;
    }

    private void StartBasicConsume()
    {
        _logger.Verbose("Starting RabbitMQ basic consume");

        if (_consumerChannel != null)
        {
            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
            consumer.Received += Consumer_Received;

            _consumerChannel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
        }
        else
        {
            _logger.Error("StartBasicConsume can't call on _consumerChannel == null");
        }
    }

    private async Task Consumer_Received(object sender, BasicDeliverEventArgs eventArgs)
    {
        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            if (message.ToLowerInvariant().Contains("throw-fake-exception"))
            {
                throw new InvalidOperationException($"Fake exception requested: \"{message}\"");
            }

            await ProcessEvent(eventName, message);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "----- ERROR Processing message \"{Message}\"", message);
        }

        // Even on exception we take the message off the queue.
        // in a REAL WORLD app this should be handled with a Dead Letter Exchange (DLX). 
        // For more information see: https://www.rabbitmq.com/dlx.html
        _consumerChannel.BasicAck(eventArgs.DeliveryTag, multiple: false);
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        _logger.Verbose("Processing RabbitMQ event: {EventName}", eventName);

        if (_subsManager.HasSubscriptionsForEvent(eventName))
        {
            await using var scope = _autofac.BeginLifetimeScope(AUTOFAC_SCOPE_NAME);
            var subscriptions = _subsManager.GetHandlersForEvent(eventName);

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDynamic)
                {
                    if (scope.ResolveOptional(subscription.HandlerType) is not IDynamicIntegrationEventHandler handler)
                    {
                        continue;
                    }
                    using dynamic eventData = JsonDocument.Parse(message);
                    await Task.Yield();
                    await handler.Handle(eventData);
                }
                else
                {
                    var handler = scope.ResolveOptional(subscription.HandlerType);
                    if (handler == null) continue;
                    var eventType = _subsManager.GetEventTypeByName(eventName);
                    var integrationEvent = JsonSerializer.Deserialize(message, eventType, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                    var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

                    await Task.Yield();
                    await (Task)concreteType.GetMethod("Handle").Invoke(handler, new object[] { integrationEvent });
                }
            }
        }
        else
        {
            _logger.Warning("No subscription for RabbitMQ event: {EventName}", eventName);
        }
    }

    public void Dispose()
    {
        if (_consumerChannel != null)
        {
            _consumerChannel.Dispose();
        }

        _subsManager.Clear();
    }

    public void Publish(IntegrationEvent @event)
    {
        if (!_persistentConnection.IsConnected)
        {
            _persistentConnection.TryConnect();
        }

        var policy = Policy.Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.Warning(ex, "Could not publish event: {EventId} after {Timeout}s ({ExceptionMessage})", @event.Id, $"{time.TotalSeconds:n1}", ex.Message);
            });

        var eventName = @event.GetType().Name;
        _logger.Verbose("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id, eventName);
        using var channel = _persistentConnection.CreateModel();
        

        channel.ExchangeDeclare(exchange: BROKER_NAME, type: ExchangeType.Direct);

        var message = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(message);

        policy.Execute(() =>
        {
            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // persistent

            _logger.Verbose("Publishing event to RabbitMQ: {EventId}", @event.Id);

            channel.BasicPublish(exchange: BROKER_NAME,
                routingKey: eventName,
                mandatory: true,
                basicProperties: properties,
                body: body);
        });
    }

    public void Subscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = _subsManager.GetEventKey<T>();
        DoInternalSubscription(eventName);

        _logger.Information("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

        _subsManager.AddSubscription<T, TH>();
        StartBasicConsume();
    }

    public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
    {
        _logger.Information("Subscribing to dynamic event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

        DoInternalSubscription(eventName);
        _subsManager.AddDynamicSubscription<TH>(eventName);
        StartBasicConsume();
    }

    private void DoInternalSubscription(string eventName)
    {
        var containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
        if (!containsKey)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            _consumerChannel.QueueBind(queue: _queueName,
                                exchange: BROKER_NAME,
                                routingKey: eventName);
        }
    }


    public void Unsubscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = _subsManager.GetEventKey<T>();

        _logger.Information("Unsubscribing from event {EventName}", eventName);

        _subsManager.RemoveSubscription<T, TH>();
    }

    public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
    {
        _subsManager.RemoveDynamicSubscription<TH>(eventName);
    }
}
