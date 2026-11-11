using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Data;

public class PericiasContext(DbContextOptions<PericiasContext> options) : DbContext(options)
{
    public DbSet<Pericia> Pericias => Set<Pericia>();
    public DbSet<DocumentoPericia> DocumentosPericia => Set<DocumentoPericia>();
    public DbSet<Periciado> Periciados => Set<Periciado>();
    public DbSet<LaudoPericia> LaudosPericia => Set<LaudoPericia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pericia>().HasIndex(p => p.Numero).IsUnique();
        modelBuilder.Entity<Pericia>().Property(p => p.ValorHonorarios).HasPrecision(12, 2);
        modelBuilder.Entity<Pericia>().Property(p => p.ValorRecebido).HasPrecision(12, 2);
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
            entity.HasOne(laudo => laudo.Pericia)
                .WithMany(pericia => pericia.Laudos)
                .HasForeignKey(laudo => laudo.PericiaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(laudo => laudo.Periciado)
                .WithMany(periciado => periciado.Laudos)
                .HasForeignKey(laudo => laudo.PericiadoId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
