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
}


