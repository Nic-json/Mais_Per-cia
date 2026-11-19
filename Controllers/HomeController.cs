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
using Sistemas_para_pericias.Services;
using Sistemas_para_pericias.Utilities;

namespace Sistemas_para_pericias.Controllers;

public class HomeController(PericiasContext db, IConfiguration configuration, IWebHostEnvironment environment, HtmlSanitizer sanitizer) : Controller
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
                await using var destino = new SqliteConnection($"Data Source={caminhoBanco}");
                await destino.OpenAsync();
                ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destino);
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


    [AllowAnonymous]
    public IActionResult Privacy() => View();

    [AllowAnonymous]
    public IActionResult Error() => View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });

    public async Task<IActionResult> Index(string? busca, StatusPericia? status, string? pagamento, string? comarca, DateTime? prazoDe, DateTime? prazoAte, int pagina = 1, int porPagina = 20)
    {
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

    public async Task<IActionResult> Processos(string? busca, string? comarca, StatusPericia? status, string? financeiro,
        decimal? honorariosMin, decimal? honorariosMax, int? laudosMin, int? laudosMax, DateTime? enviadoDe, DateTime? enviadoAte, int pagina = 1, bool arquivadas = false, int porPagina = 25)
    {
        porPagina = NormalizarTamanhoPagina(porPagina, 25);
        var query = db.Pericias.AsNoTracking().Include(p => p.Periciados).Include(p => p.Laudos).AsQueryable();
        query = arquivadas
            ? query.Where(p => (p.Status == StatusPericia.Cancelada || p.Status == StatusPericia.Concluida || p.Status == StatusPericia.Recusada) && p.ValorRecebido >= p.ValorHonorarios)
            : query.Where(p => !((p.Status == StatusPericia.Cancelada || p.Status == StatusPericia.Concluida || p.Status == StatusPericia.Recusada) && p.ValorRecebido >= p.ValorHonorarios));
        if (!string.IsNullOrWhiteSpace(busca)) query = query.Where(p => p.Numero.Contains(busca) || p.Periciados.Any(x => x.Nome.Contains(busca)) || p.Laudos.Any(l => l.NumeroLaudo.Contains(busca)));
        if (!string.IsNullOrWhiteSpace(comarca)) query = query.Where(p => p.Comarca == comarca);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (financeiro == "pendente") query = query.Where(p => p.ValorRecebido < p.ValorHonorarios);
        if (financeiro == "quitado") query = query.Where(p => p.ValorRecebido >= p.ValorHonorarios);
        if (honorariosMin.HasValue) query = query.Where(p => p.ValorHonorarios >= honorariosMin.Value);
        if (honorariosMax.HasValue) query = query.Where(p => p.ValorHonorarios <= honorariosMax.Value);
        if (laudosMin.HasValue) query = query.Where(p => p.Laudos.Count >= laudosMin.Value);
        if (laudosMax.HasValue) query = query.Where(p => p.Laudos.Count <= laudosMax.Value);
        if (enviadoDe.HasValue) query = query.Where(p => p.DataEnvio >= enviadoDe.Value);
        if (enviadoAte.HasValue) query = query.Where(p => p.DataEnvio <= enviadoAte.Value);
        var total = await query.CountAsync();
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)porPagina));
        pagina = Math.Clamp(pagina, 1, totalPaginas);
        ViewBag.Busca = busca; ViewBag.ComarcaFiltro = comarca; ViewBag.StatusFiltro = status; ViewBag.Financeiro = financeiro;
        ViewBag.HonorariosMin = honorariosMin; ViewBag.HonorariosMax = honorariosMax; ViewBag.LaudosMin = laudosMin; ViewBag.LaudosMax = laudosMax;
        ViewBag.EnviadoDe = enviadoDe?.ToString("yyyy-MM-dd"); ViewBag.EnviadoAte = enviadoAte?.ToString("yyyy-MM-dd");
        ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas; ViewBag.PorPagina = porPagina; ViewBag.Arquivadas = arquivadas;
        ViewBag.Comarcas = await db.Pericias.AsNoTracking().Where(p => p.Comarca != null && p.Comarca != "").Select(p => p.Comarca!).Distinct().OrderBy(c => c).ToListAsync();
        return View(await query.OrderByDescending(p => p.CriadaEm).Skip((pagina - 1) * porPagina).Take(porPagina).ToListAsync());
    }

    public async Task<IActionResult> Processo(int id)
    {
        var pericia = await db.Pericias.AsNoTracking().Include(p => p.Periciados).Include(p => p.Laudos).ThenInclude(l => l.Periciado)
            .Include(p => p.Documentos).Include(p => p.EventosAgenda).ThenInclude(e => e.Periciado).Include(p => p.Pagamentos).Include(p => p.Historico)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();
        ViewBag.Saldo = Math.Max(0m, pericia.ValorHonorarios - pericia.ValorRecebido);
        return View(pericia);
    }

    public async Task<IActionResult> Agenda(DateTime? data, string? modo)
    {
        modo = modo is "dia" or "semana" ? modo : "mes";
        var referencia = data ?? BrasiliaTime.Today;
        var inicio = modo == "dia" ? referencia.Date : modo == "semana"
            ? referencia.Date.AddDays(-(((int)referencia.DayOfWeek + 6) % 7))
            : new DateTime(referencia.Year, referencia.Month, 1);
        var fim = modo == "dia" ? inicio.AddDays(1) : modo == "semana" ? inicio.AddDays(7) : inicio.AddMonths(1);
        var anterior = modo == "dia" ? inicio.AddDays(-1) : modo == "semana" ? inicio.AddDays(-7) : inicio.AddMonths(-1);
        var proximo = modo == "dia" ? inicio.AddDays(1) : modo == "semana" ? inicio.AddDays(7) : inicio.AddMonths(1);
        ViewBag.Mes = inicio; ViewBag.InicioAgenda = inicio; ViewBag.FimAgenda = fim; ViewBag.Anterior = anterior; ViewBag.Proximo = proximo; ViewBag.Modo = modo;
        ViewBag.TituloPeriodo = modo == "dia" ? inicio.ToString("dd 'de' MMMM yyyy", new CultureInfo("pt-BR")) : modo == "semana" ? $"{inicio:dd/MM} – {fim.AddDays(-1):dd/MM/yyyy}" : inicio.ToString("MMMM yyyy", new CultureInfo("pt-BR"));
        ViewBag.Pericias = await ConsultaVisaoGeral().OrderBy(p => p.Numero).Select(p => new { p.Id, p.Numero }).ToListAsync();
        var marcos = await db.Pericias.AsNoTracking().Where(p => p.DataEnvio != null || p.DataAceite != null || p.PrazoLaudo != null)
            .Select(p => new MarcoAgenda(p.Numero, p.DataEnvio, p.DataAceite, p.PrazoLaudo)).ToListAsync();
        var entregas = await db.LaudosPericia.AsNoTracking().Where(l => l.EntregueEm != null)
            .Select(l => new EntregaAgenda(l.EntregueEm, l.NumeroLaudo, l.Pericia.Numero)).ToListAsync();
        ViewBag.Pessoas = await db.Periciados.AsNoTracking().Select(p => new { p.Id, p.PericiaId, p.Nome }).OrderBy(p => p.Nome).ToListAsync();
        var eventos = await db.EventosAgenda.AsNoTracking().Include(e => e.Pericia).Include(e => e.Periciado)
            .Where(e => !e.Cancelado && e.Inicio >= inicio && e.Inicio < fim).OrderBy(e => e.Inicio).ToListAsync();

        var entradas = eventos.Select(e => new AgendaCalendarEntryViewModel(
            e.Inicio.Date, e.Titulo, "compromisso", e.Inicio.ToString("HH:mm"), e.Pericia.Numero, e.Id)).ToList();
        foreach (var marco in marcos)
        {
            if (marco.DataEnvio is DateTime enviado && enviado >= inicio && enviado < fim)
                entradas.Add(new AgendaCalendarEntryViewModel(enviado.Date, "Perícia recebida", "recebida", NumeroPericia: marco.Numero));
            if (marco.DataAceite is DateTime aceite && aceite >= inicio && aceite < fim)
                entradas.Add(new AgendaCalendarEntryViewModel(aceite.Date, "Perícia aceita", "aceite", NumeroPericia: marco.Numero));
            if (marco.PrazoLaudo is DateTime prazo && prazo >= inicio && prazo < fim)
                entradas.Add(new AgendaCalendarEntryViewModel(prazo.Date, "Prazo de laudo", "prazo", NumeroPericia: marco.Numero));
        }
        foreach (var entrega in entregas)
        {
            if (entrega.EntregueEm is DateTime entregue)
            {
                var dataBrasilia = BrasiliaTime.ToBrasilia(entregue);
                if (dataBrasilia >= inicio && dataBrasilia < fim)
                    entradas.Add(new AgendaCalendarEntryViewModel(dataBrasilia.Date, $"Laudo {entrega.NumeroLaudo} entregue", "entrega", dataBrasilia.ToString("HH:mm"), entrega.Numero));
            }
        }

        var primeiroDia = modo == "mes"
            ? inicio.AddDays(-(((int)inicio.DayOfWeek + 6) % 7))
            : inicio;
        var quantidadeDias = modo == "mes"
            ? (int)Math.Ceiling((((int)inicio.DayOfWeek + 6) % 7 + (fim - inicio).Days) / 7d) * 7
            : (fim - inicio).Days;
        var entradasPorDia = entradas.GroupBy(e => e.Data.Date).ToDictionary(g => g.Key, g => (IReadOnlyList<AgendaCalendarEntryViewModel>)g.OrderBy(e => e.Hora ?? "").ThenBy(e => e.Categoria).ToList());
        ViewBag.DiasAgenda = Enumerable.Range(0, quantidadeDias).Select(offset =>
        {
            var dia = primeiroDia.AddDays(offset);
            return new AgendaCalendarDayViewModel(dia, dia >= inicio && dia < fim, dia.Date == BrasiliaTime.Today,
                entradasPorDia.GetValueOrDefault(dia.Date) ?? []);
        }).ToList();

        return View(eventos);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarEvento(int periciaId, int? periciadoId, string? titulo, DateTime inicio, DateTime? fim, string? local, string? modalidade, string? observacoes)
    {
        var pericia = await db.Pericias.Include(p => p.Periciados).FirstOrDefaultAsync(p => p.Id == periciaId);
        if (pericia is null) return NotFound();
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Trim().Length > 160 || inicio == default || (fim.HasValue && fim < inicio) || (periciadoId.HasValue && !pericia.Periciados.Any(p => p.Id == periciadoId)))
        {
            TempData["ErroAgenda"] = "Confira título, horário e periciado selecionado.";
            return RedirectToAction(nameof(Agenda));
        }
        db.EventosAgenda.Add(new EventoAgenda { PericiaId = periciaId, PericiadoId = periciadoId, Titulo = titulo.Trim(), Inicio = inicio, Fim = fim, Local = local?.Trim(), Modalidade = modalidade?.Trim(), Observacoes = observacoes?.Trim() });
        RegistrarHistorico(periciaId, "Agenda", "Novo compromisso cadastrado.");
        await db.SaveChangesAsync();
        TempData["MensagemAgenda"] = "Compromisso adicionado à agenda.";
        return RedirectToAction(nameof(Agenda), new { data = inicio, modo = "mes" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarEvento(int id)
    {
        var evento = await db.EventosAgenda.FirstOrDefaultAsync(e => e.Id == id);
        if (evento is null) return NotFound();
        evento.Cancelado = true;
        RegistrarHistorico(evento.PericiaId, "Agenda", "Compromisso cancelado.");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Agenda), new { data = evento.Inicio, modo = "mes" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarEvento(int id, string? titulo, DateTime inicio, DateTime? fim, string? local, string? modalidade, int? periciadoId, string? observacoes)
    {
        var evento = await db.EventosAgenda.Include(e => e.Pericia).FirstOrDefaultAsync(e => e.Id == id);
        if (evento is null) return NotFound();
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Trim().Length > 160 || inicio == default || (fim.HasValue && fim < inicio) ||
            (periciadoId.HasValue && !await db.Periciados.AnyAsync(p => p.Id == periciadoId && p.PericiaId == evento.PericiaId)))
        {
            TempData["ErroAgenda"] = "Confira título, horário e periciado selecionado.";
            return RedirectToAction(nameof(Agenda), new { data = evento.Inicio, modo = "mes" });
        }
        evento.Titulo = titulo.Trim(); evento.Inicio = inicio; evento.Fim = fim; evento.Local = local?.Trim(); evento.Modalidade = modalidade?.Trim(); evento.PericiadoId = periciadoId; evento.Observacoes = observacoes?.Trim();
        RegistrarHistorico(evento.PericiaId, "Agenda", "Compromisso atualizado ou reagendado.");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Agenda), new { data = inicio, modo = "mes" });
    }

    public async Task<IActionResult> Honorarios(string? busca, string? situacao)
    {
        var query = db.Pericias.AsNoTracking().Include(p => p.Periciados).Include(p => p.Pagamentos).AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca)) query = query.Where(p => p.Numero.Contains(busca) || p.Periciados.Any(x => x.Nome.Contains(busca)));
        if (situacao == "pendente") query = query.Where(p => p.ValorRecebido < p.ValorHonorarios);
        if (situacao == "quitado") query = query.Where(p => p.ValorRecebido >= p.ValorHonorarios);
        ViewBag.Busca = busca; ViewBag.Situacao = situacao;
        ViewBag.Fixado = await db.Pericias.SumAsync(p => (decimal?)p.ValorHonorarios) ?? 0m;
        ViewBag.Solicitado = await db.Pericias.SumAsync(p => (decimal?)p.ValorHonorariosSolicitados) ?? 0m;
        ViewBag.Liberado = await db.Pericias.SumAsync(p => (decimal?)p.ValorHonorariosLiberados) ?? 0m;
        ViewBag.Recebido = await db.Pericias.SumAsync(p => (decimal?)p.ValorRecebido) ?? 0m;
        ViewBag.Pendente = Math.Max(0m, (decimal)ViewBag.Fixado - (decimal)ViewBag.Recebido);
        return View(await query.OrderBy(p => p.ValorRecebido >= p.ValorHonorarios).ThenBy(p => p.PrazoLaudo).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarPagamento(int id, decimal valor, DateTime? dataRecebimento, string? observacoes)
    {
        var pericia = await db.Pericias.FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();
        if (valor <= 0 || valor > Math.Max(0m, pericia.ValorHonorarios - pericia.ValorRecebido))
        {
            TempData["ErroFinanceiro"] = "Informe um valor positivo dentro do saldo disponível.";
            return RedirectToAction(nameof(Processo), new { id, fragment = "honorarios" });
        }
        db.PagamentosHonorarios.Add(new PagamentoHonorario { PericiaId = id, Valor = valor, DataRecebimento = dataRecebimento, Observacoes = observacoes?.Trim() });
        RegistrarHistorico(id, "Honorários", "Pagamento recebido registrado.");
        pericia.ValorRecebido += valor;
        await db.SaveChangesAsync();
        TempData["MensagemFinanceira"] = "Pagamento registrado.";
        return RedirectToAction(nameof(Processo), new { id, fragment = "honorarios" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarDataSei(int id, DateTime? dataSei)
    {
        var pericia = await db.Pericias.FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();

        if (pericia.DataEnvio != dataSei)
        {
            pericia.DataEnvio = dataSei;
            RegistrarHistorico(id, "Processo", dataSei.HasValue
                ? $"Data do SEI registrada: {dataSei.Value:dd/MM/yyyy}."
                : "Data do SEI removida.");
            await db.SaveChangesAsync();
            TempData["MensagemFinanceira"] = dataSei.HasValue ? "Data do SEI registrada." : "Data do SEI removida.";
        }

        return RedirectToAction(nameof(Processo), new { id, fragment = "honorarios" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirPagamento(int id)
    {
        var pagamento = await db.PagamentosHonorarios.FirstOrDefaultAsync(p => p.Id == id);
        if (pagamento is null) return NotFound();
        var pericia = await db.Pericias.FirstOrDefaultAsync(p => p.Id == pagamento.PericiaId);
        if (pericia is not null)
            pericia.ValorRecebido = Math.Max(0m, pericia.ValorRecebido - pagamento.Valor);
        var periciaId = pagamento.PericiaId;
        db.PagamentosHonorarios.Remove(pagamento);
        RegistrarHistorico(periciaId, "Honorários", "Registro de pagamento removido.");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Processo), new { id = periciaId });
    }

    public async Task<IActionResult> Relatorios(DateTime? inicio, DateTime? fim, string? comarca, string? vara, string? tipo)
    {
        inicio ??= new DateTime(BrasiliaTime.Today.Year, 1, 1); fim ??= BrasiliaTime.Today;
        var query = db.Pericias.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(comarca)) query = query.Where(p => p.Comarca == comarca);
        if (!string.IsNullOrWhiteSpace(vara)) query = query.Where(p => p.Vara == vara);
        if (!string.IsNullOrWhiteSpace(tipo)) query = query.Where(p => p.TipoPericia == tipo);
        var periodo = (await query.ToListAsync()).Where(p => p.DataEnvio >= inicio && p.DataEnvio <= fim).ToList();
        ViewBag.Inicio = inicio.Value.ToString("yyyy-MM-dd"); ViewBag.Fim = fim.Value.ToString("yyyy-MM-dd"); ViewBag.ComarcaFiltro = comarca; ViewBag.VaraFiltro = vara; ViewBag.TipoFiltro = tipo;
        ViewBag.Comarcas = await db.Pericias.AsNoTracking().Where(p => p.Comarca != null && p.Comarca != "").Select(p => p.Comarca!).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.Varas = await db.Pericias.AsNoTracking().Where(p => p.Vara != null && p.Vara != "").Select(p => p.Vara!).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.Tipos = await db.Pericias.AsNoTracking().Where(p => p.TipoPericia != null && p.TipoPericia != "").Select(p => p.TipoPericia!).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.TotalProcessos = periodo.Count; ViewBag.HonorariosFixados = periodo.Sum(p => p.ValorHonorarios); ViewBag.HonorariosSolicitados = periodo.Sum(p => p.ValorHonorariosSolicitados); ViewBag.HonorariosLiberados = periodo.Sum(p => p.ValorHonorariosLiberados); ViewBag.Recebido = periodo.Sum(p => p.ValorRecebido); ViewBag.AReceber = periodo.Sum(p => Math.Max(0m, p.ValorHonorarios - p.ValorRecebido));
        var ids = periodo.Select(p => p.Id).ToList();
        var entregas = await db.LaudosPericia.AsNoTracking().Where(l => ids.Contains(l.PericiaId) && l.EntregueEm != null)
            .GroupBy(l => l.PericiaId).Select(g => new { Id = g.Key, Data = g.Min(l => l.EntregueEm) }).ToListAsync();
        var recebimentos = await db.PagamentosHonorarios.AsNoTracking().Where(p => ids.Contains(p.PericiaId) && p.DataRecebimento != null)
            .GroupBy(p => p.PericiaId).Select(g => new { Id = g.Key, Data = g.Min(p => p.DataRecebimento) }).ToListAsync();
        var duracoes = entregas.Join(recebimentos, e => e.Id, r => r.Id, (e, r) => (r.Data!.Value.Date - e.Data!.Value.Date).TotalDays).Where(d => d >= 0).ToList();
        ViewBag.MediaDiasPagamento = duracoes.Count == 0 ? (double?)null : duracoes.Average();
        return View(periodo.OrderBy(p => p.Comarca).ThenBy(p => p.Numero).ToList());
    }

    public async Task<IActionResult> ExportarRelatorio(DateTime? inicio, DateTime? fim, string? comarca, string? vara, string? tipo)
    {
        inicio ??= new DateTime(BrasiliaTime.Today.Year, 1, 1); fim ??= BrasiliaTime.Today;
        var query = db.Pericias.AsNoTracking().Include(p => p.Periciados).AsQueryable();
        if (!string.IsNullOrWhiteSpace(comarca)) query = query.Where(p => p.Comarca == comarca);
        if (!string.IsNullOrWhiteSpace(vara)) query = query.Where(p => p.Vara == vara);
        if (!string.IsNullOrWhiteSpace(tipo)) query = query.Where(p => p.TipoPericia == tipo);
        var rows = (await query.ToListAsync()).Where(p => p.DataEnvio >= inicio && p.DataEnvio <= fim).OrderBy(p => p.Numero);
        static string Q(string? v) => $"\"{(v ?? "").Replace("\"", "\"\"")}\"";
        var lines = new List<string> { "NumeroPericia;Periciados;Comarca;Vara;Status;HonorariosFixados;DataFixacao;HonorariosSolicitados;DataSolicitacao;HonorariosLiberados;DataLiberacao;Recebido;Saldo" };
        lines.AddRange(rows.Select(p => string.Join(';', new[] { Q(p.Numero), Q(string.Join(" | ", p.Periciados.Select(x => x.Nome))), Q(p.Comarca), Q(p.Vara), Q(p.Status.ToString()), Q(p.ValorHonorarios.ToString("0.00", CultureInfo.InvariantCulture)), Q(p.DataHonorariosFixados?.ToString("dd/MM/yyyy")), Q(p.ValorHonorariosSolicitados.ToString("0.00", CultureInfo.InvariantCulture)), Q(p.DataHonorariosSolicitados?.ToString("dd/MM/yyyy")), Q(p.ValorHonorariosLiberados.ToString("0.00", CultureInfo.InvariantCulture)), Q(p.DataHonorariosLiberados?.ToString("dd/MM/yyyy")), Q(p.ValorRecebido.ToString("0.00", CultureInfo.InvariantCulture)), Q(Math.Max(0m, p.ValorHonorarios - p.ValorRecebido).ToString("0.00", CultureInfo.InvariantCulture)) })));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray(), "text/csv; charset=utf-8", $"relatorio-pericial-{BrasiliaTime.Today:yyyy-MM-dd}.csv");
    }

    public IActionResult Arquivadas(string? busca) => RedirectToAction(nameof(Processos), new { busca, arquivadas = true });

    public async Task<IActionResult> Laudos(string? busca, StatusLaudo? status, DateTime? prazoAte)
    {
        IQueryable<Pericia> query = db.Pericias.AsNoTracking()
            .Include(pericia => pericia.Periciados)
            .Include(pericia => pericia.Laudos);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(pericia => pericia.Numero.Contains(busca) ||
                pericia.Periciados.Any(pessoa => pessoa.Nome.Contains(busca)));
        if (status.HasValue) query = query.Where(pericia => pericia.Laudos.Any(laudo => laudo.Status == status.Value));
        if (prazoAte.HasValue) query = query.Where(pericia => pericia.Laudos.Any(laudo => laudo.Prazo != null && laudo.Prazo <= prazoAte.Value));
        ViewBag.Busca = busca;
        ViewBag.StatusFiltro = status;
        ViewBag.PrazoAte = prazoAte?.ToString("yyyy-MM-dd");
        ViewBag.Total = await query.CountAsync();
        return View(await query.OrderByDescending(pericia => pericia.Laudos.Max(laudo => (DateTime?)laudo.AtualizadoEm)).ToListAsync());
    }

    public async Task<IActionResult> LaudosDaPericia(int id)
    {
        var pericia = await db.Pericias.AsNoTracking()
            .Include(p => p.Periciados)
            .Include(p => p.Laudos).ThenInclude(l => l.Periciado)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();
        ViewBag.Modelos = await db.ModelosLaudo.AsNoTracking().OrderBy(m => m.Nome).ToListAsync();
        return View(pericia);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarLaudo(int periciaId, int? periciadoId, int? modeloId)
    {
        var pericia = await db.Pericias.Include(p => p.Periciados).FirstOrDefaultAsync(p => p.Id == periciaId);
        if (pericia is null) return NotFound();
        if (periciadoId.HasValue && !pericia.Periciados.Any(p => p.Id == periciadoId.Value))
        {
            TempData["Erro"] = "O periciado selecionado não pertence a esta perícia.";
            return RedirectToAction(nameof(LaudosDaPericia), new { id = periciaId });
        }

        var numerosLaudos = await db.LaudosPericia.Select(l => l.NumeroLaudo).ToListAsync();
        var maiorNumero = numerosLaudos
            .Select(numero => int.TryParse(numero, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) ? valor : 0)
            .DefaultIfEmpty(0)
            .Max();
        var proximoNumero = maiorNumero < int.MaxValue ? maiorNumero + 1 : 1;
        while (numerosLaudos.Contains(proximoNumero.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            proximoNumero++;
        var numeroLaudo = proximoNumero.ToString(CultureInfo.InvariantCulture);

        var modelo = modeloId.HasValue ? await db.ModelosLaudo.FirstOrDefaultAsync(m => m.Id == modeloId.Value) : null;
        if (modeloId.HasValue && modelo is null) return NotFound();
        var laudo = new LaudoPericia
        {
            PericiaId = periciaId,
            NumeroLaudo = numeroLaudo,
            PericiadoId = periciadoId,
            ConteudoHtml = modelo is null ? "" : sanitizer.Sanitize(modelo.ConteudoHtml),
            Status = modelo is null ? StatusLaudo.Rascunho : StatusLaudo.EmElaboracao,
            AtualizadoEm = DateTime.UtcNow
        };
        db.LaudosPericia.Add(laudo);
        RegistrarHistorico(periciaId, "Laudo", $"Laudo {numeroLaudo} criado.");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(EditorLaudo), new { id = periciaId, laudoId = laudo.Id });
    }

    public async Task<IActionResult> ModelosLaudo()
    {
        ViewBag.Modelos = await db.ModelosLaudo.AsNoTracking().OrderBy(m => m.Nome).ToListAsync();
        return View(new ModeloLaudo());
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> SalvarModeloLaudo(int id, string? nome, string? conteudoHtml)
    {
        nome = nome?.Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 100 || conteudoHtml?.Length > 100_000)
        {
            TempData["ErroModelo"] = "Informe um nome e um conteúdo de até 100 mil caracteres.";
            return RedirectToAction(nameof(ModelosLaudo));
        }
        var modelo = id == 0 ? new ModeloLaudo() : await db.ModelosLaudo.FirstOrDefaultAsync(m => m.Id == id);
        if (modelo is null) return NotFound();
        modelo.Nome = nome;
        modelo.ConteudoHtml = sanitizer.Sanitize(conteudoHtml ?? "");
        modelo.AtualizadoEm = DateTime.UtcNow;
        if (id == 0) db.ModelosLaudo.Add(modelo);
        await db.SaveChangesAsync();
        TempData["MensagemModelo"] = "Modelo salvo.";
        return RedirectToAction(nameof(ModelosLaudo));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirModeloLaudo(int id)
    {
        var modelo = await db.ModelosLaudo.FirstOrDefaultAsync(m => m.Id == id);
        if (modelo is null) return NotFound();
        db.ModelosLaudo.Remove(modelo);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(ModelosLaudo));
    }

    public async Task<IActionResult> Exportar()
    {
        var registros = await db.Pericias.AsNoTracking().Include(p => p.Periciados).OrderBy(p => p.Numero).ToListAsync();
        static string Campo(string? valor) => $"\"{(valor ?? "").Replace("\"", "\"\"")}\"";
        var linhas = new List<string> { "Periciado;Numero;DataEnvio;DataAceite;PrazoLaudo;Honorarios;ValorRecebido;Status;Observacoes" };
        linhas.AddRange(registros.Select(p => string.Join(';', new[]
        {
            Campo(string.Join(" | ", p.Periciados.Select(pessoa => pessoa.Nome))), Campo(p.Numero), Campo(p.DataEnvio?.ToString("dd/MM/yyyy")),
            Campo(p.DataAceite?.ToString("dd/MM/yyyy")), Campo(p.PrazoLaudo?.ToString("dd/MM/yyyy")),
            Campo(p.ValorHonorarios.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.ValorRecebido.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.Status.ToString()), Campo(p.Observacoes)
        })));
        var conteudo = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", linhas))).ToArray();
        return File(conteudo, "text/csv; charset=utf-8", $"pericias-{BrasiliaTime.Today:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> Backup()
    {
        var nomeArquivo = $"pericias-backup-{BrasiliaTime.Now:yyyy-MM-dd-HHmm}.zip";
        var diretorioTemporario = Path.Combine(Path.GetTempPath(), $"pericias-backup-{Guid.NewGuid():N}");
        var caminhoBanco = Path.Combine(diretorioTemporario, "pericias.db");
        var caminhoZip = Path.Combine(diretorioTemporario, nomeArquivo);
        Directory.CreateDirectory(diretorioTemporario);
        try
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                await using var destino = new SqliteConnection($"Data Source={caminhoBanco}");
                await destino.OpenAsync();
                ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destino);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }

            var usuarioId = UsuarioAtualId();
            await LimparCopiaParaContaAsync(caminhoBanco, usuarioId);

            using (var arquivoZip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
            {
                arquivoZip.CreateEntryFromFile(caminhoBanco, "pericias.db", CompressionLevel.Fastest);
                var quantidadeDocumentos = 0;
                var nomesDocumentos = await db.DocumentosPericia.AsNoTracking().Select(d => d.NomeArmazenado).ToListAsync();
                if (Directory.Exists(diretorioDocumentos))
                {
                    foreach (var nome in nomesDocumentos)
                    {
                        var documento = Path.Combine(diretorioDocumentos, Path.GetFileName(nome));
                        if (!System.IO.File.Exists(documento)) continue;
                        arquivoZip.CreateEntryFromFile(documento, $"documentos/{Path.GetFileName(documento)}", CompressionLevel.Fastest);
                        quantidadeDocumentos++;
                    }
                }
                var manifesto = arquivoZip.CreateEntry("manifesto.json", CompressionLevel.Fastest);
                using var writer = new StreamWriter(manifesto.Open(), new UTF8Encoding(false));
                writer.Write(System.Text.Json.JsonSerializer.Serialize(new ManifestoBackup("PericiaClara", 1, SqliteSchema.VersaoEsquema, DateTime.UtcNow, quantidadeDocumentos)));
            }

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
            return PhysicalFile(caminhoZip, "application/zip", nomeArquivo);
        }
        catch
        {
            if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
            throw;
        }
    }

    private int UsuarioAtualId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static async Task LimparCopiaParaContaAsync(string caminhoBanco, int usuarioId)
    {
        if (usuarioId <= 0) throw new InvalidOperationException("Não foi possível identificar a conta dona do backup.");
        await using var conexao = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminhoBanco }.ToString());
        await conexao.OpenAsync();
        await using var transacao = (SqliteTransaction)await conexao.BeginTransactionAsync();
        foreach (var tabela in new[] { "VersoesLaudo", "PagamentosHonorarios", "HistoricoPericias", "EventosAgenda", "DocumentosPericia", "LaudosPericia", "Periciados", "Pericias", "ConfiguracoesPerito", "UnidadesForenses", "ModelosLaudo" })
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
            comando.CommandText = "DELETE FROM \"ContasUsuarios\" WHERE \"Id\" <> $usuarioId; UPDATE \"ContasUsuarios\" SET \"SenhaHash\" = 'REMOVIDA_DA_EXPORTACAO' WHERE \"Id\" = $usuarioId;";
            comando.Parameters.AddWithValue("$usuarioId", usuarioId);
            await comando.ExecuteNonQueryAsync();
        }
        await transacao.CommitAsync();
    }

    public async Task<IActionResult> Criar()
    {
        await PrepararCatalogoForenseAsync();
        return View("Formulario", new Pericia());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(PericiaFormInputDto input)
    {
        input.Periciados ??= [];
        await AplicarComarcaDoCodigoTjrsAsync(input);
        input.TipoPericia = await db.ConfiguracoesPerito.AsNoTracking()
            .Select(c => c.Especialidade)
            .FirstOrDefaultAsync();
        var pericia = input.ToEntity();
        ValidarPericiados(input.Periciados);
        if (input.Periciados.Any(p => p.Id.HasValue))
            ModelState.AddModelError(nameof(input.Periciados), "Não é possível vincular pessoas existentes a uma nova perícia.");
        if (!Enum.IsDefined(pericia.Status)) ModelState.AddModelError(nameof(pericia.Status), "Selecione um status válido.");
        if (await db.Pericias.AnyAsync(p => p.Numero == pericia.Numero))
            ModelState.AddModelError(nameof(pericia.Numero), "Esse número de perícia já está cadastrado.");
        if (!ModelState.IsValid)
        {
            await PrepararCatalogoForenseAsync();
            return View("Formulario", pericia);
        }
        pericia.CriadaEm = DateTime.UtcNow;
        db.Pericias.Add(pericia);
        await db.SaveChangesAsync();
        RegistrarHistorico(pericia.Id, "Processo", "Perícia cadastrada.");
        await db.SaveChangesAsync();
        TempData["Mensagem"] = "Perícia cadastrada com sucesso.";
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Editar(int id)
    {
        var pericia = await db.Pericias.Include(p => p.Documentos).Include(p => p.Periciados).Include(p => p.Pagamentos).FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();
        await PrepararCatalogoForenseAsync();
        return View("Formulario", pericia);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar([FromRoute] int id, PericiaFormInputDto input)
    {
        input.Periciados ??= [];
        await AplicarComarcaDoCodigoTjrsAsync(input);
        var pericia = input.ToEntity();
        pericia.Id = id;
        var atual = await db.Pericias.Include(p => p.Periciados).FirstOrDefaultAsync(p => p.Id == id);
        if (atual is null) return NotFound();
        ValidarPericiados(input.Periciados);
        var idsEnviados = input.Periciados.Where(p => p.Id.HasValue).Select(p => p.Id!.Value).ToList();
        if (idsEnviados.Distinct().Count() != idsEnviados.Count ||
            input.Periciados.Any(p => p.Id.HasValue && !atual.Periciados.Any(existente => existente.Id == p.Id.Value)))
            ModelState.AddModelError(nameof(input.Periciados), "A lista contém um periciado inválido para esta perícia.");
        if (!Enum.IsDefined(pericia.Status)) ModelState.AddModelError(nameof(pericia.Status), "Selecione um status válido.");
        if (await db.Pericias.AnyAsync(p => p.Numero == pericia.Numero && p.Id != id))
            ModelState.AddModelError(nameof(pericia.Numero), "Esse número de perícia já está cadastrado.");
        if (!ModelState.IsValid)
        {
            await PrepararCatalogoForenseAsync();
            pericia.ValorRecebido = await db.Pericias.AsNoTracking().Where(p => p.Id == id).Select(p => p.ValorRecebido).FirstOrDefaultAsync();
            pericia.Pagamentos = await db.PagamentosHonorarios.Where(p => p.PericiaId == id).OrderByDescending(p => p.DataRecebimento).ToListAsync();
            pericia.Documentos = await db.DocumentosPericia.Where(d => d.PericiaId == id).ToListAsync();
            return View("Formulario", pericia);
        }
        AtualizarCamposEditaveis(atual, pericia);
        foreach (var existente in atual.Periciados.ToList())
        {
            if (!idsEnviados.Contains(existente.Id)) db.Periciados.Remove(existente);
        }
        foreach (var pessoaInput in input.Periciados)
        {
            var pessoa = pessoaInput.Id.HasValue
                ? atual.Periciados.Single(p => p.Id == pessoaInput.Id.Value)
                : new Periciado { PericiaId = id };
            pessoa.Nome = pessoaInput.Nome.Trim();
            pessoa.Contato = pessoaInput.Telefone?.Trim();
            pessoa.RegistroEntrevista = pessoaInput.Observacoes?.Trim();
            if (!pessoaInput.Id.HasValue) atual.Periciados.Add(pessoa);
        }
        RegistrarHistorico(id, "Processo", "Cadastro do processo ou dados dos periciados atualizados.");
        await db.SaveChangesAsync();
        TempData["Mensagem"] = "Perícia atualizada.";
        var arquivada = (pericia.Status == StatusPericia.Cancelada || pericia.Status == StatusPericia.Concluida || pericia.Status == StatusPericia.Recusada) &&
            atual.ValorRecebido >= atual.ValorHonorarios;
        return arquivada ? RedirectToAction(nameof(Processos), new { arquivadas = true }) : RedirectToAction(nameof(Index));
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
    public async Task<IActionResult> BaixarDocumento(int id)
    {
        var documento = await db.DocumentosPericia.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (documento is null) return NotFound();

        var caminho = Path.Combine(diretorioDocumentos, documento.NomeArmazenado);
        if (!System.IO.File.Exists(caminho)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(caminho, ContentTypeDocumento(documento.NomeArmazenado), documento.NomeOriginal);
    }

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

    public async Task<IActionResult> EditorLaudo(int id, int laudoId)
    {
        var laudo = await db.LaudosPericia
            .Include(l => l.Pericia).ThenInclude(p => p.Periciados)
            .Include(l => l.Periciado)
            .Include(l => l.Versoes)
            .FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        var modelo = CriarModeloLaudo(laudo);
        modelo.ConteudoHtml = sanitizer.Sanitize(modelo.ConteudoHtml);
        return View(modelo);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> SalvarLaudo(int id, int laudoId, int? periciadoId, string? conteudoHtml, string? tipoLaudo, bool previsualizar = false)
    {
        var laudo = await db.LaudosPericia.FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        if (periciadoId.HasValue && !await db.Periciados.AnyAsync(p => p.Id == periciadoId.Value && p.PericiaId == id))
        {
            TempData["ErroLaudo"] = "O periciado selecionado não pertence a esta perícia.";
            return RedirectToAction(nameof(EditorLaudo), new { id, laudoId });
        }
        if (conteudoHtml?.Length > 200_000)
        {
            TempData["ErroLaudo"] = "O texto do laudo excede o limite de 200 mil caracteres.";
            return RedirectToAction(nameof(EditorLaudo), new { id, laudoId });
        }

        var tipoNormalizado = string.IsNullOrWhiteSpace(tipoLaudo) ? null : tipoLaudo.Trim();
        var conteudoNormalizado = sanitizer.Sanitize(conteudoHtml ?? "");
        if (laudo.PericiadoId != periciadoId || laudo.Tipo != tipoNormalizado || laudo.ConteudoHtml != conteudoNormalizado)
            db.VersoesLaudo.Add(new VersaoLaudo { LaudoId = laudo.Id, NumeroLaudo = laudo.NumeroLaudo, Tipo = laudo.Tipo, Status = laudo.Status, Prazo = laudo.Prazo, ConteudoHtml = laudo.ConteudoHtml, Responsavel = User.Identity?.Name, SalvoEm = DateTime.UtcNow });
        laudo.PericiadoId = periciadoId;
        laudo.Tipo = tipoNormalizado;
        laudo.ConteudoHtml = conteudoNormalizado;
        laudo.AtualizadoEm = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(laudo.ConteudoHtml) && laudo.Status == StatusLaudo.Rascunho)
            laudo.Status = StatusLaudo.EmElaboracao;
        RegistrarHistorico(id, "Laudo", $"Conteúdo ou metadados do laudo {laudo.NumeroLaudo} salvos.");
        await db.SaveChangesAsync();
        TempData["MensagemLaudo"] = "Rascunho salvo.";
        return RedirectToAction(previsualizar ? nameof(VisualizarLaudo) : nameof(EditorLaudo), new { id, laudoId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestaurarVersaoLaudo(int id, int laudoId, int versaoId)
    {
        var laudo = await db.LaudosPericia.Include(l => l.Versoes).FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        var versao = await db.VersoesLaudo.FirstOrDefaultAsync(v => v.Id == versaoId && v.LaudoId == laudoId);
        if (laudo is null || versao is null) return NotFound();
        db.VersoesLaudo.Add(new VersaoLaudo { LaudoId = laudo.Id, NumeroLaudo = laudo.NumeroLaudo, Tipo = laudo.Tipo, Status = laudo.Status, Prazo = laudo.Prazo, ConteudoHtml = laudo.ConteudoHtml, Responsavel = User.Identity?.Name, SalvoEm = DateTime.UtcNow });
        laudo.Tipo = versao.Tipo; laudo.Status = versao.Status; laudo.Prazo = versao.Prazo; laudo.ConteudoHtml = sanitizer.Sanitize(versao.ConteudoHtml); laudo.AtualizadoEm = DateTime.UtcNow;
        RegistrarHistorico(id, "Laudo", $"Versão salva em {BrasiliaTime.ToBrasilia(versao.SalvoEm):dd/MM/yyyy HH:mm} restaurada.");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(EditorLaudo), new { id, laudoId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatusLaudo(int id, int laudoId, StatusLaudo status, string? origem)
    {
        var laudo = await db.LaudosPericia.FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        if (!Enum.IsDefined(status)) return BadRequest();
        laudo.Status = status;
        laudo.EntregueEm = status == StatusLaudo.Entregue ? laudo.EntregueEm ?? DateTime.UtcNow : null;
        laudo.AtualizadoEm = DateTime.UtcNow;
        RegistrarHistorico(id, "Laudo", $"Status do laudo {laudo.NumeroLaudo} alterado para {status}.");
        await db.SaveChangesAsync();
        return origem switch
        {
            "laudos-pericia" => RedirectToAction(nameof(LaudosDaPericia), new { id }),
            "laudos" => RedirectToAction(nameof(Laudos)),
            _ => RedirectToAction(nameof(Processo), new { id })
        };
    }

    public async Task<IActionResult> VisualizarLaudo(int id, int laudoId)
    {
        var laudo = await db.LaudosPericia
            .Include(l => l.Pericia).ThenInclude(p => p.Periciados)
            .Include(l => l.Periciado)
            .FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        if (string.IsNullOrWhiteSpace(laudo.ConteudoHtml))
            return RedirectToAction(nameof(EditorLaudo), new { id, laudoId });

        var modelo = CriarModeloLaudo(laudo);
        modelo.ConteudoHtml = sanitizer.Sanitize(laudo.ConteudoHtml);
        return View(modelo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirLaudo(int id, int laudoId)
    {
        var laudo = await db.LaudosPericia.FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        db.LaudosPericia.Remove(laudo);
        await db.SaveChangesAsync();
        TempData["Mensagem"] = $"Laudo {laudo.NumeroLaudo} removido.";
        return RedirectToAction(nameof(LaudosDaPericia), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirDocumento(int id)
    {
        var documento = await db.DocumentosPericia.FirstOrDefaultAsync(d => d.Id == id);
        if (documento is null) return NotFound();

        var periciaId = documento.PericiaId;
        var caminho = Path.Combine(diretorioDocumentos, documento.NomeArmazenado);
        foreach (var pagamento in await db.PagamentosHonorarios.Where(p => p.DocumentoPericiaId == id).ToListAsync())
            pagamento.DocumentoPericiaId = null;
        db.DocumentosPericia.Remove(documento);
        RegistrarHistorico(periciaId, "Documento", "Documento removido do processo.");
        await db.SaveChangesAsync();
        if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);

        TempData["Mensagem"] = "Documento removido.";
        return RedirectToAction(nameof(Editar), new { id = periciaId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(int id)
    {
        var pericia = await db.Pericias.Include(p => p.Documentos).FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is not null)
        {
            var nomesArquivos = pericia.Documentos.Select(d => d.NomeArmazenado).ToArray();
            db.Pericias.Remove(pericia);
            await db.SaveChangesAsync();
            foreach (var nome in nomesArquivos)
            {
                var caminho = Path.Combine(diretorioDocumentos, nome);
                if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
            }
        }
        TempData["Mensagem"] = "Perícia removida.";
        return RedirectToAction(nameof(Index));
    }
}
