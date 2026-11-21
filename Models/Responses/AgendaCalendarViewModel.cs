namespace Sistemas_para_pericias.Models.Responses;

public sealed record AgendaCalendarEntryViewModel(
    DateTime Data,
    string Titulo,
    string Categoria,
    string? Hora = null,
    string? NumeroPericia = null,
    int? EventoId = null);

public sealed record AgendaCalendarDayViewModel(
    DateTime Data,
    bool DentroDoPeriodo,
    bool Hoje,
    IReadOnlyList<AgendaCalendarEntryViewModel> Itens);

