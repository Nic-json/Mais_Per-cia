# Backup externo diário

O serviço cria um snapshot SQLite consistente com `.backup`, verifica a integridade e envia o banco, anexos legados e chaves do ASP.NET Data Protection para um repositório Restic em armazenamento S3 compatível. O Restic criptografa os dados antes do envio. Após cada backup, a rotina mantém 7 backups diários e 3 mensais e remove os snapshots antigos e os dados que não são mais referenciados.

## Configuração na VPS

1. Crie um bucket privado em armazenamento S3 compatível e uma chave de aplicação limitada a esse bucket, com permissão de listar, ler, gravar e excluir objetos. A exclusão é necessária para aplicar a retenção.
2. Gere uma senha forte exclusiva para o repositório Restic. Guarde-a também em um gerenciador de senhas fora da VPS: sem ela, não será possível descriptografar os backups.
3. Crie `/opt/pericias/backup.env` com os valores fornecidos pelo armazenamento:

   ```env
   RESTIC_REPOSITORY=s3:https://<endpoint-regional>/<nome-do-bucket>
   RESTIC_PASSWORD=<senha-forte-do-repositorio>
   AWS_ACCESS_KEY_ID=<id-da-chave-limitada-ao-bucket>
   AWS_SECRET_ACCESS_KEY=<segredo-da-chave>
   AWS_DEFAULT_REGION=<regiao-do-bucket>
   ```

4. Proteja o arquivo e inicialize o repositório Restic. Execute em `/opt/pericias`:

   ```bash
   chmod 600 /opt/pericias/backup.env
   docker compose --profile backup run --rm --build --no-deps --entrypoint restic backup init
   ```

5. Faça o primeiro backup manual e confira a saída:

   ```bash
   docker compose --profile backup run --rm --build --no-deps backup
   ```

6. Ative o timer do systemd:

   ```bash
   cp /opt/pericias/ops/backup/pericias-backup.service /etc/systemd/system/
   cp /opt/pericias/ops/backup/pericias-backup.timer /etc/systemd/system/
   systemctl daemon-reload
   systemctl enable --now pericias-backup.timer
   systemctl list-timers pericias-backup.timer
   ```

O serviço executa diariamente por volta de 02:15 no horário configurado na VPS. O log e falhas podem ser consultados com `journalctl -u pericias-backup.service`. O primeiro backup é obrigatório antes de habilitar o timer.

## Recuperação

Antes de substituir `/data` em produção, restaure para uma pasta temporária e confira o SQLite. Recupere juntos `pericias.db`, `uploads` e `keys`; não restaure ou apague o volume em produção sem um plano de retorno. Faça um teste de restauração antes de depender do backup para recuperação.

O arquivo `backup.env` não deve ser commitado, compartilhado ou incluído em imagens. Os backups contêm dados pessoais e processuais; limite o acesso ao bucket e à senha Restic.
