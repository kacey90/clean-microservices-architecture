using Kacey90.MyFintechApp.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kacey90.MyFintechApp.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public class DomainEventsAccessor : IDomainEventsAccessor
{
    private readonly DbContext _fintechAppContext;

    public DomainEventsAccessor(DbContext fintechAppContext)
    {
        _fintechAppContext = fintechAppContext;
    }

    public void ClearAllDomainEvents()
    {
        var domainEntities = _fintechAppContext.ChangeTracker
                .Entries<Entity>()
                .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any()).ToList();

        domainEntities
            .ForEach(entity => entity.Entity.ClearDomainEvents());
    }

    public IReadOnlyCollection<IDomainEvent> GetAllDomainEvents()
    {
        var domainEntities = _fintechAppContext.ChangeTracker
                .Entries<Entity>()
                .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any()).ToList();

        return domainEntities
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();
    }
}
