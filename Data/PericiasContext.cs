using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Security;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Data;

public class PericiasContext(DbContextOptions<PericiasContext> options, ICurrentUser currentUser) : DbContext(options)
{
    private int CurrentUserId => currentUser.Id;

    public DbSet<ContaUsuario> ContasUsuarios => Set<ContaUsuario>();
    public DbSet<AssinaturaConta> Assinaturas => Set<AssinaturaConta>();
    public DbSet<EventoAsaas> EventosAsaas => Set<EventoAsaas>();
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

    public override int SaveChanges()
    {
        AplicarUsuarioAtual();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AplicarUsuarioAtual();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AplicarUsuarioAtual()
    {
        foreach (var entry in ChangeTracker.Entries<IUsuarioDono>().Where(entry => entry.State == EntityState.Added))
        {
            if (CurrentUserId <= 0)
                throw new InvalidOperationException("A conta autenticada não foi identificada para salvar estes dados.");
            entry.Entity.UsuarioId = CurrentUserId;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pericia>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<DocumentoPericia>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<Periciado>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<LaudoPericia>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<VersaoLaudo>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<EventoAgenda>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<PagamentoHonorario>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<HistoricoPericia>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<ConfiguracaoPerito>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<ModeloLaudo>().Property(e => e.UsuarioId).HasDefaultValue(1);
        modelBuilder.Entity<ContaUsuario>().HasIndex(u => u.EmailNormalizado).IsUnique();
        modelBuilder.Entity<ContaUsuario>().ToTable("ContasUsuarios");
        modelBuilder.Entity<AssinaturaConta>().ToTable("AssinaturasContas").HasIndex(s => s.UsuarioId).IsUnique();
        modelBuilder.Entity<EventoAsaas>().ToTable("EventosAsaas");
        modelBuilder.Entity<Pericia>().HasIndex(p => new { p.UsuarioId, p.Numero }).IsUnique();
        modelBuilder.Entity<DocumentoPericia>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<Periciado>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<LaudoPericia>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<VersaoLaudo>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<EventoAgenda>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<PagamentoHonorario>().HasIndex(e => e.UsuarioId);
        modelBuilder.Entity<HistoricoPericia>().HasIndex(e => e.UsuarioId);
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

        modelBuilder.Entity<ConfiguracaoPerito>().HasIndex(c => c.UsuarioId).IsUnique();
        modelBuilder.Entity<ModeloLaudo>().HasIndex(m => new { m.UsuarioId, m.Nome });

        modelBuilder.Entity<Pericia>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<DocumentoPericia>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<Periciado>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<LaudoPericia>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<VersaoLaudo>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<EventoAgenda>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<PagamentoHonorario>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<HistoricoPericia>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<ConfiguracaoPerito>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
        modelBuilder.Entity<ModeloLaudo>().HasQueryFilter(e => e.UsuarioId == CurrentUserId);
    }
}
