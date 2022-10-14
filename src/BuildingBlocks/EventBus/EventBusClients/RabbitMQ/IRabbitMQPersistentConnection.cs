using RabbitMQ.Client;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.EventBusClients.RabbitMQ;
public interface IRabbitMQPersistentConnection : IDisposable
{
    bool IsConnected { get; }

    bool TryConnect();

    IModel CreateModel();
}
