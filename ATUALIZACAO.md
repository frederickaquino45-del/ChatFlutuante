# Atualizar o Wfloat.IA sem desinstalar

O aplicativo é portátil: não precisa de desinstalação. O EXE e as DLLs ficam na pasta de distribuição; contas, preferências e conversas ficam em `%LOCALAPPDATA%/WindowFloatIA`.

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
