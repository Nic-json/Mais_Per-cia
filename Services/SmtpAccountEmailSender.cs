using System.Net;
using System.Net.Mail;

namespace Sistemas_para_pericias.Services;

public sealed class SmtpAccountEmailSender(IConfiguration configuration) : IAccountEmailSender
{
    private string? Host => configuration["Email:SmtpHost"];
    private int Port => int.TryParse(configuration["Email:SmtpPort"], out var port) ? port : 587;
    private string? User => configuration["Email:SmtpUser"];
    private string? Password => configuration["Email:SmtpPassword"];
    private string? FromAddress => configuration["Email:FromAddress"];
    private string FromName => configuration["Email:FromName"] ?? "Perícia em Dia";
    public string? SupportAddress => configuration["Email:SupportAddress"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(FromAddress) && !string.IsNullOrWhiteSpace(SupportAddress);

    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("O envio de e-mail ainda não está configurado.");
        using var message = new MailMessage
        {
            From = new MailAddress(FromAddress!, FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(recipient);
        using var client = new SmtpClient(Host!, Port)
        {
            EnableSsl = !string.Equals(configuration["Email:UseSsl"], "false", StringComparison.OrdinalIgnoreCase),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        if (!string.IsNullOrWhiteSpace(User))
            client.Credentials = new NetworkCredential(User, Password ?? "");
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
