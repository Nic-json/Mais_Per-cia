using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public sealed class RedefinirSenhaViewModel
{
    [Required]
    public string Token { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "Nova senha (mínimo 12 caracteres)")]
    public string NovaSenha { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NovaSenha))]
    [Display(Name = "Confirme a nova senha")]
    public string ConfirmarNovaSenha { get; set; } = "";
}
