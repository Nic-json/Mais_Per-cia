using Microsoft.Data.Sqlite;

namespace Sistemas_para_pericias.Data;

public static class SqliteSchema
{
    public const int VersaoEsquema = 3;

    public static void EnsureAdditionalTables(SqliteConnection connection, bool criarBackupAntesMigracao = true)
    {
        var tinhaTabelaPagamentos = HasTable(connection, "PagamentosHonorarios");
        var precisaMigrar = !HasTable(connection, "Periciados") || !tinhaTabelaPagamentos || !HasTable(connection, "EventosAgenda") || !HasTable(connection, "HistoricoPericias") || !HasTable(connection, "ConfiguracoesPerito") || !HasTable(connection, "ModelosLaudo") || !HasTable(connection, "VersoesLaudo") ||
            !HasColumn(connection, "Pericias", "NumeroProcesso") || !HasColumn(connection, "Pericias", "DataHonorariosFixados") ||
            !HasColumn(connection, "Pericias", "ValorHonorariosSolicitados") || !HasColumn(connection, "Pericias", "DataHonorariosSolicitados") ||
            !HasColumn(connection, "Pericias", "ValorHonorariosLiberados") || !HasColumn(connection, "Pericias", "DataHonorariosLiberados") ||
            !HasColumn(connection, "Pericias", "Comarca") || !HasColumn(connection, "Pericias", "Vara") || !HasColumn(connection, "Pericias", "Magistrado") || !HasColumn(connection, "Pericias", "TipoPericia") ||
            !HasColumn(connection, "Periciados", "Papel") || !HasColumn(connection, "Periciados", "Relacao") || !HasColumn(connection, "Periciados", "Contato") || !HasColumn(connection, "Periciados", "RegistroEntrevista") ||
            !HasColumn(connection, "LaudosPericia", "NumeroLaudo") ||
            !HasColumn(connection, "LaudosPericia", "PericiadoId") || !HasColumn(connection, "LaudosPericia", "Status") || !HasColumn(connection, "LaudosPericia", "Tipo") || !HasColumn(connection, "LaudosPericia", "Prazo") || !HasColumn(connection, "LaudosPericia", "EntregueEm");
        if (criarBackupAntesMigracao && precisaMigrar && TemPericias(connection))
            CriarPontoRestauracao(connection);

        using var transaction = connection.BeginTransaction();
        var adicionouPericiadoId = false;
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Pericias_Numero"
                ON "Pericias" ("Numero");
            CREATE TABLE IF NOT EXISTS "DocumentosPericia" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DocumentosPericia" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "NomeOriginal" TEXT NOT NULL,
                "NomeArmazenado" TEXT NOT NULL,
                "TamanhoBytes" INTEGER NOT NULL,
                "EnviadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_DocumentosPericia_Pericias_PericiaId"
                    FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_DocumentosPericia_PericiaId"
                ON "DocumentosPericia" ("PericiaId");
            CREATE TABLE IF NOT EXISTS "Periciados" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Periciados" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "Nome" TEXT NOT NULL,
                CONSTRAINT "FK_Periciados_Pericias_PericiaId"
                    FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_Periciados_PericiaId"
                ON "Periciados" ("PericiaId");
            CREATE TABLE IF NOT EXISTS "LaudosPericia" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_LaudosPericia" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "NumeroLaudo" TEXT NOT NULL DEFAULT '1',
                "PericiadoId" INTEGER NULL,
                "ConteudoHtml" TEXT NOT NULL,
                "AtualizadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_LaudosPericia_Pericias_PericiaId"
                    FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_LaudosPericia_Periciados_PericiadoId"
                    FOREIGN KEY ("PericiadoId") REFERENCES "Periciados" ("Id") ON DELETE SET NULL
            );
            """);

        if (!HasColumn(connection, transaction, "LaudosPericia", "NumeroLaudo"))
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"NumeroLaudo\" TEXT NOT NULL DEFAULT '1';");
        if (!HasColumn(connection, transaction, "LaudosPericia", "PericiadoId"))
        {
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"PericiadoId\" INTEGER NULL REFERENCES \"Periciados\" (\"Id\") ON DELETE SET NULL;");
            adicionouPericiadoId = true;
        }

        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS "EventosAgenda" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_EventosAgenda" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "PericiadoId" INTEGER NULL,
                "Titulo" TEXT NOT NULL,
                "Inicio" TEXT NOT NULL,
                "Fim" TEXT NULL,
                "Local" TEXT NULL,
                "Modalidade" TEXT NULL,
                "Observacoes" TEXT NULL,
                "Cancelado" INTEGER NOT NULL DEFAULT 0,
                "CriadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_EventosAgenda_Pericias_PericiaId" FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_EventosAgenda_Periciados_PericiadoId" FOREIGN KEY ("PericiadoId") REFERENCES "Periciados" ("Id") ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_EventosAgenda_Inicio_Cancelado" ON "EventosAgenda" ("Inicio", "Cancelado");
            CREATE TABLE IF NOT EXISTS "PagamentosHonorarios" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PagamentosHonorarios" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "Valor" TEXT NOT NULL,
                "DataRecebimento" TEXT NULL,
                "DocumentoPericiaId" INTEGER NULL,
                "Observacoes" TEXT NULL,
                "CriadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_PagamentosHonorarios_Pericias_PericiaId" FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_PagamentosHonorarios_PericiaId_DataRecebimento" ON "PagamentosHonorarios" ("PericiaId", "DataRecebimento");
            CREATE TABLE IF NOT EXISTS "HistoricoPericias" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_HistoricoPericias" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "Tipo" TEXT NOT NULL,
                "Descricao" TEXT NOT NULL,
                "Responsavel" TEXT NULL,
                "CriadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_HistoricoPericias_Pericias_PericiaId" FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_HistoricoPericias_PericiaId_CriadoEm" ON "HistoricoPericias" ("PericiaId", "CriadoEm");
            CREATE TABLE IF NOT EXISTS "ConfiguracoesPerito" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ConfiguracoesPerito" PRIMARY KEY,
                "NomeProfissional" TEXT NULL,
                "Especialidade" TEXT NULL,
                "RegistroProfissional" TEXT NULL,
                "EmailContato" TEXT NULL,
                "Telefone" TEXT NULL,
                "AntecedenciaPrazoDias" INTEGER NOT NULL DEFAULT 7,
                "AvisarPrazosNoPainel" INTEGER NOT NULL DEFAULT 1,
                "AtualizadoEm" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "ModelosLaudo" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ModelosLaudo" PRIMARY KEY AUTOINCREMENT,
                "Nome" TEXT NOT NULL,
                "ConteudoHtml" TEXT NOT NULL,
                "AtualizadoEm" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "VersoesLaudo" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_VersoesLaudo" PRIMARY KEY AUTOINCREMENT,
                "LaudoId" INTEGER NOT NULL,
                "NumeroLaudo" TEXT NOT NULL,
                "Tipo" TEXT NULL,
                "Status" TEXT NOT NULL,
                "Prazo" TEXT NULL,
                "ConteudoHtml" TEXT NOT NULL,
                "Responsavel" TEXT NULL,
                "SalvoEm" TEXT NOT NULL,
                CONSTRAINT "FK_VersoesLaudo_LaudosPericia_LaudoId" FOREIGN KEY ("LaudoId") REFERENCES "LaudosPericia" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_VersoesLaudo_LaudoId_SalvoEm" ON "VersoesLaudo" ("LaudoId", "SalvoEm");
            """);

        if (!HasColumn(connection, transaction, "Periciados", "Papel"))
            Execute(connection, transaction, "ALTER TABLE \"Periciados\" ADD COLUMN \"Papel\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Periciados", "Relacao"))
            Execute(connection, transaction, "ALTER TABLE \"Periciados\" ADD COLUMN \"Relacao\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Periciados", "Contato"))
            Execute(connection, transaction, "ALTER TABLE \"Periciados\" ADD COLUMN \"Contato\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Periciados", "RegistroEntrevista"))
            Execute(connection, transaction, "ALTER TABLE \"Periciados\" ADD COLUMN \"RegistroEntrevista\" TEXT NULL;");

        if (!HasColumn(connection, transaction, "Pericias", "NumeroProcesso"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"NumeroProcesso\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "ValorHonorariosSolicitados"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"ValorHonorariosSolicitados\" TEXT NOT NULL DEFAULT '0';");
        if (!HasColumn(connection, transaction, "Pericias", "DataHonorariosFixados"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"DataHonorariosFixados\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "DataHonorariosSolicitados"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"DataHonorariosSolicitados\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "ValorHonorariosLiberados"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"ValorHonorariosLiberados\" TEXT NOT NULL DEFAULT '0';");
        if (!HasColumn(connection, transaction, "Pericias", "DataHonorariosLiberados"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"DataHonorariosLiberados\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "Comarca"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"Comarca\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "Vara"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"Vara\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "Magistrado"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"Magistrado\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "Pericias", "TipoPericia"))
            Execute(connection, transaction, "ALTER TABLE \"Pericias\" ADD COLUMN \"TipoPericia\" TEXT NULL;");
        Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS \"IX_Pericias_NumeroProcesso\" ON \"Pericias\" (\"NumeroProcesso\");");

        Execute(connection, transaction, """
            DROP INDEX IF EXISTS "IX_LaudosPericia_PericiaId";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LaudosPericia_PericiaId_NumeroLaudo"
                ON "LaudosPericia" ("PericiaId", "NumeroLaudo");
            CREATE INDEX IF NOT EXISTS "IX_LaudosPericia_PericiadoId"
                ON "LaudosPericia" ("PericiadoId");
            INSERT INTO "Periciados" ("PericiaId", "Nome")
                SELECT p."Id", trim(p."NomePericiado")
                FROM "Pericias" p
                WHERE trim(p."NomePericiado") <> ''
                  AND NOT EXISTS (SELECT 1 FROM "Periciados" pessoa WHERE pessoa."PericiaId" = p."Id");
            """);
        if (!HasColumn(connection, transaction, "LaudosPericia", "Tipo"))
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"Tipo\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "LaudosPericia", "Status"))
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'Rascunho';");
        if (!HasColumn(connection, transaction, "LaudosPericia", "Prazo"))
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"Prazo\" TEXT NULL;");
        if (!HasColumn(connection, transaction, "LaudosPericia", "EntregueEm"))
            Execute(connection, transaction, "ALTER TABLE \"LaudosPericia\" ADD COLUMN \"EntregueEm\" TEXT NULL;");

        Execute(connection, transaction, "UPDATE \"LaudosPericia\" SET \"Status\" = 'EmElaboracao' WHERE trim(\"ConteudoHtml\") <> '' AND \"Status\" = 'Rascunho';");

        // Preserve existing aggregates as opening-balance entries with an unknown historical receipt date.
        if (!tinhaTabelaPagamentos)
        {
            Execute(connection, transaction, """
                INSERT INTO "PagamentosHonorarios" ("PericiaId", "Valor", "DataRecebimento", "DocumentoPericiaId", "Observacoes", "CriadoEm")
                    SELECT "Id", "ValorRecebido", NULL, NULL, 'Valor recebido cadastrado antes do controle detalhado de pagamentos; data original não disponível.', "CriadaEm"
                    FROM "Pericias" WHERE "ValorRecebido" > 0;
                """);
        }
        if (adicionouPericiadoId)
            Execute(connection, transaction, """
                UPDATE "LaudosPericia"
                    SET "PericiadoId" = (
                        SELECT pessoa."Id" FROM "Periciados" pessoa
                        WHERE pessoa."PericiaId" = "LaudosPericia"."PericiaId"
                        ORDER BY pessoa."Id" LIMIT 1
                    )
                    WHERE "PericiadoId" IS NULL;
                """);
        transaction.Commit();
    }

    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool TemPericias(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM \"Pericias\" LIMIT 1);";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void CriarPontoRestauracao(SqliteConnection connection)
    {
        var caminhoAtual = Path.GetFullPath(connection.DataSource);
        var pastaRestauracao = Path.Combine(Path.GetDirectoryName(caminhoAtual)!, "backups-restauracao");
        Directory.CreateDirectory(pastaRestauracao);
        var caminhoBackup = Path.Combine(pastaRestauracao, $"antes-restauracao-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminhoBackup }.ToString()))
        {
            backup.Open();
            connection.BackupDatabase(backup);
        }

        foreach (var backupAntigo in Directory.EnumerateFiles(pastaRestauracao, "antes-restauracao-*.db")
                     .OrderByDescending(File.GetCreationTimeUtc).Skip(3))
            File.Delete(backupAntigo);
    }

    private static bool HasColumn(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
