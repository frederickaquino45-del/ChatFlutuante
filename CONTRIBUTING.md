# Contribuindo com Wfloat.IA

Obrigado pelo interesse em contribuir! Abra uma issue para relatar um problema ou discutir uma mudança maior.

1. Faça um fork e clone seu fork.
2. Crie uma branch para a alteração.
3. Desenvolva no Windows com o SDK .NET 10.
4. Execute:

   ```powershell
   dotnet build WindowFloatIA.slnx -c Release
   dotnet run --project tests/WindowFloatIA.Smoke -c Release --no-build
   ```

5. Envie a branch ao seu fork e abra um pull request para `master` deste repositório.
6. Descreva o problema, a solução e os testes. Aguarde os checks e a análise do mantenedor.

A branch principal deve receber alterações por pull request. Não envie executáveis, pastas bin/obj, tokens, senhas, conversas ou arquivos de `%LOCALAPPDATA%/WindowFloatIA`. Os pacotes são gerados pelo script de publicação e pelo GitHub Actions.

Os testes usam provedores simulados e não precisam de chaves de API. Não teste integrações cobradas usando credenciais de outras pessoas. O projeto é distribuído sob a licença MIT; contribuições são feitas sob a mesma licença.
