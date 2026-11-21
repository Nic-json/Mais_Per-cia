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

public partial class HomeController
{
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

    public IActionResult Arquivadas(string? busca) => RedirectToAction(nameof(Processos), new { busca, arquivadas = true });

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

    public async Task<IActionResult> BaixarDocumento(int id)
    {
        var documento = await db.DocumentosPericia.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (documento is null) return NotFound();

        var caminho = Path.Combine(diretorioDocumentos, documento.NomeArmazenado);
        if (!System.IO.File.Exists(caminho)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(caminho, ContentTypeDocumento(documento.NomeArmazenado), documento.NomeOriginal);
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


