using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

/// <summary>Complementos de códigos de foros adicionados pela própria conta, por exemplo quando descobrir códigos ausentes ou atualizados.</summary>
public sealed class UnidadeForense : IUsuarioDono
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    [Required, StringLength(12)] public string Tribunal { get; set; } = "TJRS";
    [Required, StringLength(2)] public string Uf { get; set; } = "RS";
    [Required, StringLength(4, MinimumLength = 4, ErrorMessage = "Informe os quatro dígitos do código.")]
    [RegularExpression("^[0-9]{4}$", ErrorMessage = "O código deve conter exatamente quatro números.")]
    public string Codigo { get; set; } = "";
    [Required, StringLength(120)] public string Comarca { get; set; } = "";
    [StringLength(120)] public string? Foro { get; set; }
}

public sealed class UnidadeForenseInputDto
{
    [Required, StringLength(4, MinimumLength = 4)]
    [RegularExpression("^[0-9]{4}$", ErrorMessage = "O código deve conter exatamente quatro números.")]
    public string Codigo { get; set; } = "";
    [Required, StringLength(120)] public string Comarca { get; set; } = "";
    [StringLength(120)] public string? Foro { get; set; }
}
