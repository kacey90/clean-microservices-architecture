namespace Kacey90.MyFintechApp.BuildingBlocks.Application.Emails;
public interface IEmailSender
{
    void SendEmail(EmailMessage message);
}
