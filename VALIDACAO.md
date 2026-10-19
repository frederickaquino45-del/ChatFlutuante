# Validação do plano de layout — 19/10/2026

Implementação concluída; validação automatizada aprovada em Windows/.NET 10.

| Requisito | Implementação e evidência |
| --- | --- |
| Rolagem automática | Acompanha o final, pausa ao ler mensagens anteriores e retoma pelo botão. Testes de layout verificam posição e alternância entre abas. |
| Sessão mantida | Armazenamento DPAPI e renovação serializada testados com dados simulados; restauração da conexão no primeiro carregamento da janela. |
| Login fecha automaticamente | Teste do diálogo WPF com autenticação simulada confirma que aguarda o catálogo e fecha com o modelo selecionado. |
| Redimensionamento | Testes das quatro bordas e quatro cantos, limites mínimos e tamanhos independentes dos modos compacto/expandido. |
| Escolha de modelo | Catálogo da conta, seleção por aba e identificação na resposta. Modelo removido exige escolha explícita, inclusive após atualizar novamente o catálogo. |
| Novas abas | Contexto, rascunho, modelo, rolagem, requisição e cancelamento independentes. Testes cobrem resposta em segundo plano, indicador de não lido e fechamento. |

## Correções encontradas na revisão

- Cancelar o primeiro login preserva as abas e a pergunta digitada.
- Fechar a configuração enquanto carrega modelos mantém o catálogo anterior quando a conta permanece igual.
- Uma seleção antiga do diálogo não substitui um modelo válido.
- Trocar de conta ou desconectar limpa o contexto anterior; o primeiro login preserva a pergunta pendente.
- O carregamento cancelado não aplica resultados nem tenta completar o login após fechar a janela.

## Execução

```powershell
dotnet build WindowFloatIA.slnx -c Release -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/WindowFloatIA.Smoke -c Release --no-build
```

Build sem erros ou avisos; suíte completa aprovada. Inclui componentes WPF, streaming, concorrência entre abas, erros HTTP, OAuth simulado, callback em loopback, renovação e DPAPI. Imagens de layout são geradas em `artifacts/previews`; a imagem de abas foi inspecionada visualmente.

## Validação manual restante

1. Autorizar uma conta real, confirmar fechamento da configuração e enviar uma pergunta. A aba do navegador pode ser fechada manualmente.
2. Encerrar e reabrir o aplicativo: confirmar sessão e modelo disponíveis sem novo consentimento. As conversas são mantidas apenas enquanto o processo está aberto.
3. Arrastar/redimensionar entre monitores com escalas diferentes e confirmar limites da área útil.
4. Conferir atalho global, bandeja e consumo em repouso na máquina de uso.

Os testes não usam credenciais reais nem comprovam disponibilidade de modelos ou permissões de uma conta específica.
