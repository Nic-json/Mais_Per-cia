using System.Text.RegularExpressions;

namespace Sistemas_para_pericias.Services;

public sealed record ForoTjrs(string Codigo, string Comarca, string Foro);

/// <summary>
/// Catálogo histórico publicado pelo TJRS e hospedado pelo CNJ (28/07/2009).
/// Códigos descobertos ou corrigidos por cada conta substituem o registro de mesmo código.
/// </summary>
public static partial class CatalogoTjrs
{
    private const string DadosHistoricos = """
0154|Agudo|Foro de Agudo
0002|Alegrete|Foro de Alegrete
0003|Alvorada|Foro de Alvorada
0079|Antônio Prado|Foro de Antônio Prado
0080|Arroio do Meio|Foro de Arroio do Meio
0143|Arroio do Tigre|Foro de Arroio do Tigre
0081|Arroio Grande|Foro de Arroio Grande
0082|Arvorezinha|Foro de Arvorezinha
0149|Augusto Pestana|Foro de Augusto Pestana
0004|Bagé|Foro de Bagé
0140|Barra do Ribeiro|Foro de Barra do Ribeiro
0005|Bento Gonçalves|Foro de Bento Gonçalves
0083|Bom Jesus|Foro de Bom Jesus
0084|Butiá|Foro de Butiá
0040|Caçapava do Sul|Foro de Caçapava do Sul
0085|Cacequi|Foro de Cacequi
0006|Cachoeira do Sul|Foro de Cachoeira do Sul
0086|Cachoeirinha|Foro de Cachoeirinha
0007|Camaquã|Foro de Camaquã
0150|Campina das Missões|Foro de Campina das Missões
0087|Campo Bom|Foro de Campo Bom
0088|Campo Novo|Foro de Campo Novo
0089|Candelária|Foro de Candelária
0041|Canela|Foro de Canela
0042|Canguçu|Foro de Canguçu
0008|Canoas|Foro de Canoas
0141|Capão da Canoa|Foro de Capão da Canoa
0009|Carazinho|Foro de Carazinho
0144|Carlos Barbosa|Foro de Carlos Barbosa
0090|Casca|Foro de Casca
0091|Catuípe|Foro de Catuípe
0010|Caxias do Sul|Foro de Caxias do Sul
0043|Cerro Largo|Foro de Cerro Largo
0156|Charqueadas|Foro de Charqueadas
0161|Salto do Jacuí|Foro de Salto do Jacuí
0067|São Lourenço do Sul|Foro de São Lourenço do Sul
0092|Constantina|Foro de Constantina
0093|Coronel Bicaco|Foro de Coronel Bicaco
0094|Crissiumal|Foro de Crissiumal
0011|Cruz Alta|Foro de Cruz Alta
0145|Dois Irmãos|Foro de Dois Irmãos
0012|Dom Pedrito|Foro de Dom Pedrito
0165|Eldorado do Sul|Foro de Eldorado do Sul
0044|Encantado|Foro de Encantado
0045|Encruzilhada do Sul|Foro de Encruzilhada do Sul
0013|Erechim|Foro de Erechim
0046|Espumoso|Foro de Espumoso
0095|Estância Velha|Foro de Estância Velha
0014|Esteio|Foro de Esteio
0047|Estrela|Foro de Estrela
0048|Farroupilha|Foro de Farroupilha
0096|Faxinal do Soturno|Foro de Faxinal do Soturno
0146|Feliz|Foro de Feliz
0097|Flores da Cunha|Foro de Flores da Cunha
0049|Frederico Westphalen|Foro de Frederico Westphalen
0051|Garibaldi|Foro de Garibaldi
0098|Gaurama|Foro de Gaurama
0099|General Câmara|Foro de General Câmara
0050|Getúlio Vargas|Foro de Getúlio Vargas
0100|Giruá|Foro de Giruá
0101|Gramado|Foro de Gramado
0015|Gravataí|Foro de Gravataí
0052|Guaíba|Foro de Guaíba
0053|Guaporé|Foro de Guaporé
0102|Guarani das Missões|Foro de Guarani das Missões
0103|Herval|Foro de Herval
0104|Horizontina|Foro de Horizontina
0105|Ibirubá|Foro de Ibirubá
0142|Igrejinha|Foro de Igrejinha
0016|Ijuí|Foro de Ijuí
0106|Iraí|Foro de Iraí
0054|Itaqui|Foro de Itaqui
0165|Ivoti|Foro de Ivoti
0055|Jaguarão|Foro de Jaguarão
0107|Jaguari|Foro de Jaguari
0056|Júlio de Castilhos|Foro de Júlio de Castilhos
0057|Lagoa Vermelha|Foro de Lagoa Vermelha
0017|Lajeado|Foro de Lajeado
0108|Lavras do Sul|Foro de Lavras do Sul
0109|Marau|Foro de Marau
0110|Marcelino Ramos|Foro de Marcelino Ramos
0018|Montenegro|Foro de Montenegro
0111|Mostardas|Foro de Mostardas
0112|Não-Me-Toque|Foro de Não-Me-Toque
0113|Nonoai|Foro de Nonoai
0114|Nova Petrópolis|Foro de Nova Petrópolis
0058|Nova Prata|Foro de Nova Prata
0059|Osório|Foro de Osório
0163|Osório|Foro de Osório (Vara Integrada Terra de Areia)
0151|Palmares do Sul|Foro de Palmares do Sul
0020|Palmeira das Missões|Foro de Palmeira das Missões
0060|Panambi|Foro de Panambi
0157|Parobé|Foro de Parobé
0021|Passo Fundo|Foro de Passo Fundo
0115|Pedro Osório|Foro de Pedro Osório
0022|Pelotas|Foro de Pelotas
0117|Pinheiro Machado|Foro de Pinheiro Machado
0118|Piratini|Foro de Piratini
0116|Planalto|Foro de Planalto
0155|Portão|Foro de Portão
0001|Porto Alegre|Foro Central
0119|Porto Xavier|Foro de Porto Xavier
0061|Quaraí|Foro de Quaraí
0147|Restinga Seca|Foro de Restinga Seca
0023|Rio Grande|Foro de Rio Grande
0024|Rio Pardo|Foro de Rio Pardo
0158|Rodeio Bonito|Foro de Rodeio Bonito
0148|Ronda Alta|Foro de Ronda Alta
0062|Rosário do Sul|Foro de Rosário do Sul
0120|Sananduva|Foro de Sananduva
0121|Santa Bárbara do Sul|Foro de Santa Bárbara do Sul
0026|Santa Cruz do Sul|Foro de Santa Cruz do Sul
0027|Santa Maria|Foro de Santa Maria
0028|Santa Rosa|Foro de Santa Rosa
0063|Santa Vitória do Palmar|Foro de Santa Vitória do Palmar
0025|Santana do Livramento|Foro de Santana do Livramento
0064|Santiago|Foro de Santiago
0029|Santo Ângelo|Foro de Santo Ângelo
0065|Santo Antônio da Patrulha|Foro de Santo Antônio da Patrulha
0122|Santo Antônio das Missões|Foro de Santo Antônio das Missões
0123|Santo Augusto|Foro de Santo Augusto
0124|Santo Cristo|Foro de Santo Cristo
0030|São Borja|Foro de São Borja
0125|São Francisco de Assis|Foro de São Francisco de Assis
0066|São Francisco de Paula|Foro de São Francisco de Paula
0031|São Gabriel|Foro de São Gabriel
0032|São Jerônimo|Foro de São Jerônimo
0126|São José do Norte|Foro de São José do Norte
0127|São José do Ouro|Foro de São José do Ouro
0033|São Leopoldo|Foro de São Leopoldo
0034|São Luiz Gonzaga|Foro de São Luiz Gonzaga
0128|São Marcos|Foro de São Marcos
0129|São Pedro do Sul|Foro de São Pedro do Sul
0068|São Sebastião do Caí|Foro de São Sebastião do Caí
0130|São Sepé|Foro de São Sepé
0152|São Valentim|Foro de São Valentim
0131|São Vicente do Sul|Foro de São Vicente do Sul
0132|Sapiranga|Foro de Sapiranga
0035|Sapucaia do Sul|Foro de Sapucaia do Sul
0069|Sarandi|Foro de Sarandi
0133|Seberi|Foro de Seberi
0134|Sobradinho|Foro de Sobradinho
0036|Soledade|Foro de Soledade
0135|Tapejara|Foro de Tapejara
0136|Tapes|Foro de Tapes
0137|Tapes|Foro de Tapes
0070|Taquara|Foro de Taquara
0071|Taquari|Foro de Taquari
0138|Tenente Portela|Foro de Tenente Portela
0159|Teutônia|Foro de Teutônia
0072|Torres|Foro de Torres
0073|Tramandaí|Foro de Tramandaí
0164|Três Coroas|Foro de Três Coroas
0074|Três de Maio|Foro de Três de Maio
0075|Três Passos|Foro de Três Passos
0139|Triunfo|Foro de Triunfo
0153|Tucunduva|Foro de Tucunduva
0076|Tupanciretã|Foro de Tupanciretã
0037|Uruguaiana|Foro de Uruguaiana
0038|Vacaria|Foro de Vacaria
0077|Venâncio Aires|Foro de Venâncio Aires
0160|Vera Cruz|Foro de Vera Cruz
0078|Veranópolis|Foro de Veranópolis
0039|Viamão|Foro de Viamão
7000|Tribunal de Justiça|Tribunal de Justiça
9000|Turmas Recursais|Turmas Recursais
""";

    public static IReadOnlyList<ForoTjrs> ListaBase { get; } = DadosHistoricos
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(linha => linha.Split('|'))
        .Where(partes => partes.Length == 3)
        .Select(partes => new ForoTjrs(partes[0], partes[1], partes[2]))
        .ToArray();

    public static string? ExtrairCodigo(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return null;
        var match = CodigoTjrsRegex().Match(numero.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IdentificaForoDeComarca(string codigo) => codigo is not "7000" and not "9000";

    [GeneratedRegex(@"\.8\.21\.(\d{4})(?:/RS)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodigoTjrsRegex();
}
