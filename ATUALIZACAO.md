# Atualizar o Wfloat.IA sem desinstalar

O aplicativo é portátil e inclui `desinstalar.cmd` para remover uma cópia gerada. O EXE e as DLLs ficam na pasta de distribuição; contas, preferências e conversas ficam em `%LOCALAPPDATA%/WindowFloatIA`.

## Procedimento recomendado

1. Obtenha o ZIP completo da nova versão pelo canal oficial ou gere com `gerar-executavel.cmd`.
2. Se houver checksum publicado por uma fonte confiável, compare com `Get-FileHash -Algorithm SHA256 .\WfloatIA-win-x64.zip`. O hash só prova autoria quando sua origem também é confiável.
3. No ícone da bandeja, escolha **Sair**. Fechar a janela apenas esconde o aplicativo e mantém arquivos em uso.
4. Faça uma cópia da pasta atual do aplicativo e de `%LOCALAPPDATA%/WindowFloatIA` para backup privado. O backup de dados pode conter informações pessoais e credenciais cifradas.
5. Extraia a nova versão em uma pasta temporária. Substitua **todos os arquivos do pacote**, incluindo DLLs e runtime, na pasta definitiva. Para evitar sobras de versões antigas, mantenha a antiga pasta como backup e coloque o pacote novo no mesmo caminho original.
6. Abra `WindowFloatIA.exe` no caminho definitivo e confira contas, histórico e atalho.

Mantendo o mesmo caminho do EXE, os atalhos existentes e a entrada de inicialização com Windows continuam apontando para ele. Se mudar o caminho, desative e reative **Iniciar junto com o Windows** na nova cópia e ajuste os atalhos.

## Recuperar uma versão anterior

Encerre pela bandeja e restaure a pasta antiga do aplicativo. Se uma versão futura alterar o formato dos dados, restaure também o backup de dados correspondente; isso remove alterações feitas depois do backup. A proteção DPAPI vincula os dados ao usuário do Windows: copiar para outro usuário/computador não garante acesso.

## Validação e limites

A separação entre pasta do aplicativo e `%LOCALAPPDATA%` foi verificada no código de AuthStore, ConnectionStore, ChatHistoryStore, WindowPreferences e DesktopPreferences. Os testes de persistência usam dados temporários; não é necessário desinstalar ou apagar dados para substituir os binários. Não foi realizada substituição de uma instalação em uso nem migração entre versões incompatíveis nesta revisão.

Não há atualização automática no aplicativo. Para automatizá-la futuramente: manifesto de versão assinado, pacote verificado, preparação em pasta temporária, fechamento controlado, processo separado para troca dos arquivos e rollback em falha. Não executar scripts arbitrários fornecidos pelo servidor de atualização.

## Gerar novamente sem acumular versões

`gerar-executavel.cmd` e `scripts/publish.ps1` usam o mesmo fluxo. A saída fica
sempre em `artifacts/WfloatIA-win-x64` (ou `win-arm64`), com um ZIP e seu SHA-256.
A compilação e a compactação terminam em pasta temporária antes da troca.
Uma falha de compilação preserva o pacote anterior. Falhas durante a troca tentam
restaurá-lo e mantêm arquivos de recuperação para conferência.

Após sucesso, são removidos os pacotes antigos com o padrão de data/hora gerado
anteriormente, somente para a arquitetura escolhida. Pastas de testes, prévias,
arquivos de outros nomes e a outra arquitetura não são apagados. Se algum arquivo
estiver em uso, encerre o aplicativo pela bandeja e execute novamente. A pasta
estável mantém válido o caminho de inicialização nas compilações seguintes;
quem usava uma pasta antiga com data deve reativar a inicialização na nova cópia.

## Desinstalar

1. Escolha **Sair** no ícone da bandeja.
2. Na pasta extraída do pacote, execute **desinstalar.cmd**.
3. Confirme a remoção. Por padrão, contas, preferências e conversas são preservadas.
4. Opcionalmente, confirme apagar os dados de `%LOCALAPPDATA%/WindowFloatIA`.
   Esses dados são compartilhados por todas as cópias do aplicativo neste usuário.

O desinstalador remove apenas os arquivos listados no manifesto do pacote e a
entrada de inicialização automática se apontar para essa cópia. Arquivos extras
adicionados à pasta, ZIPs e atalhos criados manualmente são preservados.
Não precisa de administrador. Não registra um instalador MSI nem uma entrada
em Aplicativos Instalados. Pacotes antigos não possuem o desinstalador.

Para simular sem apagar nada: `powershell -File .\desinstalar.ps1 -WhatIf`.
Para uso automatizado: `-Force` dispensa perguntas e preserva dados; adicionar
`-RemoveUserData` solicita também apagá-los. Não use essa opção para atualizar.
