# Rota para Windows

Aplicativo desktop nativo para **Windows 10 e Windows 11 x64**, construído em WPF / .NET 8 e sem dependências de UI de terceiros.

## Estado da edição Windows

- Versão: **0.3.2**
- novos perfis recebem boas-vindas, configuram objetivo, prazo, dias e horas e, na terceira etapa, informam dificuldades para a IA local preparar o primeiro plano; **Agora não** adia qualquer etapa sem marcá-la como concluída;
- o primeiro plano exibe uma prévia e reutiliza a confirmação final existente antes de aplicar; se já houver um plano futuro, ele pode ser mantido sem substituição, e se a IA não estiver pronta a tela apenas oferece a configuração — nunca inicia download automaticamente;
- os dias disponíveis também podem ser alterados depois em **Configurações**, junto com o limite diário;
- interface principal **calendar-first**, com o mês completo em grade e a agenda detalhada do dia selecionado logo abaixo;
- navegação rápida entre meses, dias e retorno para hoje;
- sessões pendentes mostram uma indicação de arraste e podem ser movidas para outro dia no calendário; concluídas e revisões automáticas permanecem imóveis;
- um aviso discreto identifica blocos atrasados e sua carga total sem alterar datas; a análise ignora sessões concluídas, distingue revisões automáticas protegidas e devolve apenas uma visão limitada dos dados;
- a tela **Recuperar estudos atrasados** detalha uma amostra segura dos blocos, leva ao mais antigo e pode preparar um pedido limitado para a IA reorganizar somente datas futuras; o calendário só muda após prévia e confirmação final;
- um lembrete diário opcional pode ser ativado nas Configurações; o Agendador de Tarefas do Windows abre no horário escolhido um resumo somente leitura dos blocos de hoje e atrasados, mesmo com o aplicativo fechado;
- o botão **Resumo** mostra a semana atual de segunda a domingo, com minutos e sessões planejados, concluídos e restantes, percentual geral e andamento diário, sem alterar o calendário;
- dentro do Resumo, **Por matéria** agrupa todo o calendário salvo e compara minutos, sessões e dias concluídos de cada disciplina; o painel deixa explícito que ainda não mede notas ou acertos;
- dentro do Resumo, **Histórico** mostra os últimos 12 meses e compara as últimas 8 semanas com a semana anterior, sem alterar o calendário;
- tema **escuro e claro**, com troca imediata pela interface e preferência preservada localmente;
- cards de contexto mostram plano ativo, objetivo, carga do mês e progresso mensal usando dados reais do calendário;
- cada dia indica visualmente conteúdo, revisão, simulado/prova ou conclusão;
- os blocos do dia mostram matéria, duração, tipo, tópico, meta e ação de conclusão.
- o menu **Assistente IA** abre a área local com perfil, estado offline, ações rápidas, conversa contínua e histórico de propostas;
- a opção **Testar minha IA** verifica instalação, perfil, processador, memória, placa de vídeo e armazenamento, além de medir carregamento, resposta e velocidade real da IA local por ação explícita;
- pedidos do ENEM usam um catálogo local versionado, com quatro áreas oficiais, redação separada e matérias/conteúdos validados antes de qualquer prévia;
- a configuração da IA oferece perfis Automático, Leve, Equilibrado e Desempenho, mostrando modelo, download e espaço antes de pedir confirmação;
- instalação e download podem ser cancelados e só ficam ativos depois da verificação de integridade;
- propostas prontas são recalculadas sobre o calendário atual e só podem ser aplicadas após uma confirmação final explícita;
- aplicações ficam registradas no próprio estado do calendário, não podem se repetir e podem ser desfeitas enquanto nenhuma alteração posterior tiver ocorrido;
- o gerador de prompt para usar com outra IA permanece disponível dentro do assistente.

A preferência de tema fica separada do estado de estudos em `%LOCALAPPDATA%\Rota\theme.txt`. O calendário, o histórico, o lembrete e o progresso da configuração inicial continuam em `%LOCALAPPDATA%\Rota\desktop-state.json`. Quando ativado, o Rota cria somente a tarefa diária **Rota - Lembrete de estudos** no Agendador de Tarefas do usuário; ao desativar, remove apenas essa tarefa.

## O que esta versão faz

- importa o mesmo protocolo **StudyPlan Code 0.2** usado pelo Rota Android;
- valida o JSON antes de alterar qualquer sessão;
- preserva histórico concluído;
- substitui somente sessões futuras pendentes de planos importados;
- preserva revisões automáticas runtime existentes;
- cria D+1, D+3 e D+7 pela data real de conclusão do bloco `study`;
- mantém `plan.id` + `revision` monotônicos;
- considera histórico protegido e revisões runtime no limite diário antes de aplicar um novo plano;
- permite arrastar um `.json` StudyPlan diretamente para a janela;
- gera um prompt explicativo para qualquer IA externa, ensinando como o Rota consome e executa o JSON;
- salva o estado de estudos localmente com gravação atômica e cópia íntegra anterior em `.bak`;
- impede duas instâncias simultâneas de alterarem o mesmo calendário;
- permite exportar backup do arquivo local.

Se o estado principal estiver inválido, o Rota o preserva com o sufixo `desktop-state.corrupt-...json` e tenta recuperar automaticamente a última cópia íntegra. Ele nunca descarta silenciosamente o arquivo corrompido.

Atalhos úteis:

- `Ctrl+T`: voltar para hoje;
- `Ctrl+I`: importar StudyPlan;
- `Ctrl+G`: abrir Assistente IA;
- `Ctrl+,`: abrir Configurações;
- `Ctrl+Shift+L`: alternar entre tema escuro e claro.

## Segurança do StudyPlan

O Rota **não executa código** recebido da IA. O importador aceita apenas um objeto JSON declarativo com schema fechado. Scripts, comandos, HTML, URLs executáveis e campos desconhecidos não fazem parte do protocolo.

A identidade persistida de uma sessão é o par `(plan.id, session.id)`. O marcador `::review::` é reservado às revisões automáticas do runtime.

## Executável

O CI publica o artefato **`Rota-Windows-v0.3.2-x64`** com um executável `win-x64` **self-contained**, então o usuário não precisa instalar o .NET 8 separadamente.

Os modelos de IA não ficam embutidos no EXE. A pessoa escolhe um perfil e confirma a instalação dentro do aplicativo; só então o Rota baixa e verifica o runtime e o modelo correspondentes. Isso mantém o instalador do aplicativo menor e torna transparente o espaço necessário para cada perfil.

O binário de desenvolvimento ainda não possui assinatura Authenticode comercial. O Windows SmartScreen pode exibir um aviso de reputação na primeira execução. Uma distribuição pública definitiva deve usar um certificado de code signing persistente e protegido fora do repositório.

## Build local

Em Windows 10/11 com .NET SDK 8:

```powershell
dotnet build .\desktop\Rota.Windows\Rota.Windows.csproj -c Release
dotnet run --project .\desktop\Rota.Windows\Rota.Windows.csproj
```

Publicação x64 self-contained:

```powershell
dotnet publish .\desktop\Rota.Windows\Rota.Windows.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false -o .\artifacts\Rota-Windows
```

