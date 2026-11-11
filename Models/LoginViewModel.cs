using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(256)]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = "";
}
