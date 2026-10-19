# Conexões de IA

Abra a engrenagem → Conta → **Claude, Ollama, OpenCode e outros**. As conexões salvas aparecem na lista lateral; clique em uma para editar. Para cadastrar outra, escolha **Nova conexão**, preencha os campos e use **Testar conexão / atualizar modelos**. O teste consulta apenas o catálogo; não envia perguntas. **Salvar e carregar modelos** persiste a configuração e disponibiliza a conexão no rodapé do chat.

| Provedor | URL base | Credencial |
| --- | --- | --- |
| Claude | `https://api.anthropic.com` | Chave de API do Claude Console |
| Ollama | `http://127.0.0.1:11434` | Vazia para o servidor local padrão |
| OpenCode | `http://127.0.0.1:4096` | Senha do servidor e usuário, quando configurados |
| Compatible | URL da API, por exemplo `http://127.0.0.1:1234/v1` | Chave conforme o servidor |

O ChatGPT continua no gerenciamento de contas existente, com OAuth. O Claude utiliza a API oficial; esta integração não oferece login Claude.ai nem reutiliza tokens do Claude Code. Veja a [orientação oficial de autenticação](https://code.claude.com/docs/en/legal-and-compliance).

## Ollama e Qwen

O catálogo lista modelos instalados no servidor Ollama; Qwen aparece quando uma variante dessa família estiver instalada. O aplicativo não baixa modelos, não inicia o Ollama e não faz inferência em segundo plano. Inicie o Ollama antes de conectar. O consumo de RAM/GPU depende do modelo; as requisições usam `keep_alive: "1m"` para limitar a permanência após a geração. [API nativa do Ollama](https://docs.ollama.com/api/chat).

## OpenCode

Configure os provedores no próprio OpenCode e inicie `opencode serve --hostname 127.0.0.1 --port 4096` em uma pasta adequada. A senha é configurada no servidor com `OPENCODE_SERVER_PASSWORD`; o usuário padrão é `opencode`. Não é necessário informar as chaves dos provedores no WindowFloatIA. [Servidor OpenCode](https://opencode.ai/docs/server/).

O desktop pode usar uma porta dinâmica diferente de 4096 e exigir `/api` na URL base. Uma página HTML indica que a URL aponta para a interface; HTTP 401 indica que faltam credenciais válidas do servidor. Abrir o desktop não garante um servidor na porta 4096.

Cada conversa usa uma sessão remota própria. A integração consulta as ferramentas disponíveis, desabilita todas no envio e cria a sessão com permissão global `deny`. Se não conseguir enumerar as ferramentas, não envia a pergunta. Respostas usam eventos SSE e o resultado final HTTP. O cancelamento solicita `/abort`; em falhas, a sessão incompleta deixa de ser reutilizada. Se for necessário criar outra sessão, somente o contexto local concluído é reintroduzido, como texto.

Sessões no servidor OpenCode podem continuar armazenadas lá; o limite local de cinco conversas não apaga dados remotos. Remover uma sessão diretamente no servidor pode exigir abrir uma nova conversa no aplicativo.

## Abas, histórico e credenciais

- Cada aba mantém conexão, modelo, rascunho, contexto e cancelamento próprios. Trocar a conexão abre outro chat ou recupera o histórico daquela conexão, sem transferir mensagens da anterior.
- Até cinco conversas recentes são salvas por conexão; o ChatGPT mantém a separação por conta existente. Os registros antigos são compatíveis com a nova versão.
- Mensagens indicam a conexão e o modelo que responderam, incluindo após restaurar o histórico.
- Credenciais e catálogos ficam em `%LOCALAPPDATA%/WindowFloatIA/connections.dat`, cifrados com DPAPI. Credenciais vão apenas em cabeçalhos da requisição; redirecionamentos HTTP não são seguidos.
- Desconectar remove o segredo local e bloqueia novos envios, mantendo o histórico. Não revoga a chave no provedor. Para trocar endereço, provedor ou credencial por outra identidade, crie uma nova conexão.
- URLs remotas exigem HTTPS. HTTP é permitido apenas em loopback. APIs compatíveis devem implementar `GET models` e `POST chat/completions` com streaming SSE.
- Em falhas, não há troca de provedor nem reenvio automático. A resposta parcial permanece visível e não entra no contexto concluído.

## Validação desta implementação

Build Release sem erros ou avisos e suíte de regressão aprovada, incluindo catálogos, NDJSON/SSE, credenciais, contexto, falhas HTTP, respostas truncadas, isolamento entre abas e cancelamento remoto OpenCode. Todos os testes de inferência usam HTTP simulado.

Também foi consultado o schema HTTP do OpenCode **1.18.34** instalado e o catálogo do Ollama **0.33.3** instalado (dois modelos retornados), com servidores temporários encerrados ao final. Nenhuma pergunta foi enviada a modelos nesses testes locais.

Ainda é necessário cadastrar as conexões reais e validar uma pergunta em cada provedor. Claude requer uma chave válida e acesso ao modelo; nomes e disponibilidade vêm do catálogo da conta/servidor.
