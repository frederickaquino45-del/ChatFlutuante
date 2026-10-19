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
- Arraste a faixa superior para mover; a borda direita ajusta a largura; o separador abaixo da conversa ajusta sua altura.
- Posição, largura, altura da conversa e opção Fixar são salvas em `%LOCALAPPDATA%/WindowFloatIA/window.json` ao esconder ou encerrar. Preferências ausentes ou inválidas usam os valores padrão.
- A janela ajusta sua posição à área útil do monitor ao abrir e em alterações de tela/DPI.

## Verificação automatizada

```powershell
dotnet run --project tests/WindowFloatIA.Smoke -c Release
```

Executa verificações dos componentes WPF e do adaptador HTTP com dados simulados: streaming SSE, histórico, cancelamento, exclusão de turnos incompletos do contexto, bloqueio de envios simultâneos, erros 401/429/500, recusa e conexão truncada. Nenhuma chave ou chamada real é usada. Gera imagens fora da tela em `artifacts/previews/`. Os testes não substituem a validação manual de atalhos globais, bandeja, monitores/DPI e uma chamada autenticada.

## Conectar o primeiro agente GPT

1. Execute o aplicativo e clique em **GPT**.
2. Informe uma chave de API da sua conta OpenAI e um modelo disponível nessa conta.
3. Clique em **Aplicar**, digite uma pergunta e pressione Enter.

O modelo inicial é `gpt-5.6-terra`, editável na tela. A configuração só é validada pela API ao enviar a primeira pergunta. A chave fica na memória do processo e não é gravada nas preferências, no histórico ou em logs. Apagar a chave e aplicar desconecta o agente; encerrar libera a sessão. Não compartilhe sua chave no chat ou em commits.

Opcionalmente, o aplicativo lê `OPENAI_API_KEY` e `OPENAI_MODEL` do ambiente ao iniciar. Arquivos `.env` não são carregados pelo aplicativo. Não inclua uma chave compartilhada no executável: esta conexão direta é destinada ao uso local com a chave do próprio usuário. Uma distribuição com credenciais compartilhadas exigiria um backend.

O acesso é via API, não usa a sessão deste chat/Codex. As solicitações utilizam a conta e o saldo associados à chave. A implementação segue a [Responses API com streaming](https://developers.openai.com/api/docs/guides/streaming-responses). Consulte também o [modelo configurado inicialmente](https://developers.openai.com/api/docs/models/gpt-5.6-terra).

### Fluxo e limites

- `IChatAgent` define o contrato para futuros provedores; `OpenAiAgent` implementa a chamada HTTPS/SSE usando HttpClient, sem dependências externas.
- `ConversationSession` mantém até dez pares de pergunta/resposta concluídos, limitados também a 64.000 caracteres. Cada pergunta aceita até 16.000 caracteres; o envio inclui esse contexto recente. Esses limites são em caracteres, não tokens.
- A API recebe `store: false`; isso não é uma promessa de retenção zero de dados pelo provedor. O aplicativo não persiste a conversa localmente.
- O botão de envio vira **Parar**. Esconder a janela mantém a requisição ativa; sair solicita seu cancelamento. Interromper a conexão não garante reversão de processamento/cobrança já ocorridos no servidor.
- Texto parcial é preservado em falhas e cancelamentos, mas não entra no contexto seguinte. Não há repetição automática. Em erros, o campo recupera a pergunta se você não tiver começado outro rascunho.
- **Novo** limpa histórico e contexto. Durante uma resposta, configuração e nova conversa ficam bloqueadas; o campo continua disponível para preparar o próximo rascunho.
- O primeiro agente somente responde por texto: não tem ferramentas, acesso ao computador, uploads, pesquisa na web ou ações externas.

Histórico persistente, anexos para IA, ferramentas, comando de voz e inicialização automática ficam para próximas etapas.
