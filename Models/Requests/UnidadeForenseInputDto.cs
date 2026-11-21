using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models.Requests;

public sealed class UnidadeForenseInputDto
{
    [Required, StringLength(4, MinimumLength = 4)]
    [RegularExpression("^[0-9]{4}$", ErrorMessage = "O código deve conter exatamente quatro números.")]
    public string Codigo { get; set; } = "";
    [Required, StringLength(120)] public string Comarca { get; set; } = "";
    [StringLength(120)] public string? Foro { get; set; }
}
