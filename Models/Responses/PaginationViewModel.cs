namespace Sistemas_para_pericias.Models.Responses;

public sealed class PaginationViewModel
{
    public PaginationViewModel(string action, int pagina, int totalPaginas, int tamanhoPagina, Dictionary<string, string> filtros)
    {
        Action = action;
        Pagina = pagina;
        TotalPaginas = Math.Max(1, totalPaginas);
        TamanhoPagina = tamanhoPagina;
        Filtros = filtros;
    }

    public string Action { get; }
    public int Pagina { get; }
    public int TotalPaginas { get; }
    public int TamanhoPagina { get; }
    public Dictionary<string, string> Filtros { get; }
}

