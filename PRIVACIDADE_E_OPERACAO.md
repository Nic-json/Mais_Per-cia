# Privacidade e operação — Perícia em Dia

Documento de trabalho para operar o serviço. Não substitui revisão jurídica. A página pública `Views/Home/Privacy.cshtml` deve sempre refletir os fornecedores, configurações e procedimentos realmente usados.

## 1. Papéis e limites

- Para informações de processos, laudos e periciados, o perito define a finalidade do uso e é, em regra, o principal responsável por esse tratamento. O Perícia em Dia fornece a ferramenta e deve tratar esses dados conforme o serviço e as instruções aplicáveis. A classificação jurídica pode variar conforme as decisões efetivamente tomadas; não anunciar papéis absolutos sem revisar contratos e operação.
- Para cadastro, autenticação, cobrança da própria assinatura, suporte e segurança do serviço, o operador do Perícia em Dia decide a finalidade e os meios necessários.
- O perito é responsável por informar titulares quando necessário, usar apenas dados pertinentes, observar sigilo judicial/profissional e responder a pedidos referentes aos casos sob sua responsabilidade.
- O operador do produto responde pelos dados da conta e do serviço, protege a infraestrutura, mantém canal de contato e encaminha ao perito pedidos sobre os dados do caso.

## 2. Inventário conhecido

| Dados | Para que são usados | Destino conhecido |
|---|---|---|
| Nome, e-mail, hash da senha e confirmação de e-mail | Criar e proteger a conta, autenticar e recuperar acesso | SQLite na VPS; SMTP para mensagens transacionais |
| Processos, periciados, contatos, observações, laudos, agenda, honorários e histórico | Organização do trabalho do perito | SQLite e armazenamento persistente da VPS |
| Estado e identificadores da assinatura/checkout/cliente | Cobrança recorrente, suporte e controle de acesso | SQLite; Asaas para pagamento e webhooks |
| Dados do cartão | Processar a cobrança | Informados diretamente ao Asaas; o aplicativo não os recebe nem guarda |
| Sessão e dados técnicos mínimos | Manter login, proteger e diagnosticar o serviço | Navegador e infraestrutura do serviço |

Fornecedores em uso: Locaweb (VPS), Zoho Mail (SMTP), Asaas (pagamentos) e Backblaze B2 (backups externos via Restic). Registrar e confirmar nome jurídico, região de processamento, subprocessadores, retenção, suporte e condições contratuais de cada fornecedor antes da abertura comercial. Não afirmar que os dados ficam exclusivamente no Brasil sem validar cada fornecedor.

## 3. Controles existentes e limitações

- HTTPS via Caddy; cookie de sessão `HttpOnly`, `Secure`, `SameSite=Lax`; senha com hash adaptativo; limite de tentativas por IP.
- Separação dos dados operacionais por `UsuarioId` e filtros por conta. O administrador inicial ID 1 é uma conta diferenciada e tem acesso apenas aos dados associados à própria conta segundo os filtros do aplicativo.
- Confirmação de e-mail para contas novas; tokens de confirmação e redefinição são aleatórios, de uso único, com hash persistido e validade limitada.
- Webhook Asaas autenticado e idempotente; o estado retornado pelo navegador não libera assinatura.
- SQLite e arquivos na VPS não têm criptografia em repouso fornecida pelo aplicativo. Backups externos são criptografados pelo Restic antes de serem enviados ao Backblaze B2; credenciais e senha de restauração ficam fora do Git. A VPS, o volume Docker, o acesso SSH e cópias baixadas manualmente precisam de controles do operador.
- Não existe trilha de auditoria completa, canal dentro do produto para pedidos de titulares nem exclusão self-service. Anexos novos estão desativados; arquivos legados permanecem no armazenamento até a exclusão da conta.
- O ZIP de exportação contém dados operacionais da conta. O processo remove as contas e tokens de outras pessoas, além de hashes de senha e eventos globais de webhook.

## 4. Política de retenção implementada

O aplicativo registra a data do último login e executa uma rotina diária de retenção. A conta administrativa inicial (ID 1) é preservada.

1. **Aviso:** após 11 meses sem login, o serviço tenta enviar aviso ao e-mail cadastrado. Se o SMTP estiver indisponível, o envio falhar ou o aviso ainda não tiver sido enviado, a conta não é excluída.
2. **Exclusão:** a partir de 12 meses sem login, e somente depois de transcorridos pelo menos 30 dias do aviso enviado com sucesso, a rotina remove a conta e seus registros operacionais e arquivos legados. Um login renova a data de atividade e cancela o aviso pendente.
3. **Assinatura:** contas com status `Ativa` ou com período de acesso pago ainda vigente ficam fora da exclusão. Cancelar a assinatura, por si só, não apaga dados.
4. **Contas existentes:** na primeira migração, a contagem começa na data em que a nova regra entra em vigor, sem considerar a idade anterior da conta. A rotina envia aviso antes de qualquer exclusão; não há apagamento imediato por inatividade anterior ao deploy.
5. **Backups:** backups locais de restauração com mais de 30 dias são removidos diariamente. Uma cópia local recente pode manter os dados até vencer esse prazo. Backups externos e cópias baixadas manualmente seguem o prazo e o controle de quem os opera; ao restaurar uma cópia, execute novamente a política de retenção antes de reabrir o serviço.
6. **Pedidos de encerramento/exclusão:** ainda devem ser tratados pelo suporte com verificação de identidade, estado da assinatura e eventuais obrigações de preservação. A rotina de inatividade não substitui o atendimento a solicitações de titulares.
7. **Suporte:** usar o e-mail definido em `EMAIL_SUPPORT_ADDRESS`; registrar data, conta, categoria, responsável e conclusão sem copiar conteúdo de laudo para tickets desnecessariamente.

## 5. Atendimento a solicitações

1. Receber o pedido no e-mail de privacidade configurado e registrar a data de recebimento.
2. Confirmar a identidade por canal já associado à conta. Não pedir senha, token, número completo de cartão ou documentos excessivos por e-mail.
3. Identificar a conta e separar dados do serviço daqueles inseridos pelo perito. Quando o pedido envolver conteúdo de processo/periciado, encaminhar ao perito responsável sem revelar dados de outras contas.
4. Avaliar o pedido, retenções e efeitos sobre processo judicial, faturamento e backups. Executar somente ações autorizadas e deixar registro da decisão.
5. Responder pelo mesmo canal com o resultado, eventuais limites e próximos passos. A declaração completa de acesso tem o prazo legal específico de até 15 dias; outras solicitações devem seguir a regra aplicável ao caso.

## 6. Resposta a incidente

1. Tratar suspeita como incidente: registrar quando foi detectado, sistemas e contas possivelmente afetadas, dados envolvidos e quem está coordenando.
2. Conter o acesso (revogar credencial/chave, bloquear sessão ou rota afetada), preservar logs e evidências, corrigir a causa e evitar expor dados adicionais durante a investigação.
3. Avaliar risco ou dano relevante aos titulares e informar prontamente o controlador do perito afetado, com os fatos necessários para sua decisão.
4. O controlador avalia e realiza, quando exigido, a comunicação à ANPD e aos titulares dentro do prazo regulamentar. A regra geral da ANPD prevê três dias úteis para incidentes com risco ou dano relevante, ressalvada legislação específica.
5. Registrar causa, escopo, decisões, comunicações e ações preventivas. Não apagar logs ou evidências relevantes antes da avaliação.

## 7. Itens para liberar cadastro comercial

- Confirmar que `SERVICE_OPERATOR_NAME` e `EMAIL_SUPPORT_ADDRESS` estão corretos na VPS e que a caixa de suporte é monitorada; SMTP Zoho com domínio autorizado já foi configurado e testado.
- Confirmar nome jurídico, regiões de processamento, subprocessadores e condições de Locaweb, Zoho Mail, Asaas e Backblaze B2 antes de publicar afirmações específicas sobre localização ou tratamento dos dados.
- Revisar a página de privacidade e preparar Termos de Uso, condições de assinatura/cancelamento e contrato/instruções de tratamento com assessoria apropriada.
- Testar o envio do aviso de inatividade via SMTP; backup externo criptografado Restic/B2 já foi configurado e restaurado em pasta temporária, mas falta confirmar a execução automática agendada.
- Validar isolamento de contas e exportação sem dados de terceiros no artefato implantado; validar Asaas em produção e seus webhooks antes de cobrar.
- Definir pessoa responsável por privacidade e processo de plantão para incidente. Não publicar dados de contato pessoais sem decidir que esse canal será monitorado.

## 8. Referências oficiais

- [ANPD — Direitos dos titulares](https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados/direito-dos-titulares)
- [ANPD — Agentes de tratamento e encarregado](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-orientativo-para-definicoes-dos-agentes-de-tratamento-e-do-encarregado)
- [ANPD — Comunicação de incidente de segurança](https://www.gov.br/anpd/pt-br/canais_atendimento/agente-de-tratamento/comunicado-de-incidente-de-seguranca-cis)

## Termos de uso e cobrança

A página pública de Termos de Uso foi preparada em `Views/Home/Terms.cshtml` e ligada à landing, ao cadastro e à tela de assinatura. Ela descreve a assinatura mensal atual, cobrança recorrente por cartão via Asaas, confirmação de pagamento pelo webhook, cancelamento da renovação e manutenção do acesso até o período pago. Cancelar não exclui a conta ou os dados; a retenção segue a política de privacidade. A página exige revisar os dados do fornecedor e mostra um aviso enquanto faltarem `Service__LegalName`, `Service__TaxId`, `Service__BusinessAddress` ou o contato de suporte. Revisar o texto com assessoria jurídica e conferir com o contrato e a configuração real do Asaas antes de abrir vendas.
