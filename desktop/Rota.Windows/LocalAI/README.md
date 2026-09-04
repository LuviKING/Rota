# Fundação da IA local do Rota Windows

Este namespace contém somente os contratos e serviços de base da IA local. Ele não é conectado à interface nesta etapa e não possui acesso ao `StudyRepository`.

## Limite de responsabilidade

O backend local recebe uma entrada estruturada e devolve uma `AiProposal`. Ele nunca aplica alterações no calendário. Uma proposta nova permanece em `Pending` mesmo depois da validação de contrato; o estado `Validated` fica reservado para uma etapa futura, depois que o motor determinístico do Rota produzir um preview válido.

Há dois formatos de proposta independentes:

- `AiStudyPlanDraft`: contém StudyPlan Code 0.2 e passa pelo `StudyPlanImporter` existente sem ser aplicado;
- `AiPlanChangeDraft`: contém operações estruturadas que o motor do Rota poderá interpretar futuramente.

## Configuração

`AiConfigurationStore` mantém a configuração separada em `%LOCALAPPDATA%\Rota\AI\config.json`. A escrita é atômica, cria uma cópia `.bak` e preserva arquivos inválidos antes de recuperar uma configuração íntegra ou criar os valores seguros padrão.

Os modelos e o runtime não fazem parte do estado ou dos backups do StudyPlan. A instalação local é preparada por um serviço separado e nunca acontece automaticamente ao abrir o Rota. O ciclo de vida do `llama.cpp` existe como serviço isolado, mas ainda não é acionado pela interface; a inferência e o chat ainda não são implementados.

## Perfis de hardware

`WindowsAiHardwareProfileDetector` lê CPU, processadores lógicos, RAM física e adaptadores de vídeo diretamente das APIs locais do Windows. A GPU com mais memória dedicada é usada, adaptadores de software são ignorados e uma falha de enumeração de GPU produz aviso e fallback seguro para CPU/RAM.

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

Antes de qualquer execução, `AiInstallationIntegrityVerifier` confere novamente o recibo, o modelo, o ZIP fixado do runtime e todos os arquivos extraídos. Arquivo ausente, modificado, adicional ou redirecionado bloqueia a inicialização. Instalações criadas pelo formato anterior, que não conservavam o ZIP, precisam ser reinstaladas antes de executar. `Ready` no gerenciador continua significando apenas arquivos presentes; a confirmação operacional é o estado separado do runtime. Nenhum download é iniciado pela interface nesta etapa.

## Ciclo de vida do llama-server

`LocalAiRuntimeHost` controla uma única instância pertencente ao Rota. Ele monta os argumentos sem usar shell, remove variáveis ambientes `LLAMA_ARG_*`, escolhe CPU ou Vulkan conforme o pacote realmente instalado e inicia o processo oculto no diretório isolado da instalação.

O servidor recebe explicitamente `--host 127.0.0.1`, uma porta local livre, o modelo validado, o contexto configurado, paralelismo 1 e `--n-gpu-layers 0` para CPU ou `all` para Vulkan. Cada execução cria uma chave de API aleatória mantida somente em memória e restringe CORS a `localhost`, reduzindo acesso indevido por outros programas ou páginas locais. O endpoint nunca usa a rede externa. A porta pode sofrer uma corrida entre sua escolha e o bind do processo; nesse caso o processo falha, é encerrado e o erro fica controlado para uma nova tentativa.

O estado percorre `Stopped`, `Starting`, `Ready`, `Stopping` ou `Faulted`. A inicialização somente retorna sucesso depois de `GET /health` responder HTTP 200 com `{"status":"ok"}`. Respostas de carregamento, redirecionamentos, conteúdo inválido, processo encerrado e excesso de tempo não são tratados como prontos. Cancelamento, timeout, parada e descarte encerram toda a árvore do processo pertencente ao Rota. A saída nativa capturada é limitada para não crescer indefinidamente.

O health check segue a interface documentada do `llama-server` b10795. O serviço ainda não envia prompts, não ativa ferramentas internas do servidor e não tem qualquer referência ao `StudyRepository`.

### Fontes fixadas e verificadas em 04/09/2026

- [llama.cpp b10795](https://github.com/ggml-org/llama.cpp/releases/tag/b10795): pré-release oficial, CPU/Vulkan Windows x64. Ambos os ZIPs foram baixados para testes isolados, tiveram tamanho e SHA-256 conferidos, passaram pela extração do instalador e iniciaram com `--version` pelo controlador real do Rota. Nenhum modelo completo ou inferência foi executado.
- [documentação do llama-server b10795](https://github.com/ggml-org/llama.cpp/blob/b10795/tools/server/README.md): fonte dos argumentos de host, porta, camadas de GPU e do contrato `GET /health`.
- [Qwen3 1.7B](https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/tree/90862c4b9d2787eaed51d12237eafdfe7c5f6077): Q8_0, 1.834.426.016 bytes.
- [Qwen3 4B](https://huggingface.co/Qwen/Qwen3-4B-GGUF/tree/bc640142c66e1fdd12af0bd68f40445458f3869b): Q4_K_M, 2.497.280.256 bytes.
- [Qwen3 8B](https://huggingface.co/Qwen/Qwen3-8B-GGUF/tree/7c41481f57cb95916b40956ab2f0b139b296d974): Q4_K_M, 5.027.783.488 bytes.

Os hashes dos modelos vieram dos metadados LFS oficiais e os endereços/tamanhos foram conferidos via HEAD. Os modelos completos não foram baixados nem testados com inferência. O catálogo passou à versão 2: o identificador provisório Leve Q4 do bloco anterior foi corrigido para o Q8 oficial disponível. O orçamento de memória desse modelo deve ser medido antes da liberação da interface.

## Testes

`FakeLocalAiBackend` está somente no projeto `Rota.Windows.Tests`. Ele devolve respostas determinísticas, não faz inferência, não usa rede e não aparece na interface do usuário.

A suíte padrão contém 95 testes offline, incluindo falhas de rede simuladas, limites de resposta, cancelamento, rollback de atualização, exclusão mútua entre instaladores, comandos CPU/Vulkan, vínculo loopback, health check, timeout, encerramento e verificação pré-execução. Para conferir os ZIPs oficiais já baixados, definir `ROTA_TEST_RUNTIME_ARCHIVES` para a pasta que os contém habilita o 96º teste, que verifica todos os arquivos extraídos byte a byte por hash. As gravações dos testes usam diretórios temporários exclusivos.

A branch `feat/windows-local-ai` agora dispara o Windows CI automaticamente em cada push relevante. Os checkpoints permanecem nessa branch até autorização de integração.

## Próximo bloco

Implementar o cliente de inferência local sobre o endpoint compatível de chat do `llama-server`: prompt de sistema limitado, entrada estruturada, saída JSON restrita aos contratos do Rota, limite de resposta, cancelamento e validação completa antes de criar uma `AiProposal`. A interface e a aplicação de propostas no calendário devem continuar fora desse bloco.
