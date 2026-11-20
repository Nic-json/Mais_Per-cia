namespace Sistemas_para_pericias.Models;

public sealed class TokenConta
{
    public string Hash { get; set; } = "";
    public int UsuarioId { get; set; }
    public string Finalidade { get; set; } = "";
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
}
