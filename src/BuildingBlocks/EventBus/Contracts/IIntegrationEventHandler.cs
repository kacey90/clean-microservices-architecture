using Kacey90.MyFintechApp.BuildingBlocks.EventBus.Events;

namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.Contracts;
public interface IIntegrationEventHandler<in TIntegrationEvent> : IIntegrationEventHandler
    where TIntegrationEvent : IntegrationEvent
{
    Task Handle(TIntegrationEvent @event);
}

public interface IIntegrationEventHandler
{
}
