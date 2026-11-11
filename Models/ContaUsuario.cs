using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public sealed class ContaUsuario
{
    public int Id { get; set; }

    [Required, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(254)]
    public string EmailNormalizado { get; set; } = "";

    [Required, StringLength(160)]
    public string Nome { get; set; } = "";

    [Required, StringLength(512)]
    public string SenhaHash { get; set; } = "";

    public bool Ativo { get; set; } = true;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
