using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

/// <summary>Preferências editáveis no formulário; credenciais MFA e campos de sistema ficam fora do payload.</summary>
public sealed class ConfiguracoesInputDto
{
    [StringLength(160)] public string? NomeProfissional { get; set; }
    [StringLength(120)] public string? Especialidade { get; set; }
    [StringLength(80)] public string? RegistroProfissional { get; set; }
    [EmailAddress, StringLength(200)] public string? EmailContato { get; set; }
    [StringLength(40)] public string? Telefone { get; set; }
    [Range(0, 60)] public int AntecedenciaPrazoDias { get; set; } = 7;
    public bool AvisarPrazosNoPainel { get; set; } = true;

    public ConfiguracaoPerito ToViewModel() => new()
    {
        NomeProfissional = NomeProfissional?.Trim(),
        Especialidade = Especialidade?.Trim(),
        RegistroProfissional = RegistroProfissional?.Trim(),
        EmailContato = EmailContato?.Trim(),
        Telefone = Telefone?.Trim(),
        AntecedenciaPrazoDias = AntecedenciaPrazoDias,
        AvisarPrazosNoPainel = AvisarPrazosNoPainel,
        AtualizadoEm = DateTime.UtcNow
    };
}
