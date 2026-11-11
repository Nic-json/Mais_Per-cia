# Orientações para agentes neste repositório

- Comece por `AI_CONTEXT.md` para conhecer o produto, os limites atuais, o mapa dos arquivos e as regras de preservação de dados.
- Use o pedido atual do usuário como escopo. O roadmap em `AI_CONTEXT.md` descreve propostas; não significa que os recursos já existem nem autoriza implementar tudo de uma vez.
- Inspecione apenas os arquivos diretamente relacionados e dependências necessárias. Evite ler ou despejar o projeto inteiro quando o resumo e um recorte dirigido forem suficientes.
- Preserve dados existentes, compatibilidade do SQLite, autenticação, anexos protegidos e o editor atual de laudos. Nunca inclua dados reais, banco, `.env` ou chaves nos commits.
- Depois de uma mudança estrutural ou de funcionalidade, atualize `AI_CONTEXT.md` para refletir o estado real do código.
- Não publique nem faça push sem pedido explícito. Um deploy só está confirmado após GitHub Actions concluir com sucesso e a aplicação ser verificada.
