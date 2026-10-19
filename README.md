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

Integração com IA, histórico, comando de voz e inicialização automática ainda não estão implementados.
