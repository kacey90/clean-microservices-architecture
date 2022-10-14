using System.Data;

namespace Kacey90.MyFintechApp.BuildingBlocks.Application.Data;
public interface ISqlConnectionFactory
{
    IDbConnection GetOpenConnection();

    IDbConnection CreateNewConnection();

    string GetConnectionString();
}
