using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.EventBusClients.AzureServiceBus;
public class AzureServiceBusPersistentConnection : IAzureServiceBusPersistentConnection
{
    private readonly string _azureServiceBusConnectionString;
    private ServiceBusClient _topicClient;
    private ServiceBusAdministrationClient _subscriptionClient;

    bool _disposed;
    public AzureServiceBusPersistentConnection(string serviceBusConnectionString)
    {
        _azureServiceBusConnectionString = serviceBusConnectionString;
        _subscriptionClient = new ServiceBusAdministrationClient(_azureServiceBusConnectionString);
        _topicClient = new ServiceBusClient(_azureServiceBusConnectionString);
    }

    public ServiceBusClient TopicClient 
    {
        get
        {
            if (_topicClient.IsClosed)
            {
                _topicClient = new ServiceBusClient(_azureServiceBusConnectionString);
            }
            return _topicClient;
        }
    }

    public ServiceBusAdministrationClient AdministrationClient => _subscriptionClient;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        await _topicClient.DisposeAsync();
    }
}
