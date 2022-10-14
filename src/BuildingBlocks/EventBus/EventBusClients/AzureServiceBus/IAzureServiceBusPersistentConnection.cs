using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.EventBusClients.AzureServiceBus;
public interface IAzureServiceBusPersistentConnection : IAsyncDisposable
{
    ServiceBusClient TopicClient { get; }
    ServiceBusAdministrationClient AdministrationClient { get; }
}
