# Perícia Clara

Aplicação ASP.NET Core MVC para organizar perícias, prazos, laudos e honorários. O banco SQLite fica em um volume persistente; login, backup completo do banco e exportação CSV já estão disponíveis.

## Executar localmente

Requisitos: .NET 10 SDK.

```powershell
$env:Admin__Email = "seu-email@exemplo.com"
$env:Admin__Password = "Defina-Uma-Senha-Longa-Com-16+Caracteres"
dotnet run
```

O banco é criado em `App_Data/pericias.db` na primeira execução. Não coloque a senha em `appsettings.json` nem compartilhe o arquivo `.env`.

## Publicar em uma VPS Linux

O pacote inclui Docker Compose e Caddy para publicar por HTTPS. A VPS precisa ter Ubuntu 24.04, Docker Engine/Compose, IP público e as portas 80 e 443 liberadas. O domínio precisa apontar um registro DNS `A` para o IP da VPS. O Caddy solicita e renova o certificado HTTPS automaticamente.

1. Contrate uma VPS Linux. Para o endereço, você pode usar um subdomínio gratuito do DuckDNS ou registrar um domínio `.br` por R$ 40/ano no Registro.br.
2. Instale Docker e Docker Compose na VPS e envie os arquivos deste projeto.
3. Copie `.env.example` para `.env` e preencha o subdomínio ou domínio, e-mail de acesso e uma senha longa.
4. Inicie a aplicação com `docker compose up -d --build`.
5. Abra `https://<seu-dominio>` e entre com a conta de administrador configurada.

O banco SQLite e os anexos ficam no volume Docker `pericias-data`, que sobrevive a atualizações da imagem. Use **Backup completo (.zip)** no painel para baixar o banco e os documentos anexados; guarde a cópia fora da VPS.

## Estimativa de hospedagem

A VPS Linux de entrada da Locaweb lista 1 GB de RAM, 2 vCPUs e 40 GB SSD. A oferta consultada exibe preço promocional equivalente a R$ 15,90/mês para contrato de 24 meses e renovação indicada de R$ 23,92/mês. O valor padrão exibido é R$ 29,90/mês. Confirme o ciclo de cobrança e o total na tela final antes de contratar. Um subdomínio DuckDNS é gratuito; um domínio `.br` custa R$ 40/ano, segundo o Registro.br.

Esse arranjo deixa aplicação e banco no mesmo servidor e cabe no teto mensal informado, sujeito à forma de pagamento escolhida. A VPS é autogerenciada: atualizações do sistema e cópias de segurança externas precisam ser mantidas.

## Recursos

- Painel com casos ativos, prazos vencidos, pagamentos e valores recebidos.
- Cadastro, edição, busca, filtros, status e exclusão.
- Login único de administrador com cookie seguro e limite de tentativas.
- Editor de laudo em texto livre, com formatação básica, rascunho salvo por perícia e pré-visualização para impressão em PDF.
- Edição externa do SQLite por exportação e importação autenticadas, com validação e ponto de restauração.
- Upload protegido de PDF, DOC/DOCX, JPG e PNG (até 25 MB por arquivo), download autenticado e backup completo em ZIP.

## Publicação automática pelo GitHub

O workflow `.github/workflows/deploy.yml` compila a aplicação e publica no VPS a cada envio para a branch `main`. Ele envia apenas os arquivos do projeto; o `.env`, o banco e os anexos ficam fora do pacote. O deploy usa SSH e executa `docker compose up -d --build` em `/opt/pericias`.

Configure o repositório como **privado** no GitHub e adicione estes Actions secrets em **Settings → Secrets and variables → Actions**:

- `VPS_HOST`: IP ou domínio SSH do VPS (atualmente `179.188.193.148`).
- `VPS_USER`: usuário SSH (atualmente `root`).
- `VPS_SSH_KEY`: chave privada dedicada para o deploy automático. Gere uma chave separada da sua chave pessoal e cadastre a chave pública correspondente em `~/.ssh/authorized_keys` desse usuário no VPS. Como o workflow não solicita senha interativamente, a chave privada deve ser sem passphrase e guardada somente como secret do GitHub.
- `VPS_KNOWN_HOSTS`: linha da chave de host SSH do VPS, obtida e conferida com a impressão digital do servidor antes de salvar. Não desative a conferência de host SSH.

Depois de criar o repositório vazio no GitHub, conecte-o a este repositório local e envie a branch `main`. A partir daí, cada push em `main` inicia uma publicação. O primeiro deploy automatizado deve ser acompanhado em **Actions** no GitHub e conferido no site.

Não envie ao GitHub o `.env`, arquivos `.db`, a pasta `App_Data`, documentos de clientes ou chaves SSH. O `.gitignore` já exclui esses itens.