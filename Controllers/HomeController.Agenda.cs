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
}


