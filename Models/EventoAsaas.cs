using System.ComponentModel.DataAnnotations;

namespace Sistemas_para_pericias.Models;

public sealed class EventoAsaas
{
    [Key, MaxLength(120)] public string Id { get; set; } = "";
    public DateTime RecebidoEm { get; set; } = DateTime.UtcNow;
}
