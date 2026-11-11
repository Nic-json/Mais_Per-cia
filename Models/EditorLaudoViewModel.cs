namespace Sistemas_para_pericias.Models;

public class EditorLaudoViewModel
{
    public int PericiaId { get; set; }
    public int LaudoId { get; set; }
    public string NumeroLaudo { get; set; } = "";
    public int? PericiadoId { get; set; }
    public List<Periciado> Periciados { get; set; } = [];
    public string NomePericiado { get; set; } = "";
    public string Numero { get; set; } = "";
    public string ConteudoHtml { get; set; } = "";
    public DateTime? AtualizadoEm { get; set; }
}
