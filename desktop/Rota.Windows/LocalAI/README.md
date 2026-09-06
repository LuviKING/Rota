# Fundação da IA local do Rota Windows

Este namespace contém os contratos e serviços da IA local usados pela primeira tela visível do Assistente IA. A interface só conversa com o controlador seguro: ela não recebe acesso direto ao backend, ao instalador ou a qualquer operação de escrita no calendário.

## Limite de responsabilidade

O backend local recebe uma entrada estruturada e devolve uma `AiProposal`. Ele nunca aplica alterações no calendário. Uma proposta nova permanece em `Pending` mesmo depois da validação de contrato; o estado `Validated` só é atribuído depois que o motor determinístico do Rota produzir um preview válido.

Há dois formatos de proposta independentes:

- `AiStudyPlanDraft`: contém StudyPlan Code 0.2 e passa pelo `StudyPlanImporter` existente sem ser aplicado;
- `AiPlanChangeDraft`: contém operações estruturadas que o motor determinístico do Rota interpreta durante uma aplicação confirmada.

## Configuração

`AiConfigurationStore` mantém a configuração separada em `%LOCALAPPDATA%\Rota\AI\config.json`. A escrita é atômica, cria uma cópia `.bak` e preserva arquivos inválidos antes de recuperar uma configuração íntegra ou criar os valores seguros padrão.

Os modelos e o runtime não fazem parte do estado ou dos backups do StudyPlan. A instalação local é preparada por um serviço separado e nunca acontece automaticamente ao abrir o Rota. O ciclo de vida e o backend de inferência do `llama.cpp` permanecem isolados; a tela só permite enviar quando a instalação local já estiver íntegra e pronta.

## Perfis de hardware

`WindowsAiHardwareProfileDetector` lê CPU, processadores lógicos, RAM física e adaptadores de vídeo diretamente das APIs locais do Windows. A GPU com mais memória dedicada é usada, adaptadores de software são ignorados e uma falha de enumeração de GPU produz aviso e fallback seguro para CPU/RAM.

`AiHardwareDiagnosticsService` combina essa leitura com o espaço total e livre da unidade onde a IA local será instalada. A consulta roda fora da interface, aceita cancelamento e transforma indisponibilidade do armazenamento em aviso controlado, sem criar pastas nem alterar arquivos.

`AiProfileRecommendationPolicy` mantém os critérios determinísticos e separados da detecção:

- `Lightweight`: fallback seguro para computadores com menos recursos;
- `Balanced`: pelo menos 12 GiB de RAM e CPU com 6 processadores lógicos ou GPU com 4 GiB;
- `Performance`: pelo menos 15 GiB de RAM, 8 processadores lógicos e 7,5 GiB de VRAM;
- `Automatic`: resolve para uma das três opções acima; a política nunca recomenda algo superior a `Performance`.

A seleção manual continua sendo respeitada. Os limites identificam o PC-alvo Ryzen 7 5700X, RTX 3070 8 GB e 16 GB de RAM como `Performance`, sem criar perfil para modelos acima de 7B–8B Q4.

## Catálogo e estado dos modelos

`AiModelCatalog` é um catálogo local, imutável e versionado. Ele contém um modelo GGUF recomendado para cada perfil concreto:

- `Lightweight`: Qwen3 1.7B Q8_0, que é a quantização publicada no repositório oficial desse modelo;
- `Balanced`: Qwen3 4B Q4_K_M;
- `Performance`: Qwen3 8B Q4_K_M.

O catálogo não contém URL, credencial ou código de download. IDs e nomes de arquivo são validados para impedir duplicidade e caminhos relativos ou com travessia de diretório.

`LocalAiModelManager` resolve o perfil automático usando o detector do bloco anterior, respeita uma escolha manual e calcula o estado real a partir dos arquivos locais. O resultado diferencia runtime e modelo disponíveis e só informa `Ready` quando ambos são arquivos não vazios. A inspeção não cria diretórios, não altera a configuração persistida e não executa nenhum binário.

## Instalação verificável

`AiInstallationManifest` fixa todos os downloads por versão/revisão, tamanho exato e SHA-256. O runtime usa `llama.cpp` b10795 para Windows x64, com pacotes CPU e Vulkan independentes. Os três modelos vêm dos repositórios oficiais da Qwen no Hugging Face e usam uma revisão imutável, nunca a referência mutável `main`.

`LocalAiInstaller` executa uma instalação transacional:

1. baixa os artefatos HTTPS para um diretório de staging exclusivo;
2. confere tamanho e SHA-256 antes de usar qualquer arquivo;
3. extrai o ZIP do runtime com proteção contra travessia de diretório, links simbólicos, nomes especiais, duplicidade e expansão excessiva;
4. move o payload completo para uma instalação versionada;
5. conserva uma cópia do ZIP verificado do runtime e grava um recibo local com versão do manifesto e hashes usados;
6. ativa os caminhos somente por meio da escrita atômica de `config.json`.

Falha, corrupção ou cancelamento anteriores à ativação preservam a configuração ativa. A limpeza do staging é de melhor esforço; arquivos bloqueados ou uma interrupção abrupta podem deixar resíduos inativos. Uma instalação anterior válida é conservada, inclusive após uma atualização bem-sucedida.

Chamadas na mesma instância são serializadas; um arquivo de lock exclusivo rejeita outra instância/processo enquanto a instalação estiver em andamento. O instalador verifica espaço livre e rejeita links nos diretórios usados. O downloader limita os bytes recebidos ao tamanho do manifesto, usa espera máxima de 60 segundos para cabeçalhos/leitura ociosa e preserva arquivos preexistentes. Depois da escrita atômica da configuração, uma falha no observador de progresso não muda o resultado de sucesso.

Antes de qualquer execução, `AiInstallationIntegrityVerifier` confere novamente o recibo, o modelo, o ZIP fixado do runtime e todos os arquivos extraídos. Arquivo ausente, modificado, adicional ou redirecionado bloqueia a inicialização. Instalações criadas pelo formato anterior, que não conservavam o ZIP, precisam ser reinstaladas antes de executar. `Ready` no gerenciador continua significando apenas arquivos presentes; a confirmação operacional é o estado separado do runtime.

## Ciclo de vida do llama-server

`LocalAiRuntimeHost` controla uma única instância pertencente ao Rota. Ele monta os argumentos sem usar shell, remove variáveis ambientes `LLAMA_ARG_*`, escolhe CPU ou Vulkan conforme o pacote realmente instalado e inicia o processo oculto no diretório isolado da instalação.

O servidor recebe explicitamente `--host 127.0.0.1`, uma porta local livre, o modelo validado, o contexto configurado, paralelismo 1 e `--n-gpu-layers 0` para CPU ou `all` para Vulkan. Cada execução cria uma chave de API aleatória mantida somente em memória e restringe CORS a `localhost`, reduzindo acesso indevido por outros programas ou páginas locais. O endpoint nunca usa a rede externa. A porta pode sofrer uma corrida entre sua escolha e o bind do processo; nesse caso o processo falha, é encerrado e o erro fica controlado para uma nova tentativa.

O estado percorre `Stopped`, `Starting`, `Ready`, `Stopping` ou `Faulted`. A inicialização somente retorna sucesso depois de `GET /health` responder HTTP 200 com `{"status":"ok"}`. Respostas de carregamento, redirecionamentos, conteúdo inválido, processo encerrado e excesso de tempo não são tratados como prontos. Cancelamento, timeout, parada e descarte encerram toda a árvore do processo pertencente ao Rota. A saída nativa capturada é limitada para não crescer indefinidamente.

O health check segue a interface documentada do `llama-server` b10795. O serviço de ciclo de vida não ativa ferramentas internas do servidor e não tem qualquer referência ao `StudyRepository`.

## Diagnóstico de desempenho

`AiPerformanceDiagnosticsService` só é acionado pelo botão **Medir agora**. Ele verifica a instalação, mede quanto o runtime leva para ficar pronto e envia uma resposta curta e descartável ao endpoint loopback autenticado. A medição informa duração, tokens gerados, tokens por segundo, perfil efetivo e processamento CPU/Vulkan. Quando o `llama-server` já estava ativo, ele permanece ativo; quando foi iniciado apenas para o teste, toda a árvore do processo é encerrada ao final, inclusive após cancelamento ou falha.

`LlamaServerPerformanceProbe` limita a geração a 32 tokens, a resposta a 256 KiB e a execução a dois minutos. A taxa nativa do servidor é usada quando está presente e válida; caso contrário, o Rota calcula uma taxa conservadora pela duração total da resposta. Endpoint remoto, autenticação inválida, propriedades JSON duplicadas, métricas impossíveis, conteúdo incompleto e excesso de tamanho são recusados. O payload não contém calendário, conversa ou proposta, e esta camada não recebe nenhuma referência ao `StudyRepository` ou aos arquivos desses dados.

## Inferência estruturada

`LlamaServerBackend` implementa `ILocalAiBackend` sobre `POST /v1/chat/completions`. Ele só aceita a conexão `127.0.0.1` autenticada criada pelo controlador, nunca envia caminhos locais e serializa uma geração por vez. A requisição usa temperatura baixa, semente fixa, máximo de 4.096 tokens, limite total de cinco minutos e um JSON Schema diferente para criação de plano ou operações. Sequências de controle do template Qwen presentes no texto do usuário são neutralizadas antes do envio.

O modelo recebe apenas a entrada estruturada e a regra de que sua saída é uma proposta, nunca uma alteração aplicada. A resposta HTTP é limitada a 2 MiB, precisa terminar normalmente e conter exatamente uma escolha. JSON duplicado, desconhecido, truncado, grande demais ou fora do contrato é rejeitado. O backend cria IDs e timestamp confiáveis localmente; o modelo não controla `Pending`, identidade ou hora da proposta.

Uma resposta de plano ainda passa pelo `StudyPlanImporter` 0.2. Uma resposta de alterações só pode usar os sete tipos enumerados e depois passa pelo `AiContractValidator`. Nada nesta camada chama `StudyRepository`, aplica calendário ou altera histórico. Cancelar a chamada interrompe a requisição de geração, mantendo o servidor disponível para uma futura solicitação.

## Contexto do plano atual

`StudyPlanningContextProvider` cria uma fotografia somente leitura para propostas de alteração. Ela contém preferências de duração, identidade do plano ativo e no máximo 200 sessões pendentes a partir do dia atual. Sessões passadas ou concluídas, marca de conclusão, alvo detalhado de estudo e rótulo interno de revisão ficam de fora. Quando há mais sessões futuras, o contexto informa que a lista foi truncada em vez de crescer sem limite.

O contexto é copiado para contratos próprios antes de chegar ao backend. O modelo não recebe `StudyRepository`, caminho do arquivo de estado ou qualquer função de escrita. Revisões automáticas futuras continuam visíveis apenas com os dados necessários para calcular carga e são marcadas como protegidas contra remoção direta. A validação rejeita datas passadas, identidades duplicadas, limites inválidos e inconsistência nessa proteção. Pedidos de plano novo não recebem a fotografia do plano existente.

## Conversa local contínua

`AiConversationStore` mantém em `%LOCALAPPDATA%\Rota\AI\conversation.json` uma única conversa local que sobrevive ao reinício do aplicativo. Cada pedido recebe identidade própria, começa como `Pending` e termina como `Completed`, `Cancelled` ou `Failed`. Uma resposta concluída guarda o ID da proposta correspondente; o mesmo vínculo também é persistido em `proposals.json`, permitindo reparar automaticamente uma interrupção ocorrida entre as duas gravações.

O arquivo aceita no máximo 200 turnos e 2 MiB, rejeita propriedades duplicadas e campos desconhecidos, grava por substituição atômica e recupera a última cópia íntegra. O modelo recebe somente até seis trocas concluídas, com 500 caracteres por turno e 6.000 caracteres no total. Pedidos incompletos, cancelados ou com falha não entram no contexto. O texto é neutralizado junto com o restante do payload e continua sendo dado não confiável; nenhuma função, caminho local, recibo ou capacidade de escrita acompanha a conversa.

## Catálogo ENEM estruturado

`EnemCatalogService` fixa a versão `inep-enem-2026-r1` a partir da publicação institucional do Inep de 2026. As quatro áreas objetivas oficiais permanecem intactas e a redação fica como componente separado. A matriz oficial organiza competências, habilidades e objetos de conhecimento; a divisão desses objetos em 14 matérias é declarada explicitamente como curadoria interna do Rota, sem atribuí-la ao Inep.

O catálogo contém IDs estáveis, nomes canônicos, aliases locais e 55 grupos de conteúdo: 50 nas áreas objetivas e cinco de redação. Ele só é ativado quando o pedido menciona ENEM como termo isolado. A seleção enviada ao modelo omite aliases e URL, fica limitada a quatro áreas, 15 matérias e 55 conteúdos e marca preferências declaradas como `weak`, `strong`, `mentioned` ou `coverage`. O modelo continua livre para escolher prioridade, ordem, datas e carga, mas não recebe permissão para criar currículo.

Toda proposta de plano ENEM é verificada depois da inferência: cada `subject` e `topic` precisa corresponder a um nome canônico presente no contexto enviado. Operações que adicionam sessão ou alteram prioridade também precisam usar matéria do catálogo. Matéria ou conteúdo inventado bloqueia a proposta antes da prévia, do histórico e da tela. Pedidos que não são do ENEM continuam seguindo o fluxo geral sem essa restrição.

## Prévia isolada

`AiProposalPreviewService` transforma uma proposta pendente em uma prévia `Ready` ou `Blocked`, sem aplicar a proposta. Planos novos reutilizam a validação já existente do repositório. Alterações trabalham sobre uma cópia do contexto: mover, adicionar e remover sessões, mudar disponibilidade e redistribuir carga produzem contagens e itens de comparação determinísticos.

A prévia bloqueia IDs ausentes ou ambíguos, datas passadas ou posteriores ao objetivo, sessões maiores que os limites do aplicativo, excesso de minutos no dia, alteração direta de revisão automática e qualquer simulação baseada num contexto truncado. A redistribuição preserva revisões automáticas em suas datas e só movimenta sessões comuns. Uma solicitação genérica de reconstrução é recusada até que a IA produza um `StudyPlan` detalhado. O estado no disco é comparado nos testes antes e depois da prévia e permanece idêntico.

## Histórico de propostas

`AiProposalStore` mantém propostas e prévias em `%LOCALAPPDATA%\Rota\AI\proposals.json`, separado de `desktop-state.json`. O arquivo aceita no máximo 100 registros e 8 MiB, rejeita campos ou propriedades JSON duplicadas, grava por substituição atômica e recupera a última cópia íntegra quando o arquivo principal é corrompido.

Uma prévia pronta entra no histórico como `Validated`; uma prévia bloqueada entra como `Failed`. A confirmação final leva a proposta para `Applied`, e um desfazer concluído registra `Undone`. A pessoa também pode rejeitar uma proposta validada ou aceita. IDs não podem se repetir e proposta, prévia e timestamp UTC são validados em toda leitura e gravação.

## Fluxo coordenado

`AiProposalWorkflowService` executa geração, prévia e gravação como uma única operação serializada e cancelável. Para alterações, `AiPlanningService` devolve junto da proposta a mesma fotografia enviada ao modelo; o coordenador entrega exatamente essa instância à prévia. Assim, uma agenda alterada enquanto a geração está em andamento não faz a validação usar silenciosamente outro contexto.

O histórico só é chamado depois que geração e prévia terminam e depois de uma última verificação de cancelamento. Falha em qualquer etapa vira erro controlado e não deixa proposta parcial. Prévia bloqueada é registrada de forma íntegra como `Failed`. O fluxo de geração continua sem método de aplicação e não escreve em `StudyRepository`; a aplicação fica isolada no serviço específico descrito abaixo.

## Composição de produção

`LocalAiServices` monta configuração, detecção de hardware, diagnósticos de hardware e desempenho, catálogo de modelos, catálogo ENEM, instalador, runtime, backend, contexto do plano, conversa, prévia, histórico, fluxo e aplicação confirmada usando uma única raiz `%LOCALAPPDATA%\Rota\AI`. O aplicativo cria esse contêiner depois do repositório e o descarta ao sair, encerrando primeiro os controladores e fluxos e, em seguida, qualquer processo de runtime pertencente ao Rota.

A composição é deliberadamente inerte: seus construtores não criam a pasta de IA, não leem hardware, não baixam arquivos, não iniciam `llama-server` e não geram respostas. Essas ações só acontecem quando um comando explícito chamar o serviço correspondente. A abertura normal e os smoke tests comprovam que o calendário continua independente.

## Controlador da interface

`AiAssistantController` concentra o estado consumido pela tela do assistente, sem depender de XAML. Somente ao abrir essa área ele carrega perfil efetivo, estado da instalação, até 100 propostas e os turnos persistidos. A inicialização reconcilia um pedido interrompido com a proposta já salva ou o encerra como falha quando nenhuma proposta existe. Os cinco comandos rápidos são transformados em entradas estruturadas e escolhem criação de `StudyPlan` ou alteração conforme o caso.

O envio só é liberado quando runtime e modelo estão prontos. Durante a geração, o controlador publica estado ocupado e mantém um cancelamento próprio; cancelar encerra a operação e informa que nenhuma proposta parcial foi salva. Sucesso acrescenta a prévia ao histórico visível com o aviso de que nada foi aplicado. Erros inesperados são contidos sem expor detalhes internos. O descarte central espera uma operação ativa terminar após cancelá-la, antes de desmontar os serviços.

## Tela do Assistente IA

`AiAssistantWindow` integra o controlador ao visual já existente do Rota. Ela mostra estado local/offline, perfil efetivo, disponibilidade da instalação, avisos, cinco ações rápidas, escolha entre plano novo e ajuste do plano atual, envio, cancelamento, a conversa contínua e o histórico de prévias validadas ou bloqueadas. Os balões distinguem pessoa e Rota IA, mostram pedidos em geração, cancelados ou não concluídos e identificam respostas vinculadas a propostas. Propostas prontas oferecem ações explícitas de revisar/aplicar e rejeitar; uma aplicação ainda elegível oferece desfazer. O layout preserva os alvos de clique, ícones e estados visuais compartilhados e mantém as duas colunas utilizáveis na menor janela suportada.

`AiProposalConfirmationWindow` reapresenta a avaliação feita sobre o calendário atual, as contagens antes/depois, operações e avisos. Somente o botão final "Aplicar no calendário" consome a confirmação descartável. O gerador anterior de prompt para outra IA continua acessível como opção secundária, preservando a funcionalidade já existente.

Na terceira etapa da configuração inicial, `OnboardingFirstPlanWindow` combina a rotina já salva com as dificuldades informadas e envia uma entrada estruturada para o mesmo fluxo local. A etapa retoma uma proposta válida interrompida, mostra a prévia e exige a confirmação final existente antes da aplicação determinística. **Agora não** mantém a etapa pendente; um plano futuro já ativo pode ser preservado sem chamar a IA, e uma instalação ausente apenas apresenta a ação de configuração, sem download automático.

## Instalação guiada

`AiInstallationController` transforma a instalação em duas etapas independentes. A análise carrega a configuração, resolve o perfil automático ou manual, escolhe o modelo e o runtime compatíveis e calcula download, espaço recomendado e pasta local sem criar arquivos ou iniciar rede. Ela emite um identificador de confirmação descartável; somente esse identificador permite iniciar uma tentativa, e qualquer confirmação incorreta ou reutilizada expira o plano.

`AiInstallationWindow` mostra Automático, Leve, Equilibrado e Desempenho, além do modelo, processamento, tamanho e destino antes do download. O clique em instalar ainda abre uma confirmação final com esses mesmos dados. Durante a instalação, a tela apresenta etapa, bytes, porcentagem e cancelamento. Sucesso só é exibido depois da ativação atômica; cancelamento ou falha informam que nenhuma instalação incompleta foi ativada.

Abrir o Rota ou o Assistente IA continua inerte. A detecção de hardware ocorre ao abrir a configuração da IA, mas o download só começa depois da confirmação final da pessoa. O controlador da instalação não tem acesso ao calendário ou ao histórico de estudos.

## Aplicação confirmada e desfazer seguro

`AiProposalApplicationService` recarrega a proposta pelo ID, captura o estado inteiro do calendário numa única seção crítica e recalcula a prévia antes de emitir uma confirmação descartável. A confirmação fica vinculada à proposta, à data e à versão exata do estado. Qualquer conclusão, importação, preferência, outra aplicação ou virada do dia expira a confirmação e exige nova revisão.

Planos completos continuam passando pelo `StudyPlanImporter`. Operações estruturadas são traduzidas pelo código determinístico do Rota: mover, adicionar e remover sessões futuras, registrar prioridade, alterar disponibilidade e executar a redistribuição já calculada pela prévia. Reconstrução genérica permanece bloqueada. A aplicação final revalida o estado completo, inclusive sessões concluídas futuras e revisões automáticas, e proíbe qualquer mudança em passado, conclusão ou origem `runtime`.

O recibo da proposta e o ponto de desfazer são gravados no mesmo commit atômico de `desktop-state.json`. Esse recibo é a fonte da verdade quando a atualização posterior de `proposals.json` falha, permitindo reconciliar `Applied` ou `Undone` ao reabrir sem aplicar novamente. O mesmo ID nunca pode ser reaplicado, nem depois de desfazer. O ponto de desfazer restaura o estado anterior somente enquanto nenhuma mutação ocorreu depois; caso contrário, o Rota recusa a operação e preserva integralmente as mudanças posteriores. O índice máximo de revisões dos planos nunca diminui.

A disponibilidade agora aceita precisão de minutos e conserva dias habilitados; prioridades por matéria também são persistidas. Estados antigos continuam carregando com os valores padrão quando esses campos ainda não existem.

### Fontes fixadas e verificadas em 05/09/2026

- [llama.cpp b10795](https://github.com/ggml-org/llama.cpp/releases/tag/b10795): pré-release oficial, CPU/Vulkan Windows x64. Ambos os ZIPs foram baixados, tiveram tamanho e SHA-256 conferidos, passaram pela extração do instalador e foram executados pelo host real do Rota.
- [documentação do llama-server b10795](https://github.com/ggml-org/llama.cpp/blob/b10795/tools/server/README.md): fonte dos argumentos de host, porta, camadas de GPU e do contrato `GET /health`.
- [Qwen3 1.7B](https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/tree/90862c4b9d2787eaed51d12237eafdfe7c5f6077): Q8_0, 1.834.426.016 bytes.
- [Qwen3 4B](https://huggingface.co/Qwen/Qwen3-4B-GGUF/tree/bc640142c66e1fdd12af0bd68f40445458f3869b): Q4_K_M, 2.497.280.256 bytes.
- [Qwen3 8B](https://huggingface.co/Qwen/Qwen3-8B-GGUF/tree/7c41481f57cb95916b40956ab2f0b139b296d974): Q4_K_M, 5.027.783.488 bytes.
- [Matrizes de Referência do Enem](https://www.gov.br/inep/pt-br/centrais-de-conteudo/acervo-linha-editorial/publicacoes-institucionais/avaliacoes-e-exames-da-educacao-basica/matrizes-de-referencia-enem): publicação institucional do Inep de 2026 usada para as quatro áreas, competências, habilidades e objetos de conhecimento.
- [Redação do Enem 2025 — Cartilha do participante](https://www.gov.br/inep/pt-br/centrais-de-conteudo/acervo-linha-editorial/publicacoes-institucionais/avaliacoes-e-exames-da-educacao-basica/redacao-do-enem-2025-cartilha-do-a-participante): fonte oficial para o componente separado de redação e suas cinco competências.

Os hashes dos modelos vieram dos metadados LFS oficiais. Os três modelos completos foram baixados, conferidos novamente por SHA-256 e aprovados com inferência real no Ryzen 7 5700X / RTX 3070 da máquina de referência. Tempos, memória, critérios e procedimento reproduzível estão em [HOMOLOGATION-WINDOWS.md](HOMOLOGATION-WINDOWS.md). O catálogo permanece na versão 2 e o perfil Leve usa o Q8 oficial disponível.

## Testes

`FakeLocalAiBackend` está somente no projeto `Rota.Windows.Tests`. Ele devolve respostas determinísticas, não faz inferência, não usa rede e não aparece na interface do usuário.

A suíte padrão contém 218 testes offline, incluindo falhas de rede simuladas, limites de resposta, cancelamento, rollback de atualização, exclusão mútua entre instaladores, comandos CPU/Vulkan, vínculo loopback, health check, timeout, encerramento, verificação pré-execução, autenticação da inferência, schema alinhado ao StudyPlan, IDs locais exclusivos, preferências aplicadas ao planejamento, limite seguro do histórico, as três etapas da configuração inicial com persistência atômica, análise de atrasos somente leitura, contraste dos temas, alvos de clique e carga real das onze janelas WPF. A etapa do primeiro plano testa o transporte estruturado da rotina e das dificuldades, validação de entrada, adiamento, IA indisponível, prévia, confirmação/aplicação e preservação de um plano existente. A análise de atrasos testa recorte temporal, exclusão de concluídos, limite dos detalhes, totais completos, revisões protegidas e ausência de mutação. Os diagnósticos testam hardware e armazenamento reais, composição sem efeitos colaterais, ciclo temporário do runtime, preservação de runtime já ativo, autenticação local, métricas, limites, classificação e cancelamento. A conversa contínua testa persistência, limites do contexto, cancelamento, vínculo com propostas, recuperação atômica e reconciliação após interrupção. O catálogo ENEM testa estrutura oficial, redação separada, ativação explícita, aliases, ênfase, limites, adulteração, envio ao backend e rejeição de matérias ou conteúdos inventados. A aplicação confirmada testa preparação sem escrita, expiração por mudança ou virada do dia, clique duplicado, recibo persistente, reinício, falha de gravação, reconciliação do histórico, recuperação atômica, operações estruturadas, carga concluída protegida e desfazer seguro. Dois gates opcionais podem ser ativados separadamente: `ROTA_TEST_RUNTIME_ARCHIVES` verifica todos os arquivos dos ZIPs oficiais byte a byte e inicia CPU/Vulkan com `--version`; `ROTA_TEST_LOCAL_AI_PERFORMANCE` executa a medição ponta a ponta com a instalação local e confirma por SHA-256 que configuração, conversa e propostas não mudaram. As gravações dos testes usam diretórios temporários exclusivos.

A `main` e a branch de desenvolvimento da IA disparam o Windows CI automaticamente em cada alteração relevante.

## Estado do ciclo

A fundação da IA local foi concluída no Rota Windows 0.3.0. A versão 0.3.1 ancorou a geração na data local e corrigiu cronogramas passados. A versão 0.3.2 aplica as preferências salvas ao novo plano, garante IDs locais exclusivos e reforça a usabilidade, o contraste e a estabilidade da interface.
