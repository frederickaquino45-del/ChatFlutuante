# Revisão de segurança — 19/10/2026

## Resultado e escopo

Não foram encontrados indícios de backdoor no código-fonte revisado. Isso não é uma garantia de ausência de vulnerabilidades, nem uma certificação dos binários recebidos de terceiros. Revisão estática da base `80eeec6` e das mudanças desta branch, com testes locais de regressão. Não foi feita perícia da máquina, análise completa do histórico Git ou análise dinâmica de todos os provedores.

## Superfícies verificadas

- **Execução e persistência:** a chamada `Process.Start` abre o navegador no endereço de autorização OAuth. Não há shell de comandos remotos, download/execução de código, carregamento dinâmico de plugins ou atualização automática no aplicativo. O registro HKCU Run é usado somente pela opção explícita de iniciar com Windows. Os scripts de compilação chamam o SDK, empacotam arquivos e calculam checksum; não ativam persistência.
- **Rede:** destinos de autenticação e API OpenAI são fixos; Claude é restrito à API Anthropic. Outros provedores usam o endereço informado pelo usuário. HTTP sem TLS só é aceito em loopback. O HttpClient do aplicativo não segue redirecionamentos e não desativa a validação de certificados.
- **OAuth:** retorno em loopback com porta temporária, state, PKCE S256, nonce e validação da assinatura RSA do ID token, emissor, audiência e expiração. Chaves de validação não vêm de URLs escolhidas pelo token.
- **Credenciais e histórico:** persistência local cifrada com DPAPI do usuário atual. Não foram encontrados segredos de produção embutidos nos caminhos de execução examinados. DPAPI não protege contra processos maliciosos executados como o mesmo usuário.
- **Conteúdo de IA:** Markdown é renderizado como texto WPF, sem executar HTML/JavaScript. Links são texto com tooltip; a resposta não inicia processos. OpenCode recebe sessões com permissões deny e ferramentas desabilitadas, incluindo as enumeradas no servidor.
- **Dependências:** `dotnet list WindowFloatIA.slnx package --include-transitive` não encontrou pacotes NuGet externos. O runtime .NET, Windows e ferramentas de build permanecem dependências de confiança.
- **CI:** permissões de token somente leitura, execução por pull_request, sem pull_request_target. Nesta branch, Actions foram fixadas por SHA de commit obtido dos repositórios oficiais, e checkout não persiste credenciais.

## Riscos remanescentes e recomendações

1. **Distribuição sem assinatura:** SHA-256 detecta alteração, mas não prova autoria se ZIP e checksum forem substituídos juntos. Publicar por canal confiável; adotar assinatura Authenticode e manifestos assinados antes de implementar download automático de atualizações.
2. **Servidores configuráveis:** quem cadastra um endpoint decide para onde envia mensagens e credenciais. Um servidor malicioso pode armazená-las ou ignorar restrições de ferramentas; as permissões do cliente não tornam um servidor externo confiável.
3. **Respostas grandes:** streams e renderização não possuem um limite global de tamanho de resposta. Um servidor malicioso pode consumir memória excessiva. Recomenda-se impor orçamento de bytes e tamanho renderizado em uma evolução dedicada.
4. **Atualização:** substituir apenas o EXE pode misturar versões de DLLs. Use sempre o pacote completo, aplicativo encerrado e backup. Não há atualizador automático implementado.
5. **Verificação do binário:** a revisão do fonte não garante que um EXE obtido fora do build corresponda a esse código. Não foi executada varredura antivírus ou análise de tráfego em todos os cenários nesta revisão.

## Comunicação de falhas

Não publique tokens, senhas ou históricos em issues. Para um relato público, descreva o comportamento com dados fictícios. Vulnerabilidades com detalhes sensíveis devem ser enviadas por um canal privado disponibilizado pelo mantenedor.
