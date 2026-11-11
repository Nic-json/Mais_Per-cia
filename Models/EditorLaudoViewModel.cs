namespace Sistemas_para_pericias.Models;

public class EditorLaudoViewModel
{
    public int PericiaId { get; set; }
    public string NomePericiado { get; set; } = "";
    public string Numero { get; set; } = "";
    public string ConteudoHtml { get; set; } = "";
    public DateTime? AtualizadoEm { get; set; }
}
