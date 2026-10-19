# WindowFloatIA

Base de aplicativo Windows em C# / .NET 10 / WPF, sem pacotes NuGet externos e sem navegador embutido.

## Executar

Requer Windows e SDK .NET 10 com suporte ao Windows Desktop.

```powershell
dotnet run --project src/WindowFloatIA
```

Para iniciar escondido na bandeja:

```powershell
dotnet run --project src/WindowFloatIA -- --background
```

## Controles

- `Ctrl + Alt + Espaço`: mostra ou esconde a janela (atalho global).
- `Esc`, botão Esconder ou fechar a janela: esconde sem encerrar.
- Clique duplo no ícone da bandeja: mostra ou esconde.
- Menu da bandeja → Sair: encerra e libera o atalho.
- Opção na janela: ativa/desativa a exibição acima das outras janelas.
- Apenas uma instância por sessão do Windows. Ao executar novamente, uma mensagem informa como acessar a instância aberta.

Se o atalho estiver ocupado, o aplicativo avisa e permanece acessível pela bandeja.

## Compilar e publicar

```powershell
dotnet build src/WindowFloatIA -c Release
dotnet publish src/WindowFloatIA -c Release -r win-x64 --self-contained false -o artifacts/publish
```

A publicação acima requer .NET 10 Desktop Runtime no computador de destino. Use `--self-contained true` para incluir o runtime (distribuição maior).

## Estrutura

- `App.xaml.cs`: ciclo de vida, instância única e bandeja.
- `GlobalHotKey.cs`: atalho usando RegisterHotKey e janela de mensagens Win32.
- `MainWindow.xaml`: interface WPF nativa, redimensionável.

## Consumo e validação

Em repouso, não há polling, animações contínuas ou consultas de rede. O atalho é recebido por eventos do Windows. Durante uma pergunta, há uma conexão HTTP de streaming e um limite de tempo de três minutos. A janela escondida permanece em memória para reabrir rapidamente; consumo real deve ser medido na máquina de destino.

Validação manual: abrir/esconder com outro aplicativo em foco, testar Esc e fechar, alternar Topmost, testar início com `--background`, tentar abrir uma segunda instância e encerrar pela bandeja. Medir CPU e memória após aquecimento, em repouso e depois de vários ciclos de abertura.

## Chat flutuante — primeira versão

- Interface grafite sem barra de título, com cantos arredondados e sombra.
- Estado compacto e histórico expansível. O envio abre a conversa; o botão no topo recolhe sem apagar.
- Enter envia; Shift + Enter insere uma nova linha. O campo cresce até 120 pixels e depois permite rolagem.
- A conexão GPT aceita apenas texto nesta versão. O botão + fica desativado e arrastar arquivos não faz upload.
- O histórico visual mantém as últimas 100 mensagens em memória e é descartado ao encerrar.
- Arraste a faixa superior para mover; todas as bordas e cantos ajustam largura e altura, respeitando dimensões mínimas. O histórico ocupa o espaço livre, mantendo a entrada na parte inferior.
- Posição, dimensões independentes dos modos compacto/expandido, último modelo e opção Fixar são salvos em `%LOCALAPPDATA%/WindowFloatIA/window.json`. Preferências ausentes ou inválidas usam os valores padrão.
- A janela ajusta sua posição à área útil do monitor ao abrir e em alterações de tela/DPI.

## Verificação automatizada

```powershell
dotnet run --project tests/WindowFloatIA.Smoke -c Release
```

Executa verificações dos componentes WPF e do adaptador HTTP com dados simulados: streaming SSE, histórico, cancelamento, exclusão de turnos incompletos do contexto, bloqueio de envios simultâneos, erros 401/429/500, recusa e conexão truncada. Nenhuma chave ou chamada real é usada. Gera imagens fora da tela em `artifacts/previews/`. Os testes não substituem a validação manual de atalhos globais, bandeja, monitores/DPI e uma chamada autenticada.

## Conectar o primeiro agente GPT

1. Execute o aplicativo e clique em **GPT**.
2. Clique em **Continue with ChatGPT** e conclua o login e o consentimento no navegador do sistema.
3. Após carregar os modelos, a configuração fecha automaticamente. Escolha o modelo no seletor da própria conversa.
4. Digite a pergunta e pressione Enter.

A autenticação utiliza OAuth 2 Authorization Code + PKCE e OpenID Connect, pelo fluxo oficial [Sign in with ChatGPT](https://developers.openai.com/siwc/token-sharing-open-source/sign-in). Não é necessário digitar chave de API; `OPENAI_API_KEY` e `OPENAI_MODEL` não são usados. OAuth ainda utiliza tokens internamente, obtidos e renovados automaticamente após autorização.

- O navegador retorna para um listener temporário ligado apenas a `127.0.0.1`, em uma porta disponível e caminho `/auth/callback`. O mesmo endereço é enviado na autorização e na troca do código. O listener fecha ao concluir, cancelar ou após cinco minutos.
- Cada tentativa usa `state`, `nonce` e verifier aleatórios. A identidade é verificada por assinatura RSA/SHA-256 contra o JWKS do emissor, issuer, audience, validade e nonce; a permissão `chatgpt.tokens.use.direct` é necessária para perguntas.
- O primeiro login utiliza registro dinâmico com o nome real do aplicativo. O client ID emitido é guardado por conta; logins seguintes reutilizam esse registro e o identificador estável deste host. Não há client secret embutido nem reutilização de credenciais do Codex.
- Credenciais e registros ficam em `%LOCALAPPDATA%/WindowFloatIA/oauth.dat`, cifrados com DPAPI para o usuário atual do Windows e atualizados por substituição de arquivo. Não copie esse arquivo para outros computadores/usuários. Nunca registre seu conteúdo em logs ou commits.
- A janela de conexão permite selecionar contas registradas, adicionar outra conta/workspace e desconectar. Trocar a conta ativa limpa o contexto da conversa para evitar enviá-lo a outra conta.
- A renovação é serializada antes da expiração, persistindo o refresh token substituto. Falha de renovação ou revogação solicita novo login; perguntas não são reenviadas automaticamente.
- Ao abrir a janela pela primeira vez, a sessão salva é restaurada e os modelos são carregados sem abrir a configuração. O último modelo é restaurado quando ainda está disponível. Falhas mantêm o chat aberto e permitem reconectar pelo botão Conta.
- O fechamento automático se aplica à janela de configuração do aplicativo; a aba do navegador mostra o retorno e pode ser fechada manualmente. Não depende de permissões do navegador para fechar abas.
- Desconectar tenta revogar a sessão remota e sempre remove os tokens locais. Caso a revogação não seja confirmada, a interface orienta desconectar também nas configurações do ChatGPT. O registro da conta e o host são mantidos para reautorização.

Modelos são obtidos do catálogo da conta, preservando a ordem e filtrando `visibility: list`. A chamada usa o endpoint público Responses com `store: false`, `stream: true` e histórico em `input`, sem parâmetros incompatíveis como `max_output_tokens` ou `previous_response_id`. Veja [modelos e inferência](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference) e [limitações do fluxo](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations). Disponibilidade e uso dependem das permissões e limites do plano/workspace autorizado.

### Testes OAuth

A mesma suíte testa PKCE com vetor RFC, callback HTTP local, state incorreto, consentimento negado, client ID incompatível, JWT assinado e adulterado, audience/issuer/nonce/expiração, renovação concorrente, catálogo da conta, revogação e armazenamento DPAPI. Os testes não abrem um navegador, não autorizam contas e não usam tokens reais. O primeiro login e uma resposta real precisam ser validados interativamente pelo usuário.

### Fluxo e limites

- `IChatAgent` define o contrato para futuros provedores; `OpenAiAgent` implementa a chamada HTTPS/SSE usando HttpClient, sem dependências externas.
- `ConversationSession` mantém até dez pares de pergunta/resposta concluídos, limitados também a 64.000 caracteres. Cada pergunta aceita até 16.000 caracteres; o envio inclui esse contexto recente. Esses limites são em caracteres, não tokens.
- A API recebe `store: false`; isso não é uma promessa de retenção zero de dados pelo provedor. O aplicativo não persiste a conversa localmente.
- O botão de envio vira **Parar**. Esconder a janela mantém a requisição ativa; sair solicita seu cancelamento. Interromper a conexão não garante reversão de processamento/cobrança já ocorridos no servidor.
- Texto parcial é preservado em falhas e cancelamentos, mas não entra no contexto seguinte. Não há repetição automática. Em erros, o campo recupera a pergunta se você não tiver começado outro rascunho.
- **+ Novo** abre uma aba independente com histórico, contexto, rascunho, modelo, posição de rolagem e cancelamento próprios. Fechar uma aba cancela somente a resposta dela. Fechar a última cria uma nova conversa vazia.
- Trocar de aba não interrompe respostas. Um indicador sinaliza execução e conteúdo novo nas outras abas. O título é derivado localmente da primeira pergunta, sem chamada adicional à IA.
- Durante uma resposta, apenas a troca de modelo dessa aba fica bloqueada. Outras abas podem enviar perguntas; a configuração de conta permanece bloqueada enquanto qualquer aba responde para evitar misturar contas.
- A rolagem acompanha a resposta quando o leitor está próximo ao final. Rolar para cima suspende esse acompanhamento; **↓ Ir para o final** retoma. As atualizações de rolagem são agrupadas e executadas após o layout.
- Abas e mensagens continuam apenas na memória: são preservadas ao esconder a janela, mas não ao encerrar o processo. A sessão OAuth e as preferências permanecem salvas.
- O primeiro agente somente responde por texto: não tem ferramentas, acesso ao computador, uploads, pesquisa na web ou ações externas.

Histórico persistente, anexos para IA, ferramentas, comando de voz e inicialização automática ficam para próximas etapas.
