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

Não há polling, timers, animações ou tarefas de rede. O atalho é recebido por eventos do Windows. A janela escondida permanece em memória para reabrir rapidamente; consumo real deve ser medido na máquina de destino.

Validação manual: abrir/esconder com outro aplicativo em foco, testar Esc e fechar, alternar Topmost, testar início com `--background`, tentar abrir uma segunda instância e encerrar pela bandeja. Medir CPU e memória após aquecimento, em repouso e depois de vários ciclos de abertura.

## Chat flutuante — primeira versão

- Interface grafite sem barra de título, com cantos arredondados e sombra.
- Estado compacto e histórico expansível. O envio abre a conversa; o botão no topo recolhe sem apagar.
- Enter envia; Shift + Enter insere uma nova linha. O campo cresce até 120 pixels e depois permite rolagem.
- Botão + e arrastar/soltar selecionam até 10 arquivos locais únicos; diretórios são ignorados. Os cartões permitem remover anexos. Nenhum conteúdo de arquivo é lido ou enviado.
- Respostas são simuladas e identificadas como demonstração. O histórico mantém as últimas 100 mensagens em memória e é descartado ao encerrar.
- Arraste a faixa superior para mover; a borda direita ajusta a largura; o separador abaixo da conversa ajusta sua altura.
- Posição, largura, altura da conversa e opção Fixar são salvas em `%LOCALAPPDATA%/WindowFloatIA/window.json` ao esconder ou encerrar. Preferências ausentes ou inválidas usam os valores padrão.
- A janela ajusta sua posição à área útil do monitor ao abrir e em alterações de tela/DPI.

## Verificação automatizada

```powershell
dotnet run --project tests/WindowFloatIA.Smoke -c Release
```

Executa verificações dos componentes WPF (envio, expansão, recolhimento, anexos e limite de histórico) e gera imagens fora da tela em `artifacts/previews/`. Os testes não substituem a validação manual de atalhos globais, bandeja e diferentes monitores/DPI.

Integração com IA, histórico persistente, comando de voz e inicialização automática ainda não estão implementados.
