namespace Kacey90.MyFintechApp.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsDispatcher
{
    Task DispatchEventsAsync();
}