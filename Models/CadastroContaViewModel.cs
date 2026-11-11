using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public sealed class CadastroContaViewModel
{
    [Required, StringLength(160, MinimumLength = 2)]
    [Display(Name = "Seu nome")]
    public string Nome { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "Senha (mínimo 12 caracteres)")]
    public string Senha { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Senha))]
    [Display(Name = "Confirme a senha")]
    public string ConfirmarSenha { get; set; } = "";
}
