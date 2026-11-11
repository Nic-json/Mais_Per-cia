using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Data;

public class PericiasContext(DbContextOptions<PericiasContext> options) : DbContext(options)
{
    public DbSet<Pericia> Pericias => Set<Pericia>();
    public DbSet<DocumentoPericia> DocumentosPericia => Set<DocumentoPericia>();
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
        modelBuilder.Entity<LaudoPericia>(entity =>
        {
            entity.ToTable("LaudosPericia");
            entity.HasKey(laudo => laudo.Id);
            entity.HasIndex(laudo => laudo.PericiaId).IsUnique();
            entity.Property(laudo => laudo.ConteudoHtml).IsRequired();
            entity.HasOne(laudo => laudo.Pericia)
                .WithOne(pericia => pericia.Laudo)
                .HasForeignKey<LaudoPericia>(laudo => laudo.PericiaId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
