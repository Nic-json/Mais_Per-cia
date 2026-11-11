using Microsoft.Data.Sqlite;

namespace Sistemas_para_pericias.Data;

public static class SqliteSchema
{
    public static void EnsureAdditionalTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            CREATE TABLE IF NOT EXISTS "LaudosPericia" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_LaudosPericia" PRIMARY KEY AUTOINCREMENT,
                "PericiaId" INTEGER NOT NULL,
                "ConteudoHtml" TEXT NOT NULL,
                "AtualizadoEm" TEXT NOT NULL,
                CONSTRAINT "FK_LaudosPericia_Pericias_PericiaId"
                    FOREIGN KEY ("PericiaId") REFERENCES "Pericias" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LaudosPericia_PericiaId"
                ON "LaudosPericia" ("PericiaId");
            """;
        command.ExecuteNonQuery();
    }
}
