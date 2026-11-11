using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sistemas_para_pericias.Models;

public enum StatusPericia
{
    [Display(Name = "Aguardando aceite")] AguardandoAceite,
    [Display(Name = "Em andamento")] EmAndamento,
    [Display(Name = "Laudo enviado")] LaudoEnviado,
    [Display(Name = "Aguardando pagamento")] AguardandoPagamento,
    [Display(Name = "Concluída")] Concluida,
    [Display(Name = "Cancelada")] Cancelada
}

public class Pericia
{
    public int Id { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    [Display(Name = "Nome do periciado")]
    public string NomePericiado { get; set; } = "";

    [NotMapped, Display(Name = "Outros periciados (um por linha)")]
    public string OutrosPericiados { get; set; } = "";

    [Required, StringLength(80)]
    [Display(Name = "Número da perícia")]
    public string Numero { get; set; } = "";

    [Display(Name = "Data de envio"), DataType(DataType.Date)]
    public DateTime? DataEnvio { get; set; }

    [Display(Name = "Data de aceite"), DataType(DataType.Date)]
    public DateTime? DataAceite { get; set; }

    [Display(Name = "Prazo do laudo"), DataType(DataType.Date)]
    public DateTime? PrazoLaudo { get; set; }

    [Display(Name = "Valor dos honorários"), DataType(DataType.Currency), Range(0, 999999999)]
    public decimal ValorHonorarios { get; set; }

    [Display(Name = "Valor recebido"), DataType(DataType.Currency), Range(0, 999999999)]
    public decimal ValorRecebido { get; set; }

    [Display(Name = "Status")]
    public StatusPericia Status { get; set; } = StatusPericia.AguardandoAceite;

    [StringLength(2000), Display(Name = "Observações")]
    public string? Observacoes { get; set; }

    [Display(Name = "Cadastrada em")]
    public DateTime CriadaEm { get; set; } = DateTime.Now;

    public List<DocumentoPericia> Documentos { get; set; } = [];

    public List<Periciado> Periciados { get; set; } = [];

    public List<LaudoPericia> Laudos { get; set; } = [];
}

public class Periciado
{
    public int Id { get; set; }

    public int PericiaId { get; set; }

    [Required, StringLength(160)]
    public string Nome { get; set; } = "";

    public Pericia Pericia { get; set; } = null!;

    public List<LaudoPericia> Laudos { get; set; } = [];
}

public class DocumentoPericia
{
    public int Id { get; set; }

    public int PericiaId { get; set; }

    [Required, StringLength(255)]
    public string NomeOriginal { get; set; } = "";

    [Required, StringLength(40)]
    public string NomeArmazenado { get; set; } = "";

    public long TamanhoBytes { get; set; }

    public DateTime EnviadoEm { get; set; } = DateTime.UtcNow;

    public Pericia Pericia { get; set; } = null!;
}

public class LaudoPericia
{
    public int Id { get; set; }

    public int PericiaId { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Número do laudo")]
    public string NumeroLaudo { get; set; } = "1";

    public int? PericiadoId { get; set; }

    public string ConteudoHtml { get; set; } = "";

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public Pericia Pericia { get; set; } = null!;

    public Periciado? Periciado { get; set; }
}
