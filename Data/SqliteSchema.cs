using Microsoft.Data.Sqlite;

namespace Sistemas_para_pericias.Data;

public static class SqliteSchema
{
    public static void EnsureAdditionalTables(SqliteConnection connection, bool criarBackupAntesMigracao = true)
    {
        var precisaMigrar = !HasTable(connection, "Periciados") ||
            !HasColumn(connection, "LaudosPericia", "NumeroLaudo") ||
            !HasColumn(connection, "LaudosPericia", "PericiadoId");
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
