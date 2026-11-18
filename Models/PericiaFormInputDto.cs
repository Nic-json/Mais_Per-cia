using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

/// <summary>Campos que o formulário de cadastro/edição está autorizado a alterar.</summary>
public sealed class PericiaFormInputDto
{
    [MaxLength(50)] public List<PericiadoFormInputDto> Periciados { get; set; } = [];
    [Required, StringLength(80)] public string Numero { get; set; } = "";
    [StringLength(80)] public string? NumeroProcesso { get; set; }
    [StringLength(120)] public string? Comarca { get; set; }
    [StringLength(120)] public string? Vara { get; set; }
    [StringLength(160)] public string? Magistrado { get; set; }
    [StringLength(120)] public string? TipoPericia { get; set; }
    public DateTime? DataEnvio { get; set; }
    public DateTime? DataAceite { get; set; }
    public DateTime? PrazoLaudo { get; set; }
    [Range(0, 999999999)] public decimal ValorHonorarios { get; set; }
    public DateTime? DataHonorariosFixados { get; set; }
    [Range(0, 999999999)] public decimal ValorHonorariosSolicitados { get; set; }
    public DateTime? DataHonorariosSolicitados { get; set; }
    [Range(0, 999999999)] public decimal ValorHonorariosLiberados { get; set; }
    public DateTime? DataHonorariosLiberados { get; set; }
    public StatusPericia Status { get; set; } = StatusPericia.AguardandoAceite;
    [StringLength(2000)] public string? Observacoes { get; set; }

    public Pericia ToEntity() => new()
    {
        NomePericiado = "",
        Periciados = (Periciados ?? []).Select(p => new Periciado
        {
            Id = p.Id ?? 0,
            Nome = p.Nome?.Trim() ?? "",
            Contato = p.Telefone?.Trim(),
            RegistroEntrevista = p.Observacoes?.Trim()
        }).ToList(),
        Numero = (Numero ?? string.Empty).Trim(),
        NumeroProcesso = NumeroProcesso?.Trim(),
        Comarca = Comarca?.Trim(),
        Vara = Vara?.Trim(),
        Magistrado = Magistrado?.Trim(),
        TipoPericia = TipoPericia?.Trim(),
        DataEnvio = DataEnvio,
        DataAceite = DataAceite,
        PrazoLaudo = PrazoLaudo,
        ValorHonorarios = ValorHonorarios,
        DataHonorariosFixados = DataHonorariosFixados,
        ValorHonorariosSolicitados = ValorHonorariosSolicitados,
        DataHonorariosSolicitados = DataHonorariosSolicitados,
        ValorHonorariosLiberados = ValorHonorariosLiberados,
        DataHonorariosLiberados = DataHonorariosLiberados,
        Status = Status,
        Observacoes = Observacoes?.Trim()
    };
}

public sealed class PericiadoFormInputDto
{
    public int? Id { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    public string Nome { get; set; } = "";

    [StringLength(200)]
    public string? Telefone { get; set; }

    [StringLength(4000)]
    public string? Observacoes { get; set; }
}
