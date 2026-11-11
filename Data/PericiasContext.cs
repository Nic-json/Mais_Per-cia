using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Data;

public class PericiasContext(DbContextOptions<PericiasContext> options) : DbContext(options)
{
    public DbSet<Pericia> Pericias => Set<Pericia>();
    public DbSet<DocumentoPericia> DocumentosPericia => Set<DocumentoPericia>();
    public DbSet<Periciado> Periciados => Set<Periciado>();
    public DbSet<LaudoPericia> LaudosPericia => Set<LaudoPericia>();
    public DbSet<EventoAgenda> EventosAgenda => Set<EventoAgenda>();
    public DbSet<PagamentoHonorario> PagamentosHonorarios => Set<PagamentoHonorario>();
    public DbSet<HistoricoPericia> HistoricoPericias => Set<HistoricoPericia>();
    public DbSet<ConfiguracaoPerito> ConfiguracoesPerito => Set<ConfiguracaoPerito>();
    public DbSet<ModeloLaudo> ModelosLaudo => Set<ModeloLaudo>();
    public DbSet<VersaoLaudo> VersoesLaudo => Set<VersaoLaudo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pericia>().HasIndex(p => p.Numero).IsUnique();
        modelBuilder.Entity<Pericia>().Property(p => p.ValorHonorarios).HasPrecision(12, 2);
        modelBuilder.Entity<Pericia>().Property(p => p.ValorHonorariosSolicitados).HasPrecision(12, 2);
        modelBuilder.Entity<Pericia>().Property(p => p.ValorHonorariosLiberados).HasPrecision(12, 2);
        modelBuilder.Entity<Pericia>().Property(p => p.ValorRecebido).HasPrecision(12, 2);
        modelBuilder.Entity<Pericia>().HasIndex(p => p.NumeroProcesso);
        modelBuilder.Entity<DocumentoPericia>(entity =>
        {
            entity.ToTable("DocumentosPericia");
            entity.HasKey(documento => documento.Id);
            entity.HasIndex(documento => documento.PericiaId);
            entity.HasOne(documento => documento.Pericia)
                .WithMany(pericia => pericia.Documentos)
                .HasForeignKey(documento => documento.PericiaId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Periciado>(entity =>
        {
            entity.ToTable("Periciados");
            entity.HasKey(periciado => periciado.Id);
            entity.Property(periciado => periciado.Nome).IsRequired().HasMaxLength(160);
            entity.Property(periciado => periciado.Papel).HasMaxLength(80);
            entity.Property(periciado => periciado.Relacao).HasMaxLength(120);
            entity.Property(periciado => periciado.Contato).HasMaxLength(200);
            entity.Property(periciado => periciado.RegistroEntrevista).HasMaxLength(4000);
            entity.HasIndex(periciado => periciado.PericiaId);
            entity.HasOne(periciado => periciado.Pericia)
                .WithMany(pericia => pericia.Periciados)
                .HasForeignKey(periciado => periciado.PericiaId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<LaudoPericia>(entity =>
        {
            entity.ToTable("LaudosPericia");
            entity.HasKey(laudo => laudo.Id);
            entity.Property(laudo => laudo.NumeroLaudo).IsRequired().HasMaxLength(80);
            entity.HasIndex(laudo => new { laudo.PericiaId, laudo.NumeroLaudo }).IsUnique();
            entity.Property(laudo => laudo.ConteudoHtml).IsRequired();
            entity.Property(laudo => laudo.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(laudo => laudo.Pericia)
                .WithMany(pericia => pericia.Laudos)
                .HasForeignKey(laudo => laudo.PericiaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(laudo => laudo.Periciado)
                .WithMany(periciado => periciado.Laudos)
                .HasForeignKey(laudo => laudo.PericiadoId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(laudo => laudo.Versoes).WithOne(versao => versao.Laudo).HasForeignKey(versao => versao.LaudoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<VersaoLaudo>(entity =>
        {
            entity.ToTable("VersoesLaudo");
            entity.Property(v => v.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(v => new { v.LaudoId, v.SalvoEm });
        });
        modelBuilder.Entity<EventoAgenda>(entity =>
        {
            entity.ToTable("EventosAgenda");
            entity.HasIndex(e => new { e.Inicio, e.Cancelado });
            entity.HasOne(e => e.Pericia).WithMany(p => p.EventosAgenda).HasForeignKey(e => e.PericiaId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Periciado).WithMany().HasForeignKey(e => e.PericiadoId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<PagamentoHonorario>(entity =>
        {
            entity.ToTable("PagamentosHonorarios");
            entity.Property(p => p.Valor).HasPrecision(12, 2);
            entity.HasIndex(p => new { p.PericiaId, p.DataRecebimento });
            entity.HasOne(p => p.Pericia).WithMany(p => p.Pagamentos).HasForeignKey(p => p.PericiaId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HistoricoPericia>(entity =>
        {
            entity.ToTable("HistoricoPericias");
            entity.HasIndex(h => new { h.PericiaId, h.CriadoEm });
            entity.HasOne(h => h.Pericia).WithMany(p => p.Historico).HasForeignKey(h => h.PericiaId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
