namespace Kacey90.MyFintechApp.BuildingBlocks.Application.Outbox;
public interface IOutbox
{
    void Add(OutboxMessage message);

    Task Save();
}
