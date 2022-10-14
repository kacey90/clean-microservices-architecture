using Autofac;
using Azure.Messaging.ServiceBus;
using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Contracts;
using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Events;
using Serilog;
using System.Text.Json;
using System.Text;
using Azure.Messaging.ServiceBus.Administration;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.EventBusClients.AzureServiceBus;
public class EventBusAzureServiceBus : IEventBus, IAsyncDisposable
{
    private readonly IAzureServiceBusPersistentConnection _azureServiceBusPersistentConnection;
    private readonly ILogger _logger;
    private readonly IEventBusSubscriptionsManager _subsManager;
    private readonly ILifetimeScope _autofac;
    private readonly string _topicName = "myfintechapp_event_bus";
    private readonly string _subscriptionName;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusProcessor _processor;
    private readonly string AUTOFAC_SCOPE_NAME = "myfintechapp_event_bus";
    private const string INTEGRATION_EVENT_SUFFIX = "IntegrationEvent";

    public EventBusAzureServiceBus(
        IAzureServiceBusPersistentConnection azureServiceBusPersistentConnection,
        ILogger logger,
        IEventBusSubscriptionsManager subsManager,
        ILifetimeScope autofac,
        string subscriptionName)
    {
        _azureServiceBusPersistentConnection = azureServiceBusPersistentConnection;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
        _autofac = autofac;
        _subscriptionName = subscriptionName;
        _sender = _azureServiceBusPersistentConnection.TopicClient.CreateSender(_topicName);
        ServiceBusProcessorOptions options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 10, AutoCompleteMessages = false };
        _processor = _azureServiceBusPersistentConnection.TopicClient.CreateProcessor(_topicName, _subscriptionName, options);
        RemoveDefaultRule();
        RegisterSubscriptionClientMessageHandlerAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _subsManager.Clear();
        await _processor.CloseAsync();
    }

    public void Publish(IntegrationEvent @event)
    {
        var eventName = @event.GetType().Name.Replace(INTEGRATION_EVENT_SUFFIX, "");
        var jsonMessage = JsonSerializer.Serialize(@event, @event.GetType());
        var body = Encoding.UTF8.GetBytes(jsonMessage);

        var message = new ServiceBusMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new BinaryData(body),
            Subject = eventName,
        };

        _sender.SendMessageAsync(message)
            .GetAwaiter()
            .GetResult();
    }

    public void Subscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = typeof(T).Name.Replace(INTEGRATION_EVENT_SUFFIX, "");

        var containsKey = _subsManager.HasSubscriptionsForEvent<T>();
        if (!containsKey)
        {
            try
            {
                _azureServiceBusPersistentConnection.AdministrationClient.CreateRuleAsync(_topicName, _subscriptionName, new CreateRuleOptions
                {
                    Filter = new CorrelationRuleFilter() { Subject = eventName },
                    Name = eventName
                }).GetAwaiter().GetResult();
            }
            catch (ServiceBusException)
            {
                _logger.Warning("The messaging entity {eventName} already exists.", eventName);
            }
        }

        _logger.Information("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).Name);

        _subsManager.AddSubscription<T, TH>();
    }

    public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
    {
        _logger.Information("Subscribing to dynamic event {EventName} with {EventHandler}", eventName, typeof(TH).Name);

        _subsManager.AddDynamicSubscription<TH>(eventName);
    }

    public void Unsubscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = typeof(T).Name.Replace(INTEGRATION_EVENT_SUFFIX, "");

        try
        {
            _azureServiceBusPersistentConnection
                .AdministrationClient
                .DeleteRuleAsync(_topicName, _subscriptionName, eventName)
                .GetAwaiter()
                .GetResult();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.Warning("The messaging entity {eventName} Could not be found.", eventName);
        }

        _logger.Information("Unsubscribing from event {EventName}", eventName);

        _subsManager.RemoveSubscription<T, TH>();
    }

    public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
    {
        _logger.Information("Unsubscribing from dynamic event {EventName}", eventName);

        _subsManager.RemoveDynamicSubscription<TH>(eventName);
    }

    private async Task RegisterSubscriptionClientMessageHandlerAsync()
    {
        _processor.ProcessMessageAsync +=
            async (args) =>
            {
                var eventName = $"{args.Message.Subject}{INTEGRATION_EVENT_SUFFIX}";
                string messageData = args.Message.Body.ToString();

                // Complete the message so that it is not received again.
                if (await ProcessEvent(eventName, messageData))
                {
                    await args.CompleteMessageAsync(args.Message);
                }
            };

        _processor.ProcessErrorAsync += ErrorHandler;
        await _processor.StartProcessingAsync();
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        var ex = args.Exception;
        var context = args.ErrorSource;

        _logger.Error(ex, "ERROR handling message: {ExceptionMessage} - Context: {@ExceptionContext}", ex.Message, context);

        return Task.CompletedTask;
    }

    private async Task<bool> ProcessEvent(string eventName, string message)
    {
        var processed = false;
        if (_subsManager.HasSubscriptionsForEvent(eventName))
        {
            var scope = _autofac.BeginLifetimeScope(AUTOFAC_SCOPE_NAME);
            var subscriptions = _subsManager.GetHandlersForEvent(eventName);
            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDynamic)
                {
                    if (scope.ResolveOptional(subscription.HandlerType) is not IDynamicIntegrationEventHandler handler) continue;

                    using dynamic eventData = JsonDocument.Parse(message);
                    await handler.Handle(eventData);
                }
                else
                {
                    var handler = scope.ResolveOptional(subscription.HandlerType);
                    if (handler == null) continue;
                    var eventType = _subsManager.GetEventTypeByName(eventName);
                    var integrationEvent = JsonSerializer.Deserialize(message, eventType);
                    var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                    await (Task)concreteType.GetMethod("Handle").Invoke(handler, new[] { integrationEvent });
                }
            }
        }
        processed = true;
        return processed;
    }

    private void RemoveDefaultRule()
    {
        try
        {
            _azureServiceBusPersistentConnection
                .AdministrationClient
                .DeleteRuleAsync(_topicName, _subscriptionName, RuleProperties.DefaultRuleName)
                .GetAwaiter()
                .GetResult();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.Warning("The messaging entity {DefaultRuleName} Could not be found.", RuleProperties.DefaultRuleName);
        }
    }

}
