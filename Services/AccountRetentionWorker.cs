using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Services;

public sealed class AccountRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AccountRetentionWorker> logger,
    IHostEnvironment environment) : BackgroundService
{
    private static readonly string[] TabelasDaConta =
    [
        "VersoesLaudo", "PagamentosHonorarios", "HistoricoPericias", "EventosAgenda",
        "DocumentosPericia", "LaudosPericia", "Periciados", "Pericias",
        "ConfiguracoesPerito", "UnidadesForenses", "ModelosLaudo", "TokensConta",
        "AssinaturasContas", "ContasUsuarios"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try { await ProcessarAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Falha na rotina de retenção de contas."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessarAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PericiasContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IAccountEmailSender>();
        var agora = DateTime.UtcNow;
        var contas = await db.ContasUsuarios
            .Where(conta => conta.Ativo && conta.Id != 1)
            .ToListAsync(cancellationToken);

        foreach (var conta in contas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inativoDesde = conta.UltimoAcessoEm;
            var dataAviso = inativoDesde.AddMonths(11);
            var dataExclusao = inativoDesde.AddYears(1);

            if (agora < dataAviso)
                continue;

            if (conta.AvisoInatividadeEnviadoEm is null)
            {
                if (!emailSender.IsConfigured)
                {
                    logger.LogWarning("Conta {UsuarioId} atingiu a janela de aviso, mas o SMTP não está configurado; dados preservados.", conta.Id);
                    continue;
                }

                try
                {
                    var nome = WebUtility.HtmlEncode(conta.Nome);
                    var contato = WebUtility.HtmlEncode(emailSender.SupportAddress ?? "");
                    var dataLimite = dataExclusao.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
                    var body = $"<p>Olá, {nome}.</p><p>Não identificamos acesso à sua conta do Perícia em Dia há 11 meses. Para manter seus dados, entre na sua conta antes de <strong>{dataLimite}</strong>.</p><p>Se a conta permanecer sem acesso por 12 meses, os dados poderão ser excluídos automaticamente, desde que não haja assinatura ativa ou período pago vigente. O aviso é enviado com pelo menos 30 dias de antecedência.</p><p>Se precisar de ajuda, escreva para <a href=\"mailto:{contato}\">{contato}</a>.</p>";
                    await emailSender.SendAsync(conta.Email, "Aviso: sua conta está sem acesso há 11 meses", body, cancellationToken);
                    conta.AvisoInatividadeEnviadoEm = agora;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Não foi possível enviar o aviso de inatividade para a conta {UsuarioId}; os dados foram preservados.", conta.Id);
                }
                continue;
            }

            if (agora < dataExclusao || agora < conta.AvisoInatividadeEnviadoEm.Value.AddDays(30))
                continue;

            var assinatura = await db.Assinaturas.AsNoTracking().SingleOrDefaultAsync(item => item.UsuarioId == conta.Id, cancellationToken);
            if (assinatura is not null &&
                (string.Equals(assinatura.Status, "Ativa", StringComparison.OrdinalIgnoreCase) ||
                 (assinatura.AcessoAte.HasValue && assinatura.AcessoAte.Value >= agora)))
            {
                logger.LogInformation("Conta {UsuarioId} preservada pela assinatura ativa ou período pago vigente.", conta.Id);
                continue;
            }

            await ExcluirContaAsync(db, conta.Id, cancellationToken);
        }

        LimparBackupsLocaisAntigos();
    }

    private async Task ExcluirContaAsync(PericiasContext db, int usuarioId, CancellationToken cancellationToken)
    {
        // A consulta e cada comando de exclusão são explicitamente limitados ao UsuarioId.
        var arquivos = await db.DocumentosPericia.IgnoreQueryFilters().AsNoTracking()
            .Where(documento => documento.UsuarioId == usuarioId)
            .Select(documento => documento.NomeArmazenado)
            .ToListAsync(cancellationToken);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var tabela in TabelasDaConta)
            {
                var comando = tabela switch
                {
                    "VersoesLaudo" => "DELETE FROM \"VersoesLaudo\" WHERE \"UsuarioId\" = {0};",
                    "PagamentosHonorarios" => "DELETE FROM \"PagamentosHonorarios\" WHERE \"UsuarioId\" = {0};",
                    "HistoricoPericias" => "DELETE FROM \"HistoricoPericias\" WHERE \"UsuarioId\" = {0};",
                    "EventosAgenda" => "DELETE FROM \"EventosAgenda\" WHERE \"UsuarioId\" = {0};",
                    "DocumentosPericia" => "DELETE FROM \"DocumentosPericia\" WHERE \"UsuarioId\" = {0};",
                    "LaudosPericia" => "DELETE FROM \"LaudosPericia\" WHERE \"UsuarioId\" = {0};",
                    "Periciados" => "DELETE FROM \"Periciados\" WHERE \"UsuarioId\" = {0};",
                    "Pericias" => "DELETE FROM \"Pericias\" WHERE \"UsuarioId\" = {0};",
                    "ConfiguracoesPerito" => "DELETE FROM \"ConfiguracoesPerito\" WHERE \"UsuarioId\" = {0};",
                    "UnidadesForenses" => "DELETE FROM \"UnidadesForenses\" WHERE \"UsuarioId\" = {0};",
                    "ModelosLaudo" => "DELETE FROM \"ModelosLaudo\" WHERE \"UsuarioId\" = {0};",
                    "TokensConta" => "DELETE FROM \"TokensConta\" WHERE \"UsuarioId\" = {0};",
                    "AssinaturasContas" => "DELETE FROM \"AssinaturasContas\" WHERE \"UsuarioId\" = {0};",
                    "ContasUsuarios" => "DELETE FROM \"ContasUsuarios\" WHERE \"Id\" = {0};",
                    _ => throw new InvalidOperationException("Tabela de retenção desconhecida.")
                };
                await db.Database.ExecuteSqlRawAsync(comando, [usuarioId], cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        var diretorio = Path.GetFullPath(configuration["Documentos:Diretorio"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "uploads"));
        foreach (var nome in arquivos)
        {
            try
            {
                var nomeSeguro = Path.GetFileName(nome);
                if (string.IsNullOrWhiteSpace(nomeSeguro)) continue;
                var caminho = Path.GetFullPath(Path.Combine(diretorio, nomeSeguro));
                if (Path.GetDirectoryName(caminho) == diretorio && File.Exists(caminho))
                    File.Delete(caminho);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Não foi possível remover um arquivo legado da conta {UsuarioId}.", usuarioId);
            }
        }

        logger.LogInformation("Conta {UsuarioId} e os registros associados foram excluídos por inatividade.", usuarioId);
    }

    private void LimparBackupsLocaisAntigos()
    {
        try
        {
            var connection = configuration.GetConnectionString("Pericias") ?? "Data Source=App_Data/pericias.db";
            var arquivoBanco = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection).DataSource;
            if (string.IsNullOrWhiteSpace(arquivoBanco) || arquivoBanco == ":memory:") return;
            var diretorio = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arquivoBanco))!, "backups-restauracao");
            if (!Directory.Exists(diretorio)) return;
            foreach (var arquivo in Directory.EnumerateFiles(diretorio, "antes-restauracao-*.db"))
                if (File.GetLastWriteTimeUtc(arquivo) < DateTime.UtcNow.AddDays(-30)) File.Delete(arquivo);
        }
        catch (Exception exception) { logger.LogError(exception, "Falha ao remover backups locais de restauração com mais de 30 dias."); }
    }
}
