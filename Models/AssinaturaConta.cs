using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public sealed class AssinaturaConta
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    [MaxLength(100)] public string Status { get; set; } = "Pendente";
    [MaxLength(120)] public string? CheckoutId { get; set; }
    [MaxLength(120)] public string? CustomerId { get; set; }
    [MaxLength(120)] public string? SubscriptionId { get; set; }
    public DateTime? AcessoAte { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
