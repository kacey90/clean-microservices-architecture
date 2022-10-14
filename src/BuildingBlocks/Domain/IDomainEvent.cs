using MediatR;

namespace Kacey90.MyFintechApp.BuildingBlocks.Domain;

public interface IDomainEvent : INotification
{
    Guid Id { get; }

    DateTime OccurredOn { get; }
}