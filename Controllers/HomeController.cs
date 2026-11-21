using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Ganss.Xss;
using System.IO.Compression;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;
using Sistemas_para_pericias.Models.Requests;
using Sistemas_para_pericias.Models.Responses;
using Sistemas_para_pericias.Services;
using Sistemas_para_pericias.Utilities;

namespace Sistemas_para_pericias.Controllers;

public partial class HomeController(PericiasContext db, IConfiguration configuration, IWebHostEnvironment environment, HtmlSanitizer sanitizer) : Controller
{
    private sealed record ManifestoBackup(string Aplicacao, int VersaoFormato, int VersaoEsquema, DateTime CriadoEmUtc, int QuantidadeDocumentos);
    private sealed record MarcoAgenda(string Numero, DateTime? DataEnvio, DateTime? DataAceite, DateTime? PrazoLaudo);
    private sealed record EntregaAgenda(DateTime? EntregueEm, string NumeroLaudo, string Numero);
    private readonly string diretorioDocumentos = configuration["Documentos:Diretorio"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "uploads");

    private void RegistrarHistorico(int periciaId, string tipo, string descricao) => db.HistoricoPericias.Add(new HistoricoPericia
    {
        PericiaId = periciaId, Tipo = tipo, Descricao = descricao, Responsavel = User.Identity?.Name,
        CriadoEm = DateTime.UtcNow
    });

    private IQueryable<Pericia> ConsultaVisaoGeral() => db.Pericias.AsNoTracking().Where(p =>
        !((p.Status == StatusPericia.Cancelada || p.Status == StatusPericia.Concluida || p.Status == StatusPericia.Recusada) && p.ValorRecebido >= p.ValorHonorarios));

    private IQueryable<Pericia> ConsultaArquivadas() => db.Pericias.AsNoTracking().Where(p =>
        (p.Status == StatusPericia.Cancelada || p.Status == StatusPericia.Concluida || p.Status == StatusPericia.Recusada) && p.ValorRecebido >= p.ValorHonorarios);

    private static int NormalizarTamanhoPagina(int valor, int padrao) => valor is 10 or 20 or 25 or 50 or 100 ? valor : padrao;

    private void ValidarPericiados(IReadOnlyCollection<PericiadoFormInputDto> periciados)
    {
        periciados ??= [];
        if (periciados.Count > 50)
            ModelState.AddModelError(nameof(PericiaFormInputDto.Periciados), "Adicione no máximo 50 periciados por perícia.");
        if (periciados.Select(p => p.Nome?.Trim() ?? "").Where(nome => nome.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != periciados.Count(p => !string.IsNullOrWhiteSpace(p.Nome)))
            ModelState.AddModelError(nameof(PericiaFormInputDto.Periciados), "Não repita o mesmo nome de periciado nesta perícia.");
    }

    private static void AtualizarCamposEditaveis(Pericia destino, Pericia origem)
    {
        destino.NomePericiado = "";
        destino.Numero = origem.Numero;
        destino.Comarca = origem.Comarca;
        destino.Vara = origem.Vara;
        destino.DataAceite = origem.DataAceite;
        destino.PrazoLaudo = origem.PrazoLaudo;
        destino.ValorHonorarios = origem.ValorHonorarios;
        destino.DataHonorariosFixados = origem.DataHonorariosFixados;
        destino.ValorHonorariosSolicitados = origem.ValorHonorariosSolicitados;
        destino.DataHonorariosSolicitados = origem.DataHonorariosSolicitados;
        destino.ValorHonorariosLiberados = origem.ValorHonorariosLiberados;
        destino.DataHonorariosLiberados = origem.DataHonorariosLiberados;
        destino.Status = origem.Status;
        destino.Observacoes = origem.Observacoes;
    }

    public IActionResult Banco() => RedirectToAction(nameof(Configuracoes));



    public async Task<IActionResult> BaixarBanco()
    {
        var nomeArquivo = $"pericias-{BrasiliaTime.Now:yyyy-MM-dd-HHmm}.db";
        var diretorioTemporario = Path.Combine(Path.GetTempPath(), $"pericias-db-{Guid.NewGuid():N}");
        var caminhoBanco = Path.Combine(diretorioTemporario, nomeArquivo);
        Directory.CreateDirectory(diretorioTemporario);
        try
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                await using var destino = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = caminhoBanco,
                    Pooling = false
                }.ToString());
                await destino.OpenAsync();
                ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destino);
                await destino.CloseAsync();
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }

            await LimparCopiaParaContaAsync(caminhoBanco, UsuarioAtualId());

            HttpContext.Response.OnCompleted(() =>
            {
                try
                {
                    if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return Task.CompletedTask;
            });
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(caminhoBanco, "application/vnd.sqlite3", nomeArquivo);
        }
        catch
        {
            if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
            throw;
        }
    }


    [HttpGet("/privacidade"), HttpGet("/Home/Privacy"), AllowAnonymous]
    public IActionResult Privacy()
    {
        ViewBag.OperadorServico = configuration["Service:OperatorName"];
        ViewBag.EmailPrivacidade = configuration["Email:SupportAddress"];
        return View();
    }

    [HttpGet("/termos"), HttpGet("/Home/Terms"), AllowAnonymous]
    public IActionResult Terms()
    {
        ViewBag.OperadorServico = configuration["Service:OperatorName"];
        ViewBag.IdentificacaoLegal = configuration["Service:LegalName"];
        ViewBag.RegistroFornecedor = configuration["Service:TaxId"];
        ViewBag.EnderecoFornecedor = configuration["Service:BusinessAddress"];
        ViewBag.EmailSuporte = configuration["Email:SupportAddress"];
        ViewBag.PrecoMensal = decimal.TryParse(configuration["Billing:MonthlyPrice"], NumberStyles.Number, CultureInfo.InvariantCulture, out var preco)
            ? preco
            : 70m;
        return View();
    }

    [AllowAnonymous]
    public IActionResult Error() => View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });

    [AllowAnonymous]
    public async Task<IActionResult> Index(string? busca, StatusPericia? status, string? pagamento, string? comarca, DateTime? prazoDe, DateTime? prazoAte, int pagina = 1, int porPagina = 20)
    {
        if (User.Identity?.IsAuthenticated != true) return View("Landing");

        porPagina = NormalizarTamanhoPagina(porPagina, 20);
        var visaoGeral = ConsultaVisaoGeral();
        IQueryable<Pericia> query = visaoGeral.Include(p => p.Periciados).Include(p => p.Laudos);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(p => p.Numero.Contains(busca) || (p.Comarca != null && p.Comarca.Contains(busca)) || (p.Vara != null && p.Vara.Contains(busca)) || p.Periciados.Any(pessoa => pessoa.Nome.Contains(busca)) || p.Laudos.Any(l => l.NumeroLaudo.Contains(busca)));
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(comarca)) query = query.Where(p => p.Comarca == comarca);
        if (prazoDe.HasValue) query = query.Where(p => p.PrazoLaudo != null && p.PrazoLaudo >= prazoDe.Value);
        if (prazoAte.HasValue) query = query.Where(p => p.PrazoLaudo != null && p.PrazoLaudo <= prazoAte.Value);
        if (pagamento == "pendente") query = query.Where(p => p.ValorRecebido < p.ValorHonorarios);
        if (pagamento == "quitado") query = query.Where(p => p.ValorRecebido >= p.ValorHonorarios);
        var registrosFiltrados = await query.CountAsync();
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(registrosFiltrados / (double)porPagina));
        pagina = Math.Clamp(pagina, 1, totalPaginas);
        ViewData["Title"] = "Visão geral";
        ViewBag.Busca = busca;
        ViewBag.StatusFiltro = status;
        ViewBag.Pagamento = pagamento; ViewBag.ComarcaFiltro = comarca; ViewBag.PrazoDe = prazoDe?.ToString("yyyy-MM-dd"); ViewBag.PrazoAte = prazoAte?.ToString("yyyy-MM-dd");
        ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas; ViewBag.PorPagina = porPagina;
        ViewBag.Comarcas = await db.Pericias.AsNoTracking().Where(p => p.Comarca != null && p.Comarca != "").Select(p => p.Comarca!).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.LaudosPendentes = await db.LaudosPericia.CountAsync(l => l.Status != StatusLaudo.Entregue);
        var config = await db.ConfiguracoesPerito.AsNoTracking().FirstOrDefaultAsync();
        ViewBag.Total = await visaoGeral.CountAsync();
        ViewBag.Ativas = await visaoGeral.CountAsync(p => p.Status == StatusPericia.EmAndamento || p.Status == StatusPericia.LaudoEnviado);
        ViewBag.AguardandoPagamento = await visaoGeral.CountAsync(p => p.ValorRecebido < p.ValorHonorarios);
        ViewBag.HonorariosAReceber = await visaoGeral.SumAsync(p => (decimal?)Math.Max(0m, p.ValorHonorarios - p.ValorRecebido)) ?? 0m;
        ViewBag.PrazosVencidos = await visaoGeral.CountAsync(p => p.PrazoLaudo < BrasiliaTime.Today && p.Status != StatusPericia.Concluida && p.Status != StatusPericia.Cancelada && p.Status != StatusPericia.Recusada);
        ViewBag.PrazosProximos = config?.AvisarPrazosNoPainel == true
            ? await visaoGeral.CountAsync(p => p.PrazoLaudo >= BrasiliaTime.Today && p.PrazoLaudo <= BrasiliaTime.Today.AddDays(config.AntecedenciaPrazoDias))
            : 0;
        ViewBag.AntecedenciaPrazoDias = config?.AntecedenciaPrazoDias ?? 7;
        return View(await query.OrderBy(p => p.PrazoLaudo == null).ThenBy(p => p.PrazoLaudo).ThenBy(p => p.ValorRecebido >= p.ValorHonorarios).ThenBy(p => p.DataHonorariosSolicitados ?? p.DataEnvio).ThenBy(p => p.DataEnvio).Skip((pagina - 1) * porPagina).Take(porPagina).ToListAsync());
    }

    public async Task<IActionResult> Configuracoes()
    {
        ViewBag.EmailConta = User.FindFirstValue(ClaimTypes.Email);
        ViewBag.EmailSuporte = configuration["Email:SupportAddress"];
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var conta = await db.ContasUsuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
            ViewBag.EmailConfirmado = conta?.EmailConfirmado ?? false;
        }
        await PrepararCatalogoForenseAsync();
        return View(await db.ConfiguracoesPerito.AsNoTracking().FirstOrDefaultAsync() ?? new ConfiguracaoPerito());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarConfiguracoes(ConfiguracoesInputDto input)
    {
        var configuracao = input.ToViewModel();
        if (configuracao.AntecedenciaPrazoDias is < 0 or > 60) ModelState.AddModelError(nameof(configuracao.AntecedenciaPrazoDias), "Use um período entre 0 e 60 dias.");
        if (!ModelState.IsValid)
        {
            ViewBag.EmailConta = User.FindFirstValue(ClaimTypes.Email);
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var conta = await db.ContasUsuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
                ViewBag.EmailConfirmado = conta?.EmailConfirmado ?? false;
            }
            await PrepararCatalogoForenseAsync();
            return View("Configuracoes", configuracao);
        }
        var atual = await db.ConfiguracoesPerito.FirstOrDefaultAsync();
        if (atual is null) { configuracao.AtualizadoEm = DateTime.UtcNow; db.ConfiguracoesPerito.Add(configuracao); }
        else
        {
            atual.NomeProfissional = configuracao.NomeProfissional?.Trim(); atual.Especialidade = configuracao.Especialidade?.Trim();
            atual.RegistroProfissional = configuracao.RegistroProfissional?.Trim(); atual.EmailContato = configuracao.EmailContato?.Trim();
            atual.Telefone = configuracao.Telefone?.Trim(); atual.AntecedenciaPrazoDias = configuracao.AntecedenciaPrazoDias;
            atual.AvisarPrazosNoPainel = configuracao.AvisarPrazosNoPainel; atual.AtualizadoEm = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        TempData["MensagemConfiguracao"] = "Preferências salvas.";
        return RedirectToAction(nameof(Configuracoes));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AdicionarUnidadeForense(UnidadeForenseInputDto input)
    {
        input.Codigo = (input.Codigo ?? "").Trim();
        input.Comarca = (input.Comarca ?? "").Trim();
        input.Foro = input.Foro?.Trim();
        if (!ModelState.IsValid)
        {
            TempData["ErroCatalogoForense"] = "Confira o código de quatro dígitos e o nome da comarca.";
            return RedirectToAction(nameof(Configuracoes));
        }

        var existente = await db.UnidadesForenses.FirstOrDefaultAsync(u => u.Tribunal == "TJRS" && u.Uf == "RS" && u.Codigo == input.Codigo);
        if (existente is null)
        {
            db.UnidadesForenses.Add(new UnidadeForense { Codigo = input.Codigo, Comarca = input.Comarca, Foro = input.Foro });
        }
        else
        {
            existente.Comarca = input.Comarca;
            existente.Foro = input.Foro;
        }
        await db.SaveChangesAsync();
        TempData["MensagemCatalogoForense"] = "Código do TJRS salvo no seu catálogo.";
        return RedirectToAction(nameof(Configuracoes));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverUnidadeForense(int id)
    {
        var unidade = await db.UnidadesForenses.FirstOrDefaultAsync(u => u.Id == id);
        if (unidade is not null)
        {
            db.UnidadesForenses.Remove(unidade);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Configuracoes));
    }

    private async Task PrepararCatalogoForenseAsync()
    {
        var personalizados = await db.UnidadesForenses.AsNoTracking().Where(u => u.Tribunal == "TJRS" && u.Uf == "RS").ToListAsync();
        ViewBag.UnidadesForenses = personalizados.OrderBy(u => u.Codigo).ToList();
        var catalogoUnico = CatalogoTjrs.ListaBase.GroupBy(u => u.Codigo).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var catalogo = CatalogoTjrs.ListaBase.ToList();
        foreach (var unidade in personalizados)
        {
            catalogo.RemoveAll(u => u.Codigo == unidade.Codigo);
            catalogo.Add(new ForoTjrs(unidade.Codigo, unidade.Comarca, unidade.Foro ?? $"Foro de {unidade.Comarca}"));
        }
        ViewBag.CatalogoForos = catalogo.OrderBy(u => u.Comarca).ThenBy(u => u.Codigo).ToList();
        ViewBag.CatalogoForosCodigosPersonalizados = personalizados.Select(u => u.Codigo).ToHashSet();
        var codigos = catalogoUnico.Where(kv => CatalogoTjrs.IdentificaForoDeComarca(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Comarca);
        foreach (var unidade in personalizados.Where(u => CatalogoTjrs.IdentificaForoDeComarca(u.Codigo)))
            codigos[unidade.Codigo] = unidade.Comarca;
        ViewBag.CatalogoForosJson = System.Text.Json.JsonSerializer.Serialize(codigos);
    }

    private async Task AplicarComarcaDoCodigoTjrsAsync(PericiaFormInputDto input)
    {
        if (!string.IsNullOrWhiteSpace(input.Comarca)) return;
        var codigo = CatalogoTjrs.ExtrairCodigo(input.Numero);
        if (codigo is null || !CatalogoTjrs.IdentificaForoDeComarca(codigo)) return;
        var comarca = await db.UnidadesForenses.AsNoTracking().Where(u => u.Tribunal == "TJRS" && u.Uf == "RS" && u.Codigo == codigo)
            .Select(u => u.Comarca).FirstOrDefaultAsync();
        input.Comarca = comarca ?? (CatalogoTjrs.ListaBase.Count(u => u.Codigo == codigo) == 1
            ? CatalogoTjrs.ListaBase.Single(u => u.Codigo == codigo).Comarca
            : null);
    }






















    private int UsuarioAtualId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static async Task LimparCopiaParaContaAsync(string caminhoBanco, int usuarioId)
    {
        if (usuarioId <= 0) throw new InvalidOperationException("Não foi possível identificar a conta dona do backup.");
        await using var conexao = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = caminhoBanco,
            Pooling = false
        }.ToString());
        await conexao.OpenAsync();
        await using var transacao = (SqliteTransaction)await conexao.BeginTransactionAsync();
        foreach (var tabela in new[] { "AssinaturasContas", "VersoesLaudo", "PagamentosHonorarios", "HistoricoPericias", "EventosAgenda", "DocumentosPericia", "LaudosPericia", "Periciados", "Pericias", "ConfiguracoesPerito", "UnidadesForenses", "ModelosLaudo" })
        {
            await using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText = $"DELETE FROM \"{tabela}\" WHERE \"UsuarioId\" <> $usuarioId;";
            comando.Parameters.AddWithValue("$usuarioId", usuarioId);
            await comando.ExecuteNonQueryAsync();
        }
        await using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = "DELETE FROM \"EventosAsaas\";";
            await comando.ExecuteNonQueryAsync();
        }
        await using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = "DELETE FROM \"TokensConta\"; DELETE FROM \"ContasUsuarios\" WHERE \"Id\" <> $usuarioId; UPDATE \"ContasUsuarios\" SET \"SenhaHash\" = 'REMOVIDA_DA_EXPORTACAO' WHERE \"Id\" = $usuarioId;";
            comando.Parameters.AddWithValue("$usuarioId", usuarioId);
            await comando.ExecuteNonQueryAsync();
        }
        await transacao.CommitAsync();
    }



    private static string ContentTypeDocumento(string nomeArmazenado) => Path.GetExtension(nomeArmazenado).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };

    private static EditorLaudoViewModel CriarModeloLaudo(LaudoPericia laudo) => new()
    {
        PericiaId = laudo.PericiaId,
        LaudoId = laudo.Id,
        NumeroLaudo = laudo.NumeroLaudo,
        TipoLaudo = laudo.Tipo,
        Status = laudo.Status,
        Prazo = laudo.Prazo,
        PericiadoId = laudo.PericiadoId,
        Periciados = laudo.Pericia.Periciados.OrderBy(p => p.Nome).ToList(),
        NomePericiado = laudo.Periciado?.Nome ?? "Não associado",
        Numero = laudo.Pericia.Numero,
        ConteudoHtml = laudo.ConteudoHtml,
        AtualizadoEm = laudo.AtualizadoEm,
        Versoes = laudo.Versoes.OrderByDescending(v => v.SalvoEm).ToList()
    };








}

