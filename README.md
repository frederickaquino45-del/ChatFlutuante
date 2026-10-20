# Wfloat.IA

![Wfloat.IA](src/WindowFloatIA/Assets/logo-flat.png)

Chat flutuante para Windows: converse com diferentes provedores de IA sem sair do aplicativo em que está trabalhando. Feito em C# e WPF, com .NET 10, ícone na bandeja e atalho global.

## O que faz

- Abre e esconde o chat com um atalho configurável; padrão **Ctrl + Alt + Espaço**.
- Mantém conversas em abas e restaura até cinco conversas recentes por conexão.
- Mostra respostas em streaming, com Markdown e fórmulas básicas.
- Permite escolher conexão e modelo em cada conversa.
- Oferece tema claro/escuro, cor de destaque, janela redimensionável e opção de manter acima de outras janelas.
- Pode iniciar com o Windows, escondido na bandeja.

## Instalar e abrir

Use Windows 11 x64 para a experiência visual completa. O aplicativo também usa APIs do Windows 10; a aparência das bordas depende da versão do sistema. ARM64 exige uma compilação específica.

1. Obtenha o pacote `WfloatIA-win-x64.zip` de uma distribuição do mantenedor. Se ainda não houver uma release, compile pelo procedimento abaixo. O workflow **Windows CI** também disponibiliza o pacote como artefato nas execuções bem-sucedidas do GitHub Actions.
2. Extraia **todo o ZIP** para uma pasta permanente, por exemplo `C:\Apps\WfloatIA`.
3. Abra `WindowFloatIA.exe`. Mantenha as DLLs e os demais arquivos ao lado dele.
4. Use a engrenagem para configurar uma conta ou conexão de IA.

O pacote gerado pelo script inclui o .NET: não é necessário instalar o runtime separadamente. Ele é portátil, não possui instalador nem assinatura digital. Baixe somente de uma origem em que confie. Para atualizar, encerre pelo menu da bandeja e substitua os arquivos da pasta; seus dados ficam separados.

## Usar o chat

| Ação | Como fazer |
| --- | --- |
| Mostrar ou esconder | Ctrl + Alt + Espaço, ou duplo clique no ícone da bandeja |
| Encerrar de verdade | Botão direito no ícone da bandeja → Sair |
| Criar conversa | Botão + na área de abas |
| Trocar de conversa | Clique na aba |
| Escolher IA | Selecione a conexão e o modelo no rodapé |
| Seguir a resposta | A rolagem acompanha o texto quando você está próximo ao final |
| Ler mensagens antigas | Role para cima; use “Ir para o final” para retomar o acompanhamento |

Fechar ou esconder o chat mantém o aplicativo na bandeja. Apenas uma instância pode ficar aberta por sessão do Windows. A integração OpenCode funciona como chat, com ferramentas desabilitadas.

## Configurar as IAs

Abra **Configurações → Conta**. O ChatGPT tem gerenciamento próprio; as demais conexões ficam em **Claude, Ollama, OpenCode e outros**. As conexões salvas aparecem à esquerda: clique para editar ou use **Nova conexão**. Teste a conexão e depois salve para carregar os modelos.

| Provedor | Configuração |
| --- | --- |
| ChatGPT | Entre pelo fluxo OAuth apresentado pelo aplicativo e selecione um modelo disponível na conta. |
| Claude | Informe uma chave da API Anthropic e use `https://api.anthropic.com`. Uma assinatura do chat Claude não substitui a chave da API. |
| Ollama / Qwen | Inicie o Ollama, instale o modelo desejado e use `http://127.0.0.1:11434`. Normalmente não exige chave localmente. |
| OpenCode | Use a URL de um servidor OpenCode e suas credenciais, quando exigidas. Os modelos vêm dos provedores configurados nele. |
| API compatível | Informe a URL da API, geralmente terminada em `/v1`, e a chave exigida pelo servidor. |

O aplicativo não inclui créditos, não baixa modelos e não inicia esses servidores automaticamente. Custos e acesso aos modelos dependem do provedor. Em modelos locais, o consumo de RAM/GPU é principalmente do servidor de inferência.

### OpenCode: servidor separado

No PowerShell, defina sua própria senha e inicie o servidor:

```powershell
$env:OPENCODE_SERVER_PASSWORD = 'SUBSTITUA-POR-UMA-SENHA-SUA'
opencode serve --hostname 127.0.0.1 --port 4096
```

Mantenha o terminal aberto. Configure URL `http://127.0.0.1:4096`, usuário `opencode` e a senha escolhida. A senha é do servidor, não da conta GPT/Claude.

O OpenCode Desktop pode usar uma porta dinâmica e um prefixo `/api`, com autenticação própria. Abrir o desktop não significa que exista um servidor na porta 4096. Alguns modelos gratuitos recusam integrações externas com HTTP 403: nesse caso, escolha um provedor autorizado ou um modelo local. Consulte o [guia completo de conexões](CONEXOES.md).

## Preferências do Windows

Em **Configurações → Preferências → Inicialização e atalho**:

- Ative **Iniciar junto com o Windows** para abrir na bandeja ao entrar no sistema. A opção começa desativada e só altera a configuração do usuário atual.
- Clique no campo de atalho, pressione a combinação desejada e clique em **Aplicar atalho**. Se houver conflito, o atalho anterior permanece ativo.
- Para voltar ao padrão, use **Restaurar Ctrl + Alt + Espaço** e aplique.

Ative a inicialização somente depois de colocar o aplicativo na pasta definitiva. Se mover a pasta, desative e ative novamente essa opção.

## Dados e privacidade

Configurações e histórico ficam em `%LOCALAPPDATA%/WindowFloatIA`. Credenciais e histórico são protegidos com DPAPI, vinculados ao usuário do Windows; preferências visuais e de atalho ficam em JSON. Não compartilhe essa pasta.

As perguntas são enviadas ao provedor selecionado. Servidores remotos e o OpenCode podem guardar dados conforme sua própria configuração; apagar histórico local não remove sessões remotas. Desconectar uma conta não equivale a revogar uma chave no provedor.

## Compilar a partir do código

Requisitos: Windows, Git e **SDK .NET 10** com suporte a Windows Desktop.

```powershell
git clone https://github.com/frederickaquino45-del/ChatFlutuante.git
cd ChatFlutuante
dotnet build WindowFloatIA.slnx -c Release
dotnet run --project src/WindowFloatIA -c Release
```

Para gerar o executável portátil com runtime incluído, ZIP e checksum SHA-256:

```powershell
./scripts/publish.ps1
# Opcional: compilar especificamente para ARM64
./scripts/publish.ps1 -Runtime win-arm64
```

Saída padrão: `artifacts/WfloatIA-win-x64/WindowFloatIA.exe` e `artifacts/WfloatIA-win-x64.zip`. A compilação ARM64 não foi validada em hardware ARM64.

Para executar os testes:

```powershell
dotnet run --project tests/WindowFloatIA.Smoke -c Release
```

Os testes de provedores usam respostas simuladas, sem gerar cobranças de inferência. Conexões reais dependem de credenciais e disponibilidade externas.

## Solução de problemas

- **O aplicativo já está aberto:** procure o ícone na bandeja e use Mostrar/esconder.
- **Atalho indisponível:** altere a combinação nas preferências.
- **Não foi possível conectar:** confira o servidor, a porta e a URL base.
- **Resposta HTML:** a URL aponta para uma página, não para a API.
- **HTTP 401/403:** confira credenciais e permissões; modelos gratuitos do OpenCode podem proibir uso por integração.
- **Nenhum modelo disponível:** configure o provedor ou instale um modelo no Ollama e atualize o catálogo.

## Contribuir e licença

Contribuições são feitas por **pull request** para `master`. Veja [CONTRIBUTING.md](CONTRIBUTING.md). Use issues para relatar problemas, removendo dados pessoais e credenciais das capturas.

Distribuído sob a [licença MIT](LICENSE). O projeto é independente e não é afiliado aos provedores de IA integrados.

## Atalhos de abas e atualização

- **Ctrl+T:** abre uma nova conversa na conexão atual.
- **Ctrl+W:** fecha a conversa selecionada e cancela a resposta dela, se estiver em andamento. Ao fechar a última, uma nova aba vazia é criada.
- Os atalhos de abas funcionam quando o chat está em foco. O atalho global de mostrar/esconder continua configurável nas preferências.

O aplicativo portátil pode ser atualizado sem desinstalar: encerre pelo menu da bandeja e substitua o pacote completo no mesmo caminho, preservando `%LOCALAPPDATA%/WindowFloatIA`. Consulte [ATUALIZACAO.md](ATUALIZACAO.md) para backup e recuperação e [SECURITY.md](SECURITY.md) para a revisão de segurança e seus limites.

Novas conversas e a aba selecionada ao iniciar o aplicativo priorizam modelos leves do catálogo (nano/tiny, luna/mini, haiku/flash-lite, flash/small/lite). Em modelos locais com tamanho explícito, prioriza-se o menor número de parâmetros. Essa escolha é uma heurística pelos nomes, pois os catálogos não fornecem custo e latência comparáveis. Se nenhum modelo for reconhecido, usa-se o primeiro disponível. É possível trocar manualmente durante a conversa; abrir outra aba volta ao padrão leve.
