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
}


