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
    [Display(Name = "Cancelada")] Cancelada,
    [Display(Name = "Recusada")] Recusada
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

    [StringLength(80), Display(Name = "Número do processo (CNJ)")]
    public string? NumeroProcesso { get; set; }

    [StringLength(120), Display(Name = "Comarca")]
    public string? Comarca { get; set; }

    [StringLength(120), Display(Name = "Vara")]
    public string? Vara { get; set; }

    [StringLength(160), Display(Name = "Magistrado(a)")]
    public string? Magistrado { get; set; }

    [StringLength(120), Display(Name = "Tipo de perícia")]
    public string? TipoPericia { get; set; }

    [Display(Name = "Data de envio"), DataType(DataType.Date)]
    public DateTime? DataEnvio { get; set; }

    [Display(Name = "Data de aceite"), DataType(DataType.Date)]
    public DateTime? DataAceite { get; set; }

    [Display(Name = "Prazo do laudo"), DataType(DataType.Date)]
    public DateTime? PrazoLaudo { get; set; }

    [Display(Name = "Valor dos honorários"), DataType(DataType.Currency), Range(0, 999999999)]
    public decimal ValorHonorarios { get; set; }

    [Display(Name = "Data de fixação"), DataType(DataType.Date)]
    public DateTime? DataHonorariosFixados { get; set; }

    [Display(Name = "Honorários solicitados"), DataType(DataType.Currency), Range(0, 999999999)]
    public decimal ValorHonorariosSolicitados { get; set; }

    [Display(Name = "Data da solicitação"), DataType(DataType.Date)]
    public DateTime? DataHonorariosSolicitados { get; set; }

    [Display(Name = "Honorários liberados"), DataType(DataType.Currency), Range(0, 999999999)]
    public decimal ValorHonorariosLiberados { get; set; }

    [Display(Name = "Data da liberação"), DataType(DataType.Date)]
    public DateTime? DataHonorariosLiberados { get; set; }

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

    public List<EventoAgenda> EventosAgenda { get; set; } = [];

    public List<PagamentoHonorario> Pagamentos { get; set; } = [];

    public List<HistoricoPericia> Historico { get; set; } = [];
}

public class Periciado
{
    public int Id { get; set; }

    public int PericiaId { get; set; }

    [Required, StringLength(160)]
    public string Nome { get; set; } = "";

    [StringLength(80)] public string? Papel { get; set; }
    [StringLength(120)] public string? Relacao { get; set; }
    [StringLength(200)] public string? Contato { get; set; }
    [StringLength(4000)] public string? RegistroEntrevista { get; set; }

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

    [StringLength(80)]
    public string? Tipo { get; set; }

    public StatusLaudo Status { get; set; } = StatusLaudo.Rascunho;

    public DateTime? Prazo { get; set; }

    public DateTime? EntregueEm { get; set; }

    public string ConteudoHtml { get; set; } = "";

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public Pericia Pericia { get; set; } = null!;

    public Periciado? Periciado { get; set; }
    public List<VersaoLaudo> Versoes { get; set; } = [];
}

public class VersaoLaudo
{
    public int Id { get; set; }
    public int LaudoId { get; set; }
    [Required, StringLength(80)] public string NumeroLaudo { get; set; } = "";
    [StringLength(80)] public string? Tipo { get; set; }
    public StatusLaudo Status { get; set; }
    public DateTime? Prazo { get; set; }
    [StringLength(200000)] public string ConteudoHtml { get; set; } = "";
    [StringLength(256)] public string? Responsavel { get; set; }
    public DateTime SalvoEm { get; set; } = DateTime.UtcNow;
    public LaudoPericia Laudo { get; set; } = null!;
}

public enum StatusLaudo
{
    [Display(Name = "Rascunho")] Rascunho,
    [Display(Name = "Em elaboração")] EmElaboracao,
    [Display(Name = "Concluído")] Concluido,
    [Display(Name = "Entregue")] Entregue,
    [Display(Name = "Complemento")] Complemento
}

public class EventoAgenda
{
    public int Id { get; set; }
    public int PericiaId { get; set; }
    public int? PericiadoId { get; set; }
    [Required, StringLength(160)] public string Titulo { get; set; } = "";
    [Required] public DateTime Inicio { get; set; }
    public DateTime? Fim { get; set; }
    [StringLength(240)] public string? Local { get; set; }
    [StringLength(40)] public string? Modalidade { get; set; }
    [StringLength(2000)] public string? Observacoes { get; set; }
    public bool Cancelado { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public Pericia Pericia { get; set; } = null!;
    public Periciado? Periciado { get; set; }
}

public class PagamentoHonorario
{
    public int Id { get; set; }
    public int PericiaId { get; set; }
    [Range(0.01, 999999999)] public decimal Valor { get; set; }
    [DataType(DataType.Date)] public DateTime? DataRecebimento { get; set; } = DateTime.Today;
    public int? DocumentoPericiaId { get; set; }
    [StringLength(2000)] public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public Pericia Pericia { get; set; } = null!;
}

public class HistoricoPericia
{
    public int Id { get; set; }
    public int PericiaId { get; set; }
    [Required, StringLength(80)] public string Tipo { get; set; } = "";
    [Required, StringLength(1000)] public string Descricao { get; set; } = "";
    [StringLength(256)] public string? Responsavel { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public Pericia Pericia { get; set; } = null!;
}

public class ConfiguracaoPerito
{
    public int Id { get; set; } = 1;
    [StringLength(160)] public string? NomeProfissional { get; set; }
    [StringLength(120)] public string? Especialidade { get; set; }
    [StringLength(80)] public string? RegistroProfissional { get; set; }
    [EmailAddress, StringLength(200)] public string? EmailContato { get; set; }
    [StringLength(40)] public string? Telefone { get; set; }
    [Range(0, 60)] public int AntecedenciaPrazoDias { get; set; } = 7;
    public bool AvisarPrazosNoPainel { get; set; } = true;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public class ModeloLaudo
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Nome { get; set; } = "";
    [StringLength(100000)] public string ConteudoHtml { get; set; } = "";
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
