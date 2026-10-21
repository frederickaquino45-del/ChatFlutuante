# Ferramentas Windows e calendário (Ollama)

Esta versão adiciona ferramentas opt-in. Nenhuma é habilitada automaticamente.
Abra **Configurações → Ferramentas**, com a conexão Ollama e o modelo desejados
selecionados no chat. Clique em **Verificar compatibilidade**. A API `/api/show`
deve anunciar `tools`; o nome do modelo, sozinho, não comprova suporte.
Um Mistral instalado com essa capacidade pode solicitar chamadas estruturadas,
mas a precisão dos argumentos continua dependendo do modelo.

O teste de chamada estruturada não executa nenhuma ação. Marque cada ferramenta
e escolha **Perguntar sempre** (padrão) ou **Permitir sempre**. As permissões são
independentes para cada conexão e modelo. No modo de confirmação, um cartão no
chat mostra argumentos e destino: **Permitir uma vez** ou **Cancelar**.

O Wfloat valida os argumentos e executa sequencialmente no máximo cinco rodadas
por pergunta. Texto livre nunca é executado como comando. Chamadas repetidas
na mesma pergunta reutilizam o resultado. Cancelar não desfaz ações concluídas.
Resultados consultados são enviados à conexão/modelo selecionados.

## Aplicativos e informações do computador

Cadastre executáveis locais pela seleção de arquivo. O identificador é o nome
do executável sem extensão; somente esses identificadores podem ser solicitados.
Nesta versão não há argumentos, caminhos fornecidos pelo modelo, scripts,
interpretadores de comandos, executáveis de rede ou atalhos de aplicativos Store.
Remover um aplicativo impede novas aberturas dele.

`get_system_info` consulta Windows, arquitetura, processador e memória física.
Não consulta arquivos, senhas, números de série ou rede.

## Google Calendar

1. Em um projeto seu no Google Cloud, habilite Google Calendar API e configure
   a tela de consentimento. Enquanto o aplicativo estiver em teste, cadastre sua
   conta como usuário de teste.
2. Crie um OAuth Client do tipo **Desktop app**. Informe Client ID e o client
   secret de desktop, caso fornecido pelo registro, na página Ferramentas.
3. Selecione Google, dê um nome à conta e clique em **Conectar / reconectar**.
4. Autorize no navegador, volte ao Wfloat, selecione um calendário editável e
   clique em **Usar calendário selecionado como padrão**.

Escopos: `calendar.events` e `calendar.calendarlist.readonly`.
O callback é `http://localhost:<porta-efêmera>/`, usando PKCE e state.
O aplicativo não cria registros OAuth por você nem distribui suas credenciais.
Registros em teste podem ter limitações de duração do refresh token do provedor.

## Microsoft / Outlook

1. Registre um aplicativo seu no Microsoft Entra, com os tipos de conta que
   pretende usar (incluindo pessoais se necessário).
2. Configure a plataforma **Mobile and desktop applications**, callback
   `http://localhost` e permissão delegada `Calendars.ReadWrite`.
3. Informe o Application/Client ID em Ferramentas, com provedor Microsoft.
   Não informe client secret: este é um cliente público desktop com PKCE.
4. Conecte a conta e selecione um calendário editável como padrão.

O fluxo pede `offline_access` para renovar a sessão. Políticas de organizações
podem exigir aprovação do administrador. A autorização de calendário é separada
da conexão do modelo e do login ChatGPT.

Ambos os serviços usam navegador do sistema, state e PKCE. Tokens são protegidos
por DPAPI no usuário Windows atual, em `%LOCALAPPDATA%\WindowFloatIA\tools.dat`.
Desconectar remove o registro local; para revogar o consentimento também no
provedor, use a página de aplicativos autorizados da sua conta.

## Eventos

`create_calendar_event` recebe `title`, `start`, `end`, `time_zone` e `description`
opcional. Datas devem conter offset ISO 8601; o fuso deve corresponder ao offset.
Eventos são simples, sem convidados ou recorrência. Exemplos:

- “Crie no meu calendário uma reunião amanhã das 14h às 15h, horário de Brasília.”
- “Liste os eventos de amanhã das 8h às 18h, horário de Brasília.”

O serviço selecionado recebe o evento; a sincronização com Outlook/Google fica a
cargo dessas contas. A listagem tem paginação e limite de 100 eventos/consulta,
com indicador de truncamento. Intervalos acima de um ano são rejeitados.

Cada criação usa um identificador de operação. Google recebe um ID de evento
estável; Microsoft recebe `transactionId`. Após falha de transporte, o Google é
consultado por ID; no Microsoft a criação é reconciliada com o mesmo transactionId.
Se o resultado continuar inconclusivo, confira o calendário antes de repetir.
As últimas 100 operações ficam protegidas localmente, sem argumentos nem tokens
no registro, e as dez mais recentes aparecem nas configurações.

## Relógio do Windows — experimental

Clique em **Validar Relógio** antes de habilitar `create_alarm`. Essa verificação
abre o aplicativo, tenta localizar o formulário por Windows UI Automation e o
cancela sem salvar. Não utiliza coordenadas, armazenamento privado do Relógio
nem agendamento próprio do Wfloat.

A implementação exige processo identificado como Microsoft.WindowsAlarms,
controles únicos acessíveis, campos editáveis e formato de hora 24h. Nesta versão
são reconhecidos nomes em português/inglês e IDs de controles conhecidos. Se a
estrutura não corresponder, a ferramenta permanece desabilitada. A validação é
necessária novamente a cada sessão; uma falha revoga a disponibilidade atual.

Ao criar, preenche nome/horário, desliga repetição, confere campos, salva e procura
o alarme na lista. Se não conseguir confirmar a presença, retorna **inconclusivo**
e não tenta novamente. O Relógio pode receber foco. Não use seus controles durante
a execução. Alarmes criados ficam sob responsabilidade do Windows/Relógio.

**Limitação da validação desta entrega:** o Relógio 11.2607.6.0 foi identificado,
mas a ferramenta de controle desta sessão retornou árvore de acessibilidade vazia.
Não foi possível comprovar o cadastro automático real nesta máquina. Nenhum alarme
foi criado como teste. Não considere a integração validada até testar localmente.

## Desenvolvimento e testes

Os testes usam respostas HTTP simuladas para capacidades, autorização, revogação,
chamadas duplicadas, respostas incompletas, limites, histórico e calendário.
Nenhum teste automatizado acessa calendário pessoal ou executa aplicativos reais.
As credenciais OAuth próprias são necessárias para testar login e criação reais.

Referências oficiais:
- [Ollama tool calling](https://docs.ollama.com/capabilities/tool-calling)
- [Google OAuth desktop](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Microsoft OAuth authorization code + PKCE](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
- [Criar eventos no Graph](https://learn.microsoft.com/en-us/graph/api/calendar-post-events?view=graph-rest-1.0)
