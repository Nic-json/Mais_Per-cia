using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models.Responses;

public sealed class SolicitarRedefinicaoSenhaViewModel
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";
}

