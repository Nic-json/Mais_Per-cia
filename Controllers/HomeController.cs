using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Ganss.Xss;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Controllers;

public class HomeController(PericiasContext db, IConfiguration configuration, IWebHostEnvironment environment, HtmlSanitizer sanitizer) : Controller
{
    private const long TamanhoMaximoDocumento = 25 * 1024 * 1024;
    private const long TamanhoMaximoBanco = 50 * 1024 * 1024;
    private readonly string diretorioDocumentos = configuration["Documentos:Diretorio"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "uploads");

    private IQueryable<Pericia> ConsultaVisaoGeral() => db.Pericias.AsNoTracking().Where(p =>
        p.Status != StatusPericia.Cancelada &&
        !(p.Status == StatusPericia.Concluida && p.ValorRecebido >= p.ValorHonorarios));

    private IQueryable<Pericia> ConsultaArquivadas() => db.Pericias.AsNoTracking().Where(p =>
        p.Status == StatusPericia.Cancelada ||
        (p.Status == StatusPericia.Concluida && p.ValorRecebido >= p.ValorHonorarios));

    private static List<string> ObterNomesPericiados(string nomePrincipal, string? outrosNomes) =>
        new[] { nomePrincipal }
            .Concat((outrosNomes ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(nome => nome.Trim())
            .Where(nome => nome.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ValidarNomesPericiados(IReadOnlyCollection<string> nomes)
    {
        if (nomes.Count > 50)
            ModelState.AddModelError(nameof(Pericia.OutrosPericiados), "Cadastre no máximo 50 periciados por perícia.");
        if (nomes.Any(nome => nome.Length > 160))
            ModelState.AddModelError(nameof(Pericia.OutrosPericiados), "Cada nome deve ter no máximo 160 caracteres.");
    }

    private static void PrepararOutrosNomes(Pericia pericia)
    {
        pericia.OutrosPericiados = string.Join(Environment.NewLine,
            pericia.Periciados.Where(p => !string.Equals(p.Nome, pericia.NomePericiado, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Nome));
    }

    public IActionResult Banco()
    {
        var pastaRestauracao = Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!, "backups-restauracao");
        ViewBag.TemPontoRestauracao = Directory.Exists(pastaRestauracao) && Directory.EnumerateFiles(pastaRestauracao, "antes-restauracao-*.db").Any();
        return View();
    }

    public IActionResult BaixarPontoRestauracao()
    {
        var pastaRestauracao = Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!, "backups-restauracao");
        if (!Directory.Exists(pastaRestauracao)) return NotFound();
        var caminho = Directory.EnumerateFiles(pastaRestauracao, "antes-restauracao-*.db")
            .OrderByDescending(System.IO.File.GetCreationTimeUtc)
            .FirstOrDefault();
        return caminho is null ? NotFound() : PhysicalFile(caminho, "application/vnd.sqlite3", Path.GetFileName(caminho));
    }

    public async Task<IActionResult> BaixarBanco()
    {
        var nomeArquivo = $"pericias-{DateTime.Now:yyyy-MM-dd-HHmm}.db";
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
            return PhysicalFile(caminhoBanco, "application/vnd.sqlite3", nomeArquivo);
        }
        catch
        {
            if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
            throw;
        }
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(TamanhoMaximoBanco + 1024 * 1024)]
    public async Task<IActionResult> RestaurarBanco(IFormFile? arquivoBanco, bool confirmar = false)
    {
        if (!confirmar)
        {
            TempData["ErroBanco"] = "Marque a confirmação para substituir o banco atual.";
            return RedirectToAction(nameof(Banco));
        }
        if (arquivoBanco is null || arquivoBanco.Length < 16 || arquivoBanco.Length > TamanhoMaximoBanco ||
            !new[] { ".db", ".sqlite", ".sqlite3" }.Contains(Path.GetExtension(arquivoBanco.FileName), StringComparer.OrdinalIgnoreCase))
        {
            TempData["ErroBanco"] = "Selecione um arquivo SQLite válido de até 50 MB.";
            return RedirectToAction(nameof(Banco));
        }

        var caminhoTemporario = Path.Combine(Path.GetTempPath(), $"pericias-import-{Guid.NewGuid():N}.db");
        try
        {
            await using (var destinoTemporario = new FileStream(caminhoTemporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await arquivoBanco.CopyToAsync(destinoTemporario);

            var assinaturaEsperada = Encoding.ASCII.GetBytes("SQLite format 3\0");
            await using (var assinaturaStream = System.IO.File.OpenRead(caminhoTemporario))
            {
                var assinatura = new byte[assinaturaEsperada.Length];
                await assinaturaStream.ReadExactlyAsync(assinatura);
                if (!assinatura.SequenceEqual(assinaturaEsperada))
                {
                    TempData["ErroBanco"] = "O arquivo enviado não parece ser um banco SQLite.";
                    return RedirectToAction(nameof(Banco));
                }
            }

            await using (var importado = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminhoTemporario }.ToString()))
            {
                await importado.OpenAsync();
                var tabelas = await ObterTabelasAsync(importado);
                if (!tabelas.Contains("Pericias"))
                {
                    TempData["ErroBanco"] = "O banco enviado não contém a tabela de perícias deste sistema.";
                    return RedirectToAction(nameof(Banco));
                }

                await using (var duplicados = importado.CreateCommand())
                {
                    duplicados.CommandText = "SELECT 1 FROM \"Pericias\" GROUP BY \"Numero\" HAVING COUNT(*) > 1 LIMIT 1;";
                    try
                    {
                        if (await duplicados.ExecuteScalarAsync() is not null)
                        {
                            TempData["ErroBanco"] = "O banco contém números de perícia repetidos.";
                            return RedirectToAction(nameof(Banco));
                        }
                    }
                    catch (SqliteException)
                    {
                        TempData["ErroBanco"] = "O banco enviado tem uma estrutura incompatível com o sistema.";
                        return RedirectToAction(nameof(Banco));
                    }
                }

                SqliteSchema.EnsureAdditionalTables(importado, criarBackupAntesMigracao: false);
                var colunasPericia = await ObterColunasAsync(importado, "Pericias");
                var colunasObrigatorias = new[] { "Id", "NomePericiado", "Numero", "DataEnvio", "DataAceite", "PrazoLaudo", "ValorHonorarios", "ValorRecebido", "Status", "Observacoes", "CriadaEm" };
                var colunasDocumentos = await ObterColunasAsync(importado, "DocumentosPericia");
                var colunasPericiados = await ObterColunasAsync(importado, "Periciados");
                var colunasLaudos = await ObterColunasAsync(importado, "LaudosPericia");
                if (colunasObrigatorias.Any(coluna => !colunasPericia.Contains(coluna)) ||
                    new[] { "Id", "PericiaId", "NomeOriginal", "NomeArmazenado", "TamanhoBytes", "EnviadoEm" }.Any(coluna => !colunasDocumentos.Contains(coluna)) ||
                    new[] { "Id", "PericiaId", "Nome" }.Any(coluna => !colunasPericiados.Contains(coluna)) ||
                    new[] { "Id", "PericiaId", "NumeroLaudo", "PericiadoId", "ConteudoHtml", "AtualizadoEm" }.Any(coluna => !colunasLaudos.Contains(coluna)))
                {
                    TempData["ErroBanco"] = "O banco enviado tem uma estrutura incompatível com o sistema.";
                    return RedirectToAction(nameof(Banco));
                }

                await using var integridade = importado.CreateCommand();
                integridade.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(Convert.ToString(await integridade.ExecuteScalarAsync()), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErroBanco"] = "O SQLite encontrou problemas de integridade no arquivo enviado.";
                    return RedirectToAction(nameof(Banco));
                }

                await using var chaves = importado.CreateCommand();
                chaves.CommandText = "PRAGMA foreign_key_check;";
                await using var resultadoChaves = await chaves.ExecuteReaderAsync();
                if (await resultadoChaves.ReadAsync())
                {
                    TempData["ErroBanco"] = "O arquivo contém referências inválidas entre tabelas.";
                    return RedirectToAction(nameof(Banco));
                }
            }

            var caminhoAtual = db.Database.GetDbConnection().DataSource;
            var pastaRestauracao = Path.Combine(Path.GetDirectoryName(caminhoAtual)!, "backups-restauracao");
            Directory.CreateDirectory(pastaRestauracao);
            var caminhoPontoRestauracao = Path.Combine(pastaRestauracao, $"antes-restauracao-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");

            await db.Database.OpenConnectionAsync();
            try
            {
                var bancoAtual = (SqliteConnection)db.Database.GetDbConnection();
                await using var pontoRestauracao = new SqliteConnection($"Data Source={caminhoPontoRestauracao}");
                await pontoRestauracao.OpenAsync();
                bancoAtual.BackupDatabase(pontoRestauracao);

                await using var importado = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminhoTemporario, Mode = SqliteOpenMode.ReadOnly }.ToString());
                await importado.OpenAsync();
                try
                {
                    importado.BackupDatabase(bancoAtual);
                }
                catch
                {
                    pontoRestauracao.BackupDatabase(bancoAtual);
                    throw;
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }

            var pontosRestauracao = Directory.EnumerateFiles(pastaRestauracao, "antes-restauracao-*.db")
                .OrderByDescending(System.IO.File.GetCreationTimeUtc)
                .Skip(3);
            foreach (var pontoAntigo in pontosRestauracao)
                System.IO.File.Delete(pontoAntigo);

            TempData["MensagemBanco"] = "Banco atualizado. Um ponto de restauração anterior foi guardado na VPS.";
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidDataException)
        {
            TempData["ErroBanco"] = "Não foi possível importar esse banco. O banco atual foi preservado.";
        }
        finally
        {
            if (System.IO.File.Exists(caminhoTemporario)) System.IO.File.Delete(caminhoTemporario);
        }

        return RedirectToAction(nameof(Banco));
    }

    private static async Task<HashSet<string>> ObterTabelasAsync(SqliteConnection connection)
    {
        var tabelas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var comando = connection.CreateCommand();
        comando.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync()) tabelas.Add(leitor.GetString(0));
        return tabelas;
    }

    private static async Task<HashSet<string>> ObterColunasAsync(SqliteConnection connection, string tabela)
    {
        var colunas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var comando = connection.CreateCommand();
        comando.CommandText = $"PRAGMA table_info(\"{tabela}\");";
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync()) colunas.Add(leitor.GetString(1));
        return colunas;
    }
    [AllowAnonymous]
    public IActionResult Error() => View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });

    public async Task<IActionResult> Index(string? busca, StatusPericia? status)
    {
        var visaoGeral = ConsultaVisaoGeral();
        IQueryable<Pericia> query = visaoGeral.Include(p => p.Periciados);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(p => p.NomePericiado.Contains(busca) || p.Numero.Contains(busca) || p.Periciados.Any(pessoa => pessoa.Nome.Contains(busca)));
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        ViewData["Title"] = "Visão geral";
        ViewBag.Busca = busca;
        ViewBag.StatusFiltro = status;
        ViewBag.Total = await visaoGeral.CountAsync();
        ViewBag.Ativas = await visaoGeral.CountAsync(p => p.Status == StatusPericia.EmAndamento || p.Status == StatusPericia.LaudoEnviado);
        ViewBag.AguardandoPagamento = await visaoGeral.CountAsync(p => p.Status == StatusPericia.AguardandoPagamento || p.ValorRecebido < p.ValorHonorarios);
        ViewBag.PrazosVencidos = await visaoGeral.CountAsync(p => p.PrazoLaudo < DateTime.Today && p.Status != StatusPericia.Concluida);
        ViewBag.TotalRecebido = await visaoGeral.SumAsync(p => (decimal?)p.ValorRecebido) ?? 0m;
        return View(await query.OrderByDescending(p => p.CriadaEm).ToListAsync());
    }

    public async Task<IActionResult> Arquivadas(string? busca)
    {
        IQueryable<Pericia> query = ConsultaArquivadas().Include(p => p.Periciados);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(p => p.NomePericiado.Contains(busca) || p.Numero.Contains(busca) || p.Periciados.Any(pessoa => pessoa.Nome.Contains(busca)));
        ViewBag.Busca = busca;
        ViewBag.Total = await ConsultaArquivadas().CountAsync();
        return View(await query.OrderByDescending(p => p.CriadaEm).ToListAsync());
    }

    public async Task<IActionResult> Laudos(string? busca)
    {
        IQueryable<LaudoPericia> query = db.LaudosPericia.AsNoTracking()
            .Include(laudo => laudo.Pericia).ThenInclude(pericia => pericia.Periciados)
            .Include(laudo => laudo.Periciado);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(laudo => laudo.NumeroLaudo.Contains(busca) || laudo.Pericia.Numero.Contains(busca) ||
                laudo.Pericia.NomePericiado.Contains(busca) || laudo.Pericia.Periciados.Any(pessoa => pessoa.Nome.Contains(busca)));
        ViewBag.Busca = busca;
        ViewBag.Total = await query.CountAsync();
        return View(await query.OrderByDescending(laudo => laudo.AtualizadoEm).ToListAsync());
    }

    public async Task<IActionResult> NovoLaudo()
    {
        var pericias = await ConsultaVisaoGeral()
            .Include(p => p.Periciados)
            .OrderBy(p => p.Numero)
            .ToListAsync();
        return View(pericias);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarLaudo(int periciaId, string? numeroLaudo, int? periciadoId)
    {
        var pericia = await db.Pericias.Include(p => p.Periciados).FirstOrDefaultAsync(p => p.Id == periciaId);
        if (pericia is null) return NotFound();
        numeroLaudo = numeroLaudo?.Trim();
        if (string.IsNullOrWhiteSpace(numeroLaudo) || numeroLaudo.Length > 80)
        {
            TempData["Erro"] = "Informe um número de laudo com até 80 caracteres.";
            return RedirectToAction(nameof(Editar), new { id = periciaId });
        }
        if (periciadoId.HasValue && !pericia.Periciados.Any(p => p.Id == periciadoId.Value))
        {
            TempData["Erro"] = "O periciado selecionado não pertence a esta perícia.";
            return RedirectToAction(nameof(Editar), new { id = periciaId });
        }
        if (await db.LaudosPericia.AnyAsync(l => l.PericiaId == periciaId && l.NumeroLaudo == numeroLaudo))
        {
            TempData["Erro"] = "Já existe um laudo com esse número nesta perícia.";
            return RedirectToAction(nameof(Editar), new { id = periciaId });
        }

        var laudo = new LaudoPericia
        {
            PericiaId = periciaId,
            NumeroLaudo = numeroLaudo,
            PericiadoId = periciadoId,
            AtualizadoEm = DateTime.UtcNow
        };
        db.LaudosPericia.Add(laudo);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(EditorLaudo), new { id = periciaId, laudoId = laudo.Id });
    }

    public async Task<IActionResult> Exportar()
    {
        var registros = await db.Pericias.AsNoTracking().Include(p => p.Periciados).OrderBy(p => p.Numero).ToListAsync();
        static string Campo(string? valor) => $"\"{(valor ?? "").Replace("\"", "\"\"")}\"";
        var linhas = new List<string> { "Periciado;Numero;DataEnvio;DataAceite;PrazoLaudo;Honorarios;ValorRecebido;Status;Observacoes" };
        linhas.AddRange(registros.Select(p => string.Join(';', new[]
        {
            Campo(string.Join(" | ", p.Periciados.Select(pessoa => pessoa.Nome).Prepend(p.NomePericiado).Distinct(StringComparer.OrdinalIgnoreCase))), Campo(p.Numero), Campo(p.DataEnvio?.ToString("dd/MM/yyyy")),
            Campo(p.DataAceite?.ToString("dd/MM/yyyy")), Campo(p.PrazoLaudo?.ToString("dd/MM/yyyy")),
            Campo(p.ValorHonorarios.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.ValorRecebido.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.Status.ToString()), Campo(p.Observacoes)
        })));
        var conteudo = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", linhas))).ToArray();
        return File(conteudo, "text/csv; charset=utf-8", $"pericias-{DateTime.Today:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> Backup()
    {
        var nomeArquivo = $"pericias-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";
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

            using (var arquivoZip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
            {
                arquivoZip.CreateEntryFromFile(caminhoBanco, "pericias.db", CompressionLevel.Fastest);
                if (Directory.Exists(diretorioDocumentos))
                {
                    foreach (var documento in Directory.EnumerateFiles(diretorioDocumentos))
                        arquivoZip.CreateEntryFromFile(documento, $"documentos/{Path.GetFileName(documento)}", CompressionLevel.Fastest);
                }
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
            return PhysicalFile(caminhoZip, "application/zip", nomeArquivo);
        }
        catch
        {
            if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
            throw;
        }
    }

    public IActionResult Criar(bool abrirLaudoDepois = false)
    {
        ViewBag.AbrirLaudoDepois = abrirLaudoDepois;
        return View("Formulario", new Pericia { DataEnvio = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(Pericia pericia, bool abrirLaudoDepois = false)
    {
        var nomes = ObterNomesPericiados(pericia.NomePericiado, pericia.OutrosPericiados);
        ValidarNomesPericiados(nomes);
        if (await db.Pericias.AnyAsync(p => p.Numero == pericia.Numero))
            ModelState.AddModelError(nameof(pericia.Numero), "Esse número de perícia já está cadastrado.");
        if (!ModelState.IsValid)
        {
            ViewBag.AbrirLaudoDepois = abrirLaudoDepois;
            return View("Formulario", pericia);
        }
        pericia.CriadaEm = DateTime.Now;
        pericia.Periciados = nomes.Select(nome => new Periciado { Nome = nome }).ToList();
        db.Pericias.Add(pericia);
        await db.SaveChangesAsync();
        TempData["Mensagem"] = "Perícia cadastrada com sucesso.";
        if (abrirLaudoDepois) return RedirectToAction(nameof(Editar), new { id = pericia.Id });
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Editar(int id)
    {
        var pericia = await db.Pericias.Include(p => p.Documentos).Include(p => p.Periciados).Include(p => p.Laudos).ThenInclude(l => l.Periciado).FirstOrDefaultAsync(p => p.Id == id);
        if (pericia is null) return NotFound();
        PrepararOutrosNomes(pericia);
        return View("Formulario", pericia);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, Pericia pericia)
    {
        if (id != pericia.Id) return NotFound();
        var nomes = ObterNomesPericiados(pericia.NomePericiado, pericia.OutrosPericiados);
        ValidarNomesPericiados(nomes);
        if (await db.Pericias.AnyAsync(p => p.Numero == pericia.Numero && p.Id != id))
            ModelState.AddModelError(nameof(pericia.Numero), "Esse número de perícia já está cadastrado.");
        if (!ModelState.IsValid)
        {
            pericia.Documentos = await db.DocumentosPericia.Where(d => d.PericiaId == id).ToListAsync();
            pericia.Periciados = await db.Periciados.Where(p => p.PericiaId == id).ToListAsync();
            pericia.Laudos = await db.LaudosPericia.Include(l => l.Periciado).Where(l => l.PericiaId == id).OrderBy(l => l.NumeroLaudo).ToListAsync();
            return View("Formulario", pericia);
        }
        var atual = await db.Pericias.Include(p => p.Periciados).FirstOrDefaultAsync(p => p.Id == id);
        if (atual is null) return NotFound();
        db.Entry(atual).CurrentValues.SetValues(pericia);
        foreach (var pessoa in atual.Periciados.ToList())
        {
            if (!nomes.Contains(pessoa.Nome, StringComparer.OrdinalIgnoreCase))
                db.Periciados.Remove(pessoa);
        }
        var nomesExistentes = atual.Periciados.Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var nome in nomes.Where(nome => !nomesExistentes.Contains(nome)))
            atual.Periciados.Add(new Periciado { PericiaId = id, Nome = nome });
        await db.SaveChangesAsync();
        TempData["Mensagem"] = "Perícia atualizada.";
        var arquivada = pericia.Status == StatusPericia.Cancelada ||
            (pericia.Status == StatusPericia.Concluida && pericia.ValorRecebido >= pericia.ValorHonorarios);
        return RedirectToAction(arquivada ? nameof(Arquivadas) : nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(TamanhoMaximoDocumento + 1024 * 1024)]
    public async Task<IActionResult> AnexarDocumento(int id, IFormFile? arquivo)
    {
        var pericia = await db.Pericias.FindAsync(id);
        if (pericia is null) return NotFound();

        if (arquivo is null || arquivo.Length == 0)
        {
            TempData["Erro"] = "Selecione um arquivo PDF, Word, JPG ou PNG para anexar.";
            return RedirectToAction(nameof(Editar), new { id });
        }
        if (arquivo.Length > TamanhoMaximoDocumento)
        {
            TempData["Erro"] = "O arquivo excede o limite de 25 MB.";
            return RedirectToAction(nameof(Editar), new { id });
        }

        var nomeOriginal = Path.GetFileName(arquivo.FileName.Replace('\\', '/')).Trim();
        nomeOriginal = new string(nomeOriginal.Where(caractere => !char.IsControl(caractere)).ToArray());
        var extensao = Path.GetExtension(nomeOriginal).ToLowerInvariant();
        var extensoesPermitidas = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png" };
        if (string.IsNullOrWhiteSpace(nomeOriginal) || nomeOriginal.Length > 255 || !extensoesPermitidas.Contains(extensao))
        {
            TempData["Erro"] = "Formato não permitido. Envie PDF, DOC, DOCX, JPG ou PNG, com nome de até 255 caracteres.";
            return RedirectToAction(nameof(Editar), new { id });
        }
        if (!await ArquivoCorrespondeAoFormatoAsync(arquivo, extensao))
        {
            TempData["Erro"] = "O conteúdo do arquivo não corresponde ao formato informado.";
            return RedirectToAction(nameof(Editar), new { id });
        }

        Directory.CreateDirectory(diretorioDocumentos);
        var nomeArmazenado = $"{Guid.NewGuid():N}{extensao}";
        var caminho = Path.Combine(diretorioDocumentos, nomeArmazenado);
        await using (var destino = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await arquivo.CopyToAsync(destino);

        db.DocumentosPericia.Add(new DocumentoPericia
        {
            PericiaId = id,
            NomeOriginal = nomeOriginal,
            NomeArmazenado = nomeArmazenado,
            TamanhoBytes = arquivo.Length,
            EnviadoEm = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
            throw;
        }

        TempData["Mensagem"] = "Documento anexado com sucesso.";
        return RedirectToAction(nameof(Editar), new { id });
    }

    private static async Task<bool> ArquivoCorrespondeAoFormatoAsync(IFormFile arquivo, string extensao)
    {
        try
        {
            await using var stream = arquivo.OpenReadStream();
            var assinatura = new byte[Math.Min(8, (int)arquivo.Length)];
            var totalLido = 0;
            while (totalLido < assinatura.Length)
            {
                var lido = await stream.ReadAsync(assinatura.AsMemory(totalLido));
                if (lido == 0) break;
                totalLido += lido;
            }
            if (totalLido != assinatura.Length) return false;

            return extensao switch
            {
                ".pdf" => Encoding.ASCII.GetString(assinatura).StartsWith("%PDF-", StringComparison.Ordinal),
                ".jpg" or ".jpeg" => assinatura.Length >= 3 && assinatura[0] == 0xFF && assinatura[1] == 0xD8 && assinatura[2] == 0xFF,
                ".png" => assinatura.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                ".doc" => assinatura.SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
                ".docx" => EhDocumentoWordModerno(stream, assinatura),
                _ => false
            };
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static bool EhDocumentoWordModerno(Stream stream, byte[] assinatura)
    {
        if (assinatura.Length < 4 || assinatura[0] != 0x50 || assinatura[1] != 0x4B || assinatura[2] != 0x03 || assinatura[3] != 0x04)
            return false;

        stream.Position = 0;
        using var arquivoZip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var temTiposConteudo = arquivoZip.GetEntry("[Content_Types].xml") is not null;
        var temDocumento = arquivoZip.GetEntry("word/document.xml") is not null;
        return temTiposConteudo && temDocumento;
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
        return System.IO.File.Exists(caminho)
            ? PhysicalFile(caminho, ContentTypeDocumento(documento.NomeArmazenado), documento.NomeOriginal)
            : NotFound();
    }

    private static EditorLaudoViewModel CriarModeloLaudo(LaudoPericia laudo) => new()
    {
        PericiaId = laudo.PericiaId,
        LaudoId = laudo.Id,
        NumeroLaudo = laudo.NumeroLaudo,
        PericiadoId = laudo.PericiadoId,
        Periciados = laudo.Pericia.Periciados.OrderBy(p => p.Nome).ToList(),
        NomePericiado = laudo.Periciado?.Nome ?? laudo.Pericia.NomePericiado,
        Numero = laudo.Pericia.Numero,
        ConteudoHtml = laudo.ConteudoHtml,
        AtualizadoEm = laudo.AtualizadoEm
    };

    public async Task<IActionResult> EditorLaudo(int id, int laudoId)
    {
        var laudo = await db.LaudosPericia
            .Include(l => l.Pericia).ThenInclude(p => p.Periciados)
            .Include(l => l.Periciado)
            .FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        return laudo is null ? NotFound() : View(CriarModeloLaudo(laudo));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> SalvarLaudo(int id, int laudoId, string? numeroLaudo, int? periciadoId, string? conteudoHtml, bool previsualizar = false)
    {
        var laudo = await db.LaudosPericia.FirstOrDefaultAsync(l => l.Id == laudoId && l.PericiaId == id);
        if (laudo is null) return NotFound();
        numeroLaudo = numeroLaudo?.Trim();
        if (string.IsNullOrWhiteSpace(numeroLaudo) || numeroLaudo.Length > 80 ||
            await db.LaudosPericia.AnyAsync(l => l.PericiaId == id && l.Id != laudoId && l.NumeroLaudo == numeroLaudo))
        {
            TempData["ErroLaudo"] = "Informe um número de laudo válido e ainda não utilizado nesta perícia.";
            return RedirectToAction(nameof(EditorLaudo), new { id, laudoId });
        }
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

        laudo.NumeroLaudo = numeroLaudo;
        laudo.PericiadoId = periciadoId;
        laudo.ConteudoHtml = sanitizer.Sanitize(conteudoHtml ?? "");
        laudo.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["MensagemLaudo"] = "Rascunho salvo.";
        return RedirectToAction(previsualizar ? nameof(VisualizarLaudo) : nameof(EditorLaudo), new { id, laudoId });
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
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirDocumento(int id)
    {
        var documento = await db.DocumentosPericia.FindAsync(id);
        if (documento is null) return NotFound();

        var periciaId = documento.PericiaId;
        var caminho = Path.Combine(diretorioDocumentos, documento.NomeArmazenado);
        db.DocumentosPericia.Remove(documento);
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
