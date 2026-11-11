namespace Sistemas_para_pericias.Models;

public sealed class AssinaturaViewModel
{
    public decimal? ValorMensal { get; init; }
    public string Status { get; init; } = "Pendente";
    public DateTime? AcessoAte { get; init; }
    public string? CheckoutUrl { get; init; }
    public bool CheckoutConfigurado { get; init; }
}
