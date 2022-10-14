using Dapper;
using Kacey90.MyFintechApp.BuildingBlocks.Application.Data;
using Kacey90.MyFintechApp.BuildingBlocks.Application.Emails;
using Serilog;

namespace Kacey90.MyFintechApp.BuildingBlocks.Infrastructure.Emails;
public class EmailSender : IEmailSender
{
    private readonly ILogger _logger;

    private readonly EmailConfiguration _configuration;

    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public EmailSender(
        ILogger logger,
        EmailConfiguration configuration,
        ISqlConnectionFactory sqlConnectionFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public void SendEmail(EmailMessage message)
    {
        var sqlConnection = _sqlConnectionFactory.GetOpenConnection();

        sqlConnection.ExecuteScalar(
            "INSERT INTO [app].[Emails] ([Id], [From], [To], [Subject], [Content], [Date]) " +
            "VALUES (@Id, @From, @To, @Subject, @Content, @Date) ",
            new
            {
                Id = Guid.NewGuid(),
                From = _configuration.FromEmail,
                message.To,
                message.Subject,
                message.Content,
                Date = DateTime.UtcNow
            });

        _logger.Information(
            "Email sent. From: {From}, To: {To}, Subject: {Subject}, Content: {Content}.",
            _configuration.FromEmail,
            message.To,
            message.Subject,
            message.Content);
    }
}
