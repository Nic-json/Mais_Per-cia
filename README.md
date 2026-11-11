# Perícia Clara

Aplicação ASP.NET Core MVC para organizar perícias, prazos, laudos e honorários. Cada perito tem uma conta individual e os registros são isolados no banco SQLite compartilhado.

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
5. Abra `https://<seu-dominio>` e entre com a conta inicial configurada. Novos peritos podem criar contas individuais pela tela de login. As áreas internas são liberadas após pagamento confirmado.

O banco SQLite e os anexos ficam no volume Docker `pericias-data`, que sobrevive a atualizações da imagem. Cada conta pode exportar um ZIP com apenas seus registros e anexos. A restauração e edição direta do banco estão desativadas para contas de perito, pois o banco é compartilhado entre usuários. O operador do serviço precisa manter cópias externas do volume.

As chaves do ASP.NET Data Protection também ficam em `/data/keys` no volume persistente. Preserve-as junto com o banco ao migrar a VPS para manter válidos os cookies de autenticação durante atualizações. O backup ZIP do painel não inclui essas chaves. Banco, anexos e backups ainda não têm criptografia em repouso fornecida pela aplicação; proteja o disco e mantenha backups externos criptografados.

## Estimativa de hospedagem

A VPS Linux de entrada da Locaweb lista 1 GB de RAM, 2 vCPUs e 40 GB SSD. A oferta consultada exibe preço promocional equivalente a R$ 15,90/mês para contrato de 24 meses e renovação indicada de R$ 23,92/mês. O valor padrão exibido é R$ 29,90/mês. Confirme o ciclo de cobrança e o total na tela final antes de contratar. Um subdomínio DuckDNS é gratuito; um domínio `.br` custa R$ 40/ano, segundo o Registro.br.

Esse arranjo deixa aplicação e banco no mesmo servidor e cabe no teto mensal informado, sujeito à forma de pagamento escolhida. A VPS é autogerenciada: atualizações do sistema e cópias de segurança externas precisam ser mantidas.

## Recursos

- Painel com casos ativos, prazos vencidos, pagamentos e valores recebidos.
- Cadastro, edição, busca, filtros, status e exclusão.
- Cada perícia pode ter vários periciados e vários laudos com numeração própria; cada laudo pode ser associado a um periciado.
- Cadastro e login individuais, com hash de senha, cookie seguro, limite de tentativas e filtros de isolamento por conta.
- Editor de laudo em texto livre, com formatação básica, rascunho salvo por perícia e pré-visualização para impressão em PDF.
- Upload protegido de PDF, DOC/DOCX, JPG e PNG (até 25 MB por arquivo), download autenticado e exportação ZIP filtrada por conta.

## Situação comercial

O cadastro individual e o isolamento por conta são a base técnica para vários peritos. A integração de assinatura mensal com checkout Asaas está implementada em sandbox, com valor inicial de R$ 49,90 configurável e ativação baseada em webhook autenticado. Antes de cobrar clientes reais, configure credenciais e webhook de produção, valide todo o fluxo no sandbox e finalize confirmação de e-mail, recuperação/exclusão de conta, suporte, termos e aviso de privacidade.

## Módulos de processos e gestão

- Cadastro de processo com número CNJ, comarca, vara, magistrado e tipo de perícia; vários periciados com papel, relação, contato e registro de entrevista.
- Página de detalhe do processo com laudos, documentos, agenda, honorários e atividade recente.
- Central de laudos com estados, prazos, modelos reutilizáveis e edição no editor atual.
- Agenda navegável por mês, semana e dia, com compromissos e marcos de envio, aceite e prazo.
- Pagamentos parciais, saldo pendente, recibos ligados a documentos anexados e relatórios CSV/impressão para PDF por período/comarca/vara/tipo.
- Histórico restaurável de versões dos laudos e exportação ZIP com manifesto, dados e anexos da própria conta.
- Preferências de perfil e antecedência de lembretes exibidos no painel.

## Publicação automática pelo GitHub

O workflow `.github/workflows/deploy.yml` compila a aplicação e publica no VPS a cada envio para a branch `main`. Ele envia apenas os arquivos do projeto; o `.env`, o banco e os anexos ficam fora do pacote. O deploy usa SSH e executa `docker compose up -d --build` em `/opt/pericias`.

Configure o repositório como **privado** no GitHub e adicione estes Actions secrets em **Settings → Secrets and variables → Actions**:

- `VPS_HOST`: IP ou domínio SSH do VPS (atualmente `179.188.193.148`).
- `VPS_USER`: usuário SSH (atualmente `root`).
- O deploy SSH com `root` dá ao GitHub Actions acesso administrativo completo à VPS. Migre para uma conta dedicada e restrinja permissões; colocá-la no grupo `docker` ainda equivale, na prática, a acesso root.
- `VPS_SSH_KEY`: chave privada dedicada para o deploy automático. Gere uma chave separada da sua chave pessoal e cadastre a chave pública correspondente em `~/.ssh/authorized_keys` desse usuário no VPS. Como o workflow não solicita senha interativamente, a chave privada deve ser sem passphrase e guardada somente como secret do GitHub.
- `VPS_KNOWN_HOSTS`: linha da chave de host SSH do VPS, obtida e conferida com a impressão digital do servidor antes de salvar. Não desative a conferência de host SSH.

Depois de criar o repositório vazio no GitHub, conecte-o a este repositório local e envie a branch `main`. A partir daí, cada push em `main` inicia uma publicação. O primeiro deploy automatizado deve ser acompanhado em **Actions** no GitHub e conferido no site.

Não envie ao GitHub o `.env`, arquivos `.db`, a pasta `App_Data`, documentos de clientes ou chaves SSH. O `.gitignore` já exclui esses itens.

## Assinatura online (Asaas)

O checkout é criado no servidor e aceita Pix/cartão em cobrança recorrente. Configure no .env da VPS BILLING_MONTHLY_PRICE=49.90, ASAAS_API_KEY, ASAAS_WEBHOOK_TOKEN e ASAAS_BASE_URL. O padrão é https://api-sandbox.asaas.com/v3; só use o endpoint de produção depois dos testes e da liberação da conta Asaas. Nunca coloque chaves no Git, no navegador ou em mensagens.

Cadastre no painel Asaas um webhook para https://SEU-DOMINIO/webhooks/asaas, com um token de autenticação forte igual a ASAAS_WEBHOOK_TOKEN. Selecione eventos de checkout (CHECKOUT_PAID, CHECKOUT_CANCELED, CHECKOUT_EXPIRED) e assinatura/pagamentos (SUBSCRIPTION_CREATED, SUBSCRIPTION_UPDATED, SUBSCRIPTION_INACTIVATED, SUBSCRIPTION_DELETED, PAYMENT_CONFIRMED, PAYMENT_RECEIVED). O webhook deve enviar o token no cabeçalho asaas-access-token. O retorno do navegador não ativa assinatura: a confirmação chega pelo webhook. A conta administrativa inicial (ID 1), que já possui os dados legados, conserva acesso administrativo sem cobrança.
