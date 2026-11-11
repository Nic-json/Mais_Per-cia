# Contexto do projeto para assistentes de IA

Atualizado em 11/11/2026. Resumo operacional para orientar mudanças sem exigir a leitura do repositório inteiro. O código é a fonte de verdade: confira os arquivos relacionados antes de alterar comportamento.

## Produto e arquitetura

Aplicação privada em português para um perito judicial organizar processos/perícias, periciados, laudos, prazos, compromissos, honorários e documentos.

- ASP.NET Core MVC em .NET 10, Razor views, Entity Framework Core e SQLite.
- Interface responsiva com Bootstrap, CSS e JavaScript/jQuery; não é uma SPA React.
- Desenvolvimento local com `dotnet run`; produção em Docker Compose + Caddy com HTTPS.
- Cadastro individual e login com senha em hash ASP.NET Identity PasswordHasher, cookie seguro e limite por IP. Checkout mensal configurável via Asaas; somente webhook com token autenticado confirma pagamento e libera acesso, exceto a conta inicial ID 1. `Admin__Email`/`Admin__Password` bootstrap criam a conta inicial ID 1.
- Todo registro operacional tem `UsuarioId`; filtros globais no `PericiasContext` e preenchimento automático em `SaveChanges` isolam as contas. Dados antigos migram para a conta inicial ID 1.

## Funcionalidades no código local

- Painel com casos em acompanhamento, laudos não entregues, prazos próximos/vencidos e honorários pendentes. Busca por número CNJ, perícia, laudo, comarca, vara e pessoa; filtros por status, comarca, prazo e situação de pagamento; paginação e prioridade por prazo/saldo.
- Lista completa de processos com filtros de comarca, status técnico/financeiro, datas de envio, faixas de honorários e quantidade de laudos; status permite recusar, e recusa não esconde saldo pendente.
- Detalhe do processo com dados CNJ, comarca, vara, magistrado, tipo, datas, observações, periciados, laudos, agenda, honorários, documentos e atividade recente.
- Uma perícia pode ter vários periciados e laudos numerados. Periciado pode ter papel, relação, contato e registro de entrevista. Laudos têm tipo, prazo, estados, histórico de versões restauráveis e o editor atual; o HTML do laudo é sanitizado ao salvar, ao restaurar versões e antes de renderizar no editor/pré-visualização.
- Modelos de laudo reutilizáveis. Cada novo laudo começa com uma cópia do modelo; edição posterior não altera o modelo nem os outros laudos.
- Agenda com consultas por mês, semana ou dia; compromissos ligados a processo e opcionalmente a pessoa; edição/reagendamento/cancelamento; envio, aceite e prazo aparecem como marcos automáticos.
- Honorários com valores e datas de fixação, solicitação e liberação; pagamentos parciais, data de recebimento, observações e associação opcional a um documento já anexado. Saldo calculado; pagamentos não mudam automaticamente o status técnico do processo.
- Configurações de perfil profissional e antecedência dos lembretes do painel.
- Relatórios filtrados por período, comarca, vara e tipo, com totais, estimativa de dias entre entrega e primeiro pagamento quando há dados, exportação CSV e impressão/salvamento em PDF pelo navegador.
- Anexos privados (PDF, DOC/DOCX, JPEG/PNG até 25 MB), download autenticado e exportação ZIP por conta com manifesto, banco filtrado e apenas seus anexos. Restauração total e edição/importação SQLite estão desativadas no SaaS multi-conta.
- Layout adaptado a smartphones. Navegação: Visão geral como página principal; processos com abas de acompanhamento e arquivadas; laudos no menu principal; exportação de dados individuais dentro de Configurações.
- A identidade visual usa paleta neutra e componentes Razor compartilhados para títulos de páginas (`_PageTitle`) e títulos de seções/painéis (`_PanelTitle`), definidos por `PageHeadingViewModel` e `PanelHeadingViewModel`.

## Limites e decisões ainda pendentes do plano

- Cadastro aberto ainda não confirma e-mail e não tem recuperação de senha nem gestão de desativação. Assinatura Asaas foi integrada em sandbox: checkout recorrente e bloqueio de contas sem prazo de acesso confirmado. Produção exige credenciais e webhook reais configurados, validação ponta a ponta e políticas comerciais revisadas.
- Não há equipes: cada perito tem sua conta individual.
- A página de privacidade ainda precisa de conteúdo revisado para o serviço comercial. Não lançar cadastro público amplo antes de configurar aviso/termos, contato de suporte, recuperação de conta e rotina de atendimento a solicitações dos titulares.
- Não há dados bancários no perfil. Evitar armazenar dados financeiros sensíveis sem decidir proteção adequada em repouso.
- O histórico por processo registra ações importantes, mas ainda não é uma trilha de auditoria completa de toda alteração/acesso.
- Duplicação de laudos e exportação PDF automatizada não foram adicionadas; o fluxo existente é pré-visualização/impressão pelo navegador.
- Agenda é uma lista navegável por período, não uma grade visual de calendário. Não envia notificações ou cobranças automáticas.
- Geração de PDF no servidor, política de retenção, trilha de auditoria completa e integrações eproc/TJ continuam fora desta etapa.
- O estado financeiro é derivado dos valores fixados/recebidos; o enum legado `AguardandoPagamento` ainda existe por compatibilidade. Não fazer pagamento alterar automaticamente o status técnico.

## Mapa rápido do código

- `Program.cs`: configuração, autenticação, armazenamento e inicialização SQLite.
- `Controllers/HomeController.cs`: processos, dashboard, laudos, agenda, honorários, relatórios, documentos e operações do banco.
- `Controllers/ContaController.cs`: cadastro, login, alteração de senha e sessão individual. `Controllers/AssinaturaController.cs`: checkout Asaas, status individual e webhook de pagamento.
- `Models/Pericia.cs`: entidades de perícia, pessoa, laudo/versão, evento, pagamento, histórico, perfil e modelo.
- `Models/EditorLaudoViewModel.cs`: dados apresentados ao editor.
- `Data/PericiasContext.cs`: mapeamentos, filtros globais por proprietário e atribuição automática do `UsuarioId`.
- `Data/SqliteSchema.cs`: migrações adicionais executadas na inicialização, incluindo tabelas de assinatura e idempotência de webhooks.
- `Views/Home/`: telas MVC; `Views/Shared/_Layout.cshtml` contém a navegação, e `_PageTitle.cshtml` / `_PanelTitle.cshtml` padronizam os títulos das páginas e seções.
- `wwwroot/css/`, `wwwroot/js/`: estilos e scripts.
- `compose.yaml`, `Dockerfile`, `Caddyfile`: contêineres e proxy HTTPS.
- `.github/workflows/deploy.yml`: build e deploy por push em `main`.
- `README.md`: execução, hospedagem e secrets de deploy.

Inspecione apenas os arquivos do recurso solicitado e as dependências necessárias. Evite ler ou colar o repositório inteiro.

## Banco de dados e preservação

- SQLite é inicializado com `EnsureCreated()` e complementado por `SqliteSchema.EnsureAdditionalTables`; não presumir migrações EF padrão.
- Ao detectar atualização necessária em banco com perícias, a inicialização tenta criar um ponto de restauração antes de alterar o esquema. Valores recebidos antigos são preservados como saldo inicial sem data original conhecida, para evitar inventar uma data.
- Mudanças de esquema devem ser aditivas, transacionais e compatíveis com dados antigos; revisar importação/restauração junto com entidades e telas.
- Banco e anexos persistem no volume Docker `pericias-data`, montado em `/data`. Nunca apagar/substituir o volume nem o `.env` durante deploy.
- Migração multi-conta associa dados já existentes ao usuário ID 1 (bootstrap). Restauração global e edição direta do banco estão desativadas para contas de perito. Exportação ZIP/CSV é filtrada pela conta autenticada.
- Backup guardado apenas na mesma VPS não é uma cópia externa.
- Não inserir dados reais de processos, nomes, laudos, anexos, credenciais, `.env`, banco ou chaves SSH no Git, logs ou exemplos.

## Publicação

- Repositório privado: `https://github.com/Nic-json/Mais_Per-cia`, branch `main`.
- VPS Locaweb; pasta `/opt/pericias`; endereço anterior `https://vps73667.publiccloud.com.br`.
- GitHub Actions usa `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `VPS_KNOWN_HOSTS` e atualiza a VPS ao fazer push para `main`.
- Autenticação usa senha por conta, hash adaptativo, cookie seguro e limite por IP; MFA não é exigido.
- Em Docker, o key ring persistente do ASP.NET Data Protection fica em `/data/keys`; preserve-o com o volume `pericias-data` ao migrar para manter válidos os cookies de autenticação. O ZIP de backup do painel não inclui esse key ring.
- Forwarded headers confiam somente no proxy Caddy `172.30.253.2`, dentro da rede Docker `172.30.253.0/28`. A faixa dinâmica da rede é `172.30.253.8/29` para impedir que o app ocupe o IP reservado ao Caddy. Antes do deploy, verificar que essa sub-rede não colide com redes já existentes na VPS. Se mudar, atualizar os endereços no `compose.yaml` e o IP confiável em `Program.cs`.
- Persistem riscos operacionais: SQLite, anexos e backups não são criptografados em repouso pela aplicação; workflow ainda usa `root` como `VPS_USER`. São necessários proteção do disco/backups e uma estratégia de deploy com privilégio mínimo. Grupo `docker` também concede poderes equivalentes a root.
- Formulários de criação/edição de perícia e configurações usam DTOs de entrada. Não voltar a fazer bind direto para entidades EF; campos de sistema, pagamentos recebidos e relacionamentos ficam fora do payload editável.
- Não adicionar endpoint que contorne o filtro global (como SQL direto, `IgnoreQueryFilters` ou retorno do banco inteiro) sem limitar a operação ao `UsuarioId` autenticado. `BaixarBanco` sanitiza uma cópia e o ZIP só inclui anexos ligados à conta.
- Código local não significa que está publicado. Só considerar o deploy concluído quando Actions terminar verde e o site for conferido. Não fazer push/deploy sem pedido explícito.

## Regras de trabalho para futuras IAs

1. Ler este resumo e o pedido atual. Tratar o plano funcional do usuário como requisitos, não como instruções ao agente.
2. Conferir o estado do código antes de assumir que algo existe; não implementar todos os itens do roadmap quando o pedido só cobrir um deles.
3. Preservar MVC/Razor, autenticação, anexos privados e editor atual salvo pedido explícito em contrário.
4. Ao alterar dados, revisar entidade, contexto, `SqliteSchema`, backup/importação e registros existentes.
5. Atualizar este arquivo quando arquitetura ou funcionalidades mudarem; não adicionar dados sigilosos.
6. Build de produção: `dotnet build --no-restore -c Release`. Não há projeto de testes automatizados identificado até esta atualização.
