using Kacey90.MyFintechApp.BuildingBlocks.Domain;

namespace Kacey90.MyFintechApp.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsAccessor
{
    IReadOnlyCollection<IDomainEvent> GetAllDomainEvents();

    void ClearAllDomainEvents();
}