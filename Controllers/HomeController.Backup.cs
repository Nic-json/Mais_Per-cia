using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Ganss.Xss;
using System.IO.Compression;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;
using Sistemas_para_pericias.Models.Requests;
using Sistemas_para_pericias.Models.Responses;
using Sistemas_para_pericias.Services;
using Sistemas_para_pericias.Utilities;

namespace Sistemas_para_pericias.Controllers;

public partial class HomeController
{
    public async Task<IActionResult> Exportar()
    {
        var registros = await db.Pericias.AsNoTracking().Include(p => p.Periciados).OrderBy(p => p.Numero).ToListAsync();
        static string Campo(string? valor) => $"\"{(valor ?? "").Replace("\"", "\"\"")}\"";
        var linhas = new List<string> { "Periciado;Numero;DataEnvio;DataAceite;PrazoLaudo;Honorarios;ValorRecebido;Status;Observacoes" };
        linhas.AddRange(registros.Select(p => string.Join(';', new[]
        {
            Campo(string.Join(" | ", p.Periciados.Select(pessoa => pessoa.Nome))), Campo(p.Numero), Campo(p.DataEnvio?.ToString("dd/MM/yyyy")),
            Campo(p.DataAceite?.ToString("dd/MM/yyyy")), Campo(p.PrazoLaudo?.ToString("dd/MM/yyyy")),
            Campo(p.ValorHonorarios.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.ValorRecebido.ToString("0.00", CultureInfo.InvariantCulture)),
            Campo(p.Status.ToString()), Campo(p.Observacoes)
        })));
        var conteudo = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", linhas))).ToArray();
        return File(conteudo, "text/csv; charset=utf-8", $"pericias-{BrasiliaTime.Today:yyyy-MM-dd}.csv");
    }

    public async Task<IActionResult> Backup()
    {
        var nomeArquivo = $"pericias-backup-{BrasiliaTime.Now:yyyy-MM-dd-HHmm}.zip";
        var diretorioTemporario = Path.Combine(Path.GetTempPath(), $"pericias-backup-{Guid.NewGuid():N}");
        var caminhoBanco = Path.Combine(diretorioTemporario, "pericias.db");
        var caminhoZip = Path.Combine(diretorioTemporario, nomeArquivo);
        Directory.CreateDirectory(diretorioTemporario);
        try
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                await using var destino = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = caminhoBanco,
                    Pooling = false
                }.ToString());
                await destino.OpenAsync();
                ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destino);
                await destino.CloseAsync();
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
            var usuarioId = UsuarioAtualId();
            await LimparCopiaParaContaAsync(caminhoBanco, usuarioId);

            using (var arquivoZip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
            {
                arquivoZip.CreateEntryFromFile(caminhoBanco, "pericias.db", CompressionLevel.Fastest);
                var quantidadeDocumentos = 0;
                var nomesDocumentos = await db.DocumentosPericia.AsNoTracking().Select(d => d.NomeArmazenado).ToListAsync();
                if (Directory.Exists(diretorioDocumentos))
                {
                    foreach (var nome in nomesDocumentos)
                    {
                        var documento = Path.Combine(diretorioDocumentos, Path.GetFileName(nome));
                        if (!System.IO.File.Exists(documento)) continue;
                        arquivoZip.CreateEntryFromFile(documento, $"documentos/{Path.GetFileName(documento)}", CompressionLevel.Fastest);
                        quantidadeDocumentos++;
                    }
                }
                var manifesto = arquivoZip.CreateEntry("manifesto.json", CompressionLevel.Fastest);
                using var writer = new StreamWriter(manifesto.Open(), new UTF8Encoding(false));
                writer.Write(System.Text.Json.JsonSerializer.Serialize(new ManifestoBackup("PericiaClara", 1, SqliteSchema.VersaoEsquema, DateTime.UtcNow, quantidadeDocumentos)));
            }

            HttpContext.Response.OnCompleted(() =>
            {
                try
                {
                    if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return Task.CompletedTask;
            });
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(caminhoZip, "application/zip", nomeArquivo);
        }
        catch
        {
            if (Directory.Exists(diretorioTemporario)) Directory.Delete(diretorioTemporario, recursive: true);
            throw;
        }
    }
}


