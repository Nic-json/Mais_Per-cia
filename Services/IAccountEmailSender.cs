namespace Sistemas_para_pericias.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    string? SupportAddress { get; }
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
