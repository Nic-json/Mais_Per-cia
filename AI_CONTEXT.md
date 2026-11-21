# Contexto do projeto para assistentes de IA

Atualizado em 20/11/2026. Resumo operacional para orientar mudanças sem exigir a leitura do repositório inteiro. O código é a fonte de verdade: confira os arquivos relacionados antes de alterar comportamento.

## Produto e arquitetura

Perícia em Dia é uma aplicação privada em português para um perito judicial organizar processos/perícias, periciados, laudos, prazos, compromissos, honorários e documentos. A assinatura visual usa o descritor “Gestão pericial”.

- ASP.NET Core MVC em .NET 10, Razor views, Entity Framework Core e SQLite.
- Interface responsiva com Bootstrap, CSS e JavaScript/jQuery; não é uma SPA React.
- O arquivo `wwwroot/images/pericia-em-dia-mark.png` contém o símbolo original da marca e é usado no menu, telas públicas e como favicon.
- Desenvolvimento local com `dotnet run`; produção em Docker Compose + Caddy com HTTPS.
- Cadastro individual e login com senha em hash ASP.NET Identity PasswordHasher, cookie seguro e limite por IP. Novas contas confirmam o e-mail antes de entrar; links de confirmação expiram em 24 horas. Redefinição de senha usa tokens aleatórios de uso único, armazenados somente como hash, com validade de uma hora e resposta que não revela se o e-mail existe. SMTP é configurado por `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpUser`, `Email__SmtpPassword`, `Email__FromAddress`, `Email__FromName`, `Email__SupportAddress` e `Email__UseSsl`; o cadastro fica indisponível sem SMTP, contato de suporte e `Service__OperatorName`. A migração SQLite adiciona `EmailConfirmado` com default `true` para preservar as contas existentes e cria a tabela de tokens. Checkout mensal recorrente somente por cartão de crédito, configurável via Asaas; a primeira cobrança vence no dia da contratação. O usuário pode cancelar a própria recorrência pela página Assinatura; o sistema só confirma após resposta do Asaas, mantém o acesso até o fim do período pago e informa que cobranças pendentes/vencidas vinculadas podem ser removidas pelo provedor. A tela de assinatura separa os estados ativa, checkout pendente, cancelada e indisponibilidade; checkout pendente retoma o link salvo. Erro da Asaas por restrição cadastral da conta recebedora recebe orientação específica na interface, sem expor a resposta bruta da API. Um checkout recorrente aberto é reutilizado para evitar cobranças duplicadas. O retorno do checkout informa que a confirmação é assíncrona; somente webhook autenticado confirma pagamento e libera acesso, exceto a conta inicial ID 1. Antes de gerar cobrança, a aplicação consulta e provisiona o webhook Asaas. O link retornado pelo Asaas é validado e persistido por conta; links antigos sem valor salvo usam fallback por ambiente. `Public__BaseUrl` em produção define callbacks HTTPS usando `APP_DOMAIN`. Webhooks associam eventos de pagamento ao checkout usando `payment.checkoutSession` quando o corpo não tem objeto `checkout`. Eventos de confirmação, atraso, recusa, cancelamento, expiração, estorno e chargeback são tratados com idempotência por ID; assinatura criada/alterada sem pagamento confirmado não libera acesso. Os dados do produto enviados ao Asaas usam a marca Perícia em Dia. O preço padrão da assinatura é R$ 70,00, configurável por `Billing__MonthlyPrice` / `BILLING_MONTHLY_PRICE`; o valor de assinaturas existentes no Asaas não é alterado automaticamente. `Admin__Email`/`Admin__Password` bootstrap criam a conta inicial ID 1.
- Todo registro operacional tem `UsuarioId`; filtros globais no `PericiasContext` e preenchimento automático em `SaveChanges` isolam as contas. Dados antigos migram para a conta inicial ID 1.

## Funcionalidades no código local

- Painel com casos em acompanhamento, laudos não entregues, prazos próximos/vencidos e honorários a receber. O total já recebido fica nas áreas de Honorários e Relatórios. Busca por número da perícia, laudo, comarca, vara e pessoa; filtros por status, comarca, prazo e situação de pagamento; paginação e prioridade por prazo/saldo.
- Lista completa de processos com filtros de comarca, status técnico/financeiro, datas de envio, faixas de honorários e quantidade de laudos; status permite recusar, e recusa não esconde saldo pendente.
- Detalhe da perícia com número da perícia como identificador principal, comarca, vara, tipo, datas, observações, periciados, laudos, agenda, honorários e atividade recente. Periciados são somente leitura nessa tela e só podem ser gerenciados em Editar. As colunas legadas de CNJ e magistrado permanecem no SQLite por compatibilidade; os campos não são editados, buscados, exibidos nem exportados.
- Na tela de detalhe do processo, a seção de laudos é somente informativa: número, periciado, status e prazo são exibidos sem ações de edição ou navegação para edição. Alterações continuam disponíveis nas telas próprias de Laudos e cadastro do processo.
- Uma perícia pode ter zero ou vários periciados e zero ou vários laudos numerados automaticamente em sequência por usuário; não existe periciado principal. No cadastro, periciados são adicionados individualmente por botão, com nome, telefone e observações. O telefone e as observações usam as colunas legadas `Contato` e `RegistroEntrevista`, preservando o banco existente; as propriedades antigas `Papel` e `Relacao` permanecem por compatibilidade, mas não são editadas neste formulário. Laudos têm tipo, prazo, estados, histórico de versões restauráveis e o editor visual em página A4, com correção ortográfica do navegador, fontes, estilos de parágrafo, alinhamento, listas, cores e realce, links, tabelas e espaçamento entre linhas; o prazo do laudo não é editado no editor. O HTML do laudo é sanitizado ao salvar, ao restaurar versões e antes de renderizar no editor/pré-visualização.
- Modelos de laudo reutilizáveis. Cada novo laudo começa com uma cópia do modelo; edição posterior não altera o modelo nem os outros laudos.
- Agenda com visualização em calendário nos modos mês, semana ou dia; cadastro de compromisso por modal no cabeçalho, compromissos ligados a processo e opcionalmente a pessoa; edição/reagendamento/cancelamento; envio, aceite, prazo e entrega de laudo aparecem como marcos automáticos dentro das datas.
- Honorários com valores e datas de fixação, solicitação e liberação; pagamentos parciais, data de recebimento e observações. Campos de honorários, filtros de valor e registro de pagamento exibem máscara monetária brasileira e enviam decimais para o servidor. O formulário atual não oferece comprovantes; associações antigas a documentos continuam preservadas no banco. Saldo calculado; pagamentos não mudam automaticamente o status técnico do processo.
- A tela de Honorários usa cartões responsivos de resumo, filtros alinhados em grade e tabela de largura fixa com quebra segura de nomes e pagamentos. No detalhe do processo, as ações de registrar ou atualizar a data do SEI e registrar pagamento são feitas por modais reutilizáveis acionadas por botões; o histórico de pagamentos continua visível. A seção Histórico lista eventos registrados automaticamente, com data, hora e responsável; não aceita atualizações textuais manuais.
- Configurações de perfil profissional e antecedência dos lembretes do painel. A especialidade do perfil define automaticamente o tipo nas novas perícias; o tipo não aparece no formulário de cadastro/edição e é preservado em perícias existentes. O cadastro do TJRS inicia com a relação histórica oficial de foros/códigos de 2009; cada conta pode adicionar ou corrigir códigos próprios. Números completos no padrão CNJ do TJRS (8.21) preenchem a comarca quando o código OOOO é conhecido; código ausente/ambíguo não impede salvar e a comarca pode ser informada manualmente.
- Relatórios filtrados por período, comarca, vara e tipo, com totais, estimativa de dias entre entrega e primeiro pagamento quando há dados, exportação CSV e impressão/salvamento em PDF pelo navegador.
- Novos anexos foram desativados temporariamente. Documentos já armazenados continuam preservados no armazenamento e incluídos na exportação ZIP por conta; o arquivo por conta remove registros operacionais e assinaturas de outros usuários, remove hashes de senha e omite eventos globais de webhook. Os documentos não são exibidos na interface; não remover dados ou arquivos existentes. Restauração total e edição/importação SQLite estão desativadas no SaaS multi-conta.
- A Central de laudos lista todas as perícias da conta, inclusive as que ainda não têm laudos, com busca e filtros agregados. A ação é sempre “Abrir laudos” e leva à página dedicada daquela perícia, onde se pode criar o primeiro laudo ou gerenciar os existentes, consultar status e abrir o editor. A tabela dessa página mostra tipo, periciado, status e ações, sem exibir o número interno do laudo, prazo ou última atualização. A Central não abre as especificações do processo, e o formulário de especificações não gerencia laudos.
- A rota inicial apresenta uma landing page pública responsiva com apresentação, fluxo de uso, recursos existentes, painel ilustrativo, preço de R$ 70, FAQ e aviso temporário de que novas assinaturas aguardam a ativação do pagamento. Os CTAs públicos direcionam para recursos ou login; não há depoimentos ou afirmações de integrações/IA que o produto não oferece. Para usuários autenticados e liberados, a mesma rota continua exibindo a Visão geral; usuários sem assinatura ativa seguem para a tela de assinatura. A landing page não consulta nem exibe dados operacionais.
- Layout adaptado a smartphones. Navegação autenticada: Visão geral como página principal; processos com abas de acompanhamento e arquivadas; laudos no menu principal; exportação de dados individuais dentro de Configurações.
- As tabelas de Visão geral e Processos usam largura fixa com quebra de nomes e conteúdo nas células; em smartphone elas se ajustam à largura disponível sem rolagem horizontal.
- Os filtros da Visão geral usam uma grade responsiva: busca com espaço maior, seletores e datas alinhados, ações agrupadas na última coluna.
- Os paginadores de Visão geral e Processos permitem escolher 10, 20, 25, 50 ou 100 itens por página em um seletor sem rótulo visível; filtros e quantidade selecionada são preservados na navegação. O paginador não é exibido quando a lista está vazia; em Arquivadas, a ação de criar processo não aparece no cabeçalho.
- O rodapé compartilhado da tabela alinha o seletor de itens à esquerda e até cinco páginas à direita, sem resumo textual; acompanha a rolagem no limite inferior da tela enquanto o card está visível.
- Ações em links de tabela e limpeza de filtros usam estilo de botão compartilhado com contorno; ações de busca usam o rótulo “Pesquisar”. Variantes primária/secundária conservam a hierarquia visual.
- Cadastro e edição de perícia organizam cada campo em uma linha, com rótulo à esquerda e controle à direita; validações e ajudas ficam abaixo do controle. Em telas estreitas, o rótulo passa para cima.
- A área de periciados usa uma linha de largura completa: título/ajuda à esquerda e botão de adicionar à direita.
- No cadastro/edição, o bloco financeiro mantém honorários, status e o resumo dos recebimentos; a data do SEI (campo legado `DataEnvio`) é registrada separadamente na seção Honorários do detalhe do processo. Valores e datas históricos de honorários solicitados/liberados continuam preservados, mas não são editáveis nesse formulário; pagamentos também são geridos na seção Honorários do processo.
- A identidade visual usa superfícies em azul pastel bem claro; campos editáveis permanecem brancos para se distinguirem. A interface usa fontes nativas do sistema e escala tipográfica ampliada para facilitar a leitura, sem interferir na formatação escolhida dentro do editor de laudos; em telas maiores, a área principal pode ocupar até 1680px. O teal fica reservado a ações, links, navegação selecionada e foco dos campos. A identificação do perito aparece apenas no rodapé da navegação; o topo mantém a data sem repetir o avatar; componentes Razor compartilhados para títulos de páginas (`_PageTitle`), títulos de seções/painéis (`_PanelTitle`) e estrutura de modais (`_AppModal`), definida por `PageHeadingViewModel`, `PanelHeadingViewModel` e `AppModalViewModel`. Os seletores das páginas usam um dropdown compartilhado com pesquisa, lista rolável e suporte a teclado; os valores continuam sendo enviados pelos elementos de formulário existentes. A comarca tem sugestões customizadas e aceita texto manual.
- Ações destrutivas e restaurações usam uma modal de confirmação própria, compartilhada no layout e acionada por `data-confirm-message`; as confirmações não dependem do diálogo nativo do navegador.
- Datas correntes da interface, filtros de prazo e registros de data/hora são apresentados segundo o fuso America/Sao_Paulo (Brasília), independentemente do fuso configurado no servidor; timestamps persistidos continuam em UTC.

## Limites e decisões ainda pendentes do plano

- O fluxo de cadastro, recuperação de senha e aviso de inatividade requer SMTP autenticado na VPS. A rotina diária em `Services/AccountRetentionWorker.cs` envia aviso após 11 meses sem login e exclui conta/dados após 12 meses somente se o aviso tiver sido enviado há pelo menos 30 dias, a assinatura não estiver ativa e não houver período pago vigente; falha de e-mail preserva os dados. Login renova o último acesso; conta administrativa ID 1 é isenta. Backups locais de restauração com mais de 30 dias são limpos diariamente; cópias externas/manual precisam de política própria. Cancelar assinatura não remove dados. Assinatura Asaas tem checkout recorrente e processamento de webhook implementados; a integração externa ainda precisa ser validada com credenciais de produção e eventos reais de teste.
- Não há equipes: cada perito tem sua conta individual.
- A página pública de privacidade mostra o operador e o canal vindos da configuração e descreve dados, fornecedores e retenção. Fornecedores em uso: Locaweb (VPS), Zoho Mail (SMTP), Asaas (pagamentos), Backblaze B2 com Restic (backup); confirmar regiões, subprocessadores e termos contratuais antes de publicar afirmações específicas. Antes do cadastro comercial, conferir `Service__OperatorName`/`Email__SupportAddress` na VPS, testar aviso de retenção e revisar política e termos com assessoria apropriada. `PRIVACIDADE_E_OPERACAO.md` registra inventário, rotina e limites de backup. O cancelamento da assinatura não apaga dados.
- Não há dados bancários no perfil. Evitar armazenar dados financeiros sensíveis sem decidir proteção adequada em repouso.
- O histórico por processo registra ações importantes, mas ainda não é uma trilha de auditoria completa de toda alteração/acesso.
- Duplicação de laudos e exportação PDF automatizada não foram adicionadas; o fluxo existente é pré-visualização/impressão pelo navegador.
- A Agenda não envia notificações ou cobranças automáticas.
- Geração de PDF no servidor, política de retenção, trilha de auditoria completa e integrações eproc/TJ continuam fora desta etapa.
- O estado financeiro é derivado dos valores fixados/recebidos; o enum legado `AguardandoPagamento` ainda existe por compatibilidade. Não fazer pagamento alterar automaticamente o status técnico.

## Mapa rápido do código

- `Program.cs`: configuração, autenticação, armazenamento e inicialização SQLite.
- `Controllers/HomeController.cs`: landing page pública, processos, dashboard, laudos, agenda, honorários, relatórios, documentos e operações do banco.
- `Controllers/ContaController.cs`: cadastro, confirmação de e-mail, login, alteração e redefinição de senha e sessão individual. `Services/SmtpAccountEmailSender.cs`: envio de mensagens transacionais via SMTP. `Controllers/AssinaturaController.cs`: checkout Asaas, status individual e webhook de pagamento.
- `Models/Pericia.cs`: entidades de perícia, pessoa, laudo/versão, evento, pagamento, histórico, perfil e modelo.
- `Models/EditorLaudoViewModel.cs`: dados apresentados ao editor.
- `Data/PericiasContext.cs`: mapeamentos, filtros globais por proprietário e atribuição automática do `UsuarioId`.
- `Data/SqliteSchema.cs`: migrações adicionais executadas na inicialização, incluindo tabelas de assinatura e idempotência de webhooks.
- `Views/Home/`: telas MVC, incluindo `Landing.cshtml` pública; `Views/Shared/_Layout.cshtml` contém a navegação autenticada, e `_PageTitle.cshtml` / `_PanelTitle.cshtml` padronizam os títulos das páginas e seções.
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

## Backup externo

- `ops/backup/` implementa um backup externo diário usando Restic, em contêiner Docker, para Backblaze B2 via API S3. O contêiner executa `.backup` online do SQLite, verifica `PRAGMA integrity_check` e inclui `/data/uploads` e `/data/keys` quando existem.
- Produção está configurada com bucket privado `periciaemdia-backups-nicolas`, região `us-east-005` e endpoint `s3.us-east-005.backblazeb2.com`. Restic criptografa os dados antes do envio. O nome do bucket e a região são informação operacional; IDs, segredos da chave e senha Restic nunca devem entrar no Git.
- Retenção: 7 diários e 3 mensais, aplicada após cada execução com `restic forget --prune`. O bucket B2 está configurado para manter somente a última versão de cada objeto, permitindo que a limpeza do Restic reduza o armazenamento.
- VPS já foi configurada: `/opt/pericias/backup.env` protegido com permissões restritas, repositório Restic inicializado, backup manual concluído com sucesso e timer `pericias-backup.timer` habilitado. Execução automática foi agendada diariamente por volta de 02:15, com atraso aleatório de até 15 minutos; confirmar a primeira execução automática nos logs.
- Diagnóstico de horário em 21/11/2026: fuso `America/Sao_Paulo`; `systemd-timesyncd` ativo, mas sem sincronização, com timeouts ao consultar `ntp.ubuntu.com`; UFW inativo. A sincronização NTP não impediu o backup manual; verificar a primeira execução agendada e solicitar à Locaweb análise de tráfego NTP de saída UDP/123 se os timeouts persistirem.
- Consultar execução com `journalctl -u pericias-backup.service -n 30 --no-pager` e agendamento com `systemctl list-timers --all pericias-backup.timer`. O backup manual foi validado e um snapshot foi restaurado em pasta temporária; `PRAGMA integrity_check` retornou `ok`. O teste não substituiu dados de produção. A senha de descriptografia precisa ser guardada fora da VPS.
- Ver instruções de instalação e operação em `ops/backup/README.md`. Restaurar DB, uploads e Data Protection keys juntos; sempre ensaiar a restauração antes de depender dela.

## Termos públicos e cobrança

- `Views/Home/Terms.cshtml` publica os Termos de Uso; `HomeController.Terms` fornece operador, identificação legal, CPF/CNPJ, endereço, suporte e preço atual (`Billing:MonthlyPrice`, fallback R$ 70). Rotas públicas anônimas: `/termos` e `/privacidade`. Configure `Service__LegalName`, `Service__TaxId` e `Service__BusinessAddress` no ambiente para completar a identificação comercial; enquanto faltarem, a página de termos avisa que as vendas não devem ser abertas.
- A landing page, o cadastro individual e a tela Assinatura apontam para Termos e Privacidade. `Views/Home/Privacy.cshtml` é pública via `HomeController.Privacy` e também está ligada no rodapé da landing.
- Os termos refletem o comportamento atual: mensalidade recorrente no cartão via Asaas, liberação após webhook confirmado, cancelamento solicitado na conta e efetivado após confirmação do Asaas, acesso até o fim do período pago, sem exclusão automática dos dados pelo cancelamento. Direitos de consumo aplicáveis são preservados.
- Conteúdo preparado como rascunho operacional, não como parecer jurídico. Antes de abrir vendas, confirmar identificação legal do operador, contato monitorado, preço/condições no Asaas e revisar os termos e a política com assessoria jurídica.
