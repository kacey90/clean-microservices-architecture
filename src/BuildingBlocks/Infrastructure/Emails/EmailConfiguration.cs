namespace Kacey90.MyFintechApp.BuildingBlocks.Infrastructure.Emails;
public class EmailConfiguration
{
    public EmailConfiguration(string fromEmail)
    {
        FromEmail = fromEmail;
    }

    public string FromEmail { get; }
}
