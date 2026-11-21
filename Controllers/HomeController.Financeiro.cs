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
}


