namespace Kacey90.MyFintechApp.BuildingBlocks.EventBus.Contracts;
public interface IDynamicIntegrationEventHandler
{
    Task Handle(dynamic eventData);
}
