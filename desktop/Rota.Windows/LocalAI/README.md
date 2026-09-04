# Fundação da IA local do Rota Windows

Este namespace contém somente os contratos e serviços de base da IA local. Ele não é conectado à interface nesta etapa e não possui acesso ao `StudyRepository`.

## Limite de responsabilidade

O backend local recebe uma entrada estruturada e devolve uma `AiProposal`. Ele nunca aplica alterações no calendário. Uma proposta nova permanece em `Pending` mesmo depois da validação de contrato; o estado `Validated` fica reservado para uma etapa futura, depois que o motor determinístico do Rota produzir um preview válido.

Há dois formatos de proposta independentes:

- `AiStudyPlanDraft`: contém StudyPlan Code 0.2 e passa pelo `StudyPlanImporter` existente sem ser aplicado;
- `AiPlanChangeDraft`: contém operações estruturadas que o motor do Rota poderá interpretar futuramente.

## Configuração

`AiConfigurationStore` mantém a configuração separada em `%LOCALAPPDATA%\Rota\AI\config.json`. A escrita é atômica, cria uma cópia `.bak` e preserva arquivos inválidos antes de recuperar uma configuração íntegra ou criar os valores seguros padrão.

Os modelos e o runtime não fazem parte do estado ou dos backups do StudyPlan. Downloads, `llama.cpp` e inferência ainda não são implementados.

## Perfis de hardware

`WindowsAiHardwareProfileDetector` lê CPU, processadores lógicos, RAM física e adaptadores de vídeo diretamente das APIs locais do Windows. A GPU com mais memória dedicada é usada, adaptadores de software são ignorados e uma falha de enumeração de GPU produz aviso e fallback seguro para CPU/RAM.

`AiProfileRecommendationPolicy` mantém os critérios determinísticos e separados da detecção:

- `Lightweight`: fallback seguro para computadores com menos recursos;
- `Balanced`: pelo menos 12 GiB de RAM e CPU com 6 processadores lógicos ou GPU com 4 GiB;
- `Performance`: pelo menos 15 GiB de RAM, 8 processadores lógicos e 7,5 GiB de VRAM;
- `Automatic`: resolve para uma das três opções acima; a política nunca recomenda algo superior a `Performance`.

A seleção manual continua sendo respeitada. Os limites identificam o PC-alvo Ryzen 7 5700X, RTX 3070 8 GB e 16 GB de RAM como `Performance`, sem criar perfil para modelos acima de 7B–8B Q4.

## Catálogo e estado dos modelos

`AiModelCatalog` é um catálogo local, imutável e versionado. Ele contém um modelo GGUF Q4_K_M recomendado para cada perfil concreto:

- `Lightweight`: Qwen3 1.7B;
- `Balanced`: Qwen3 4B;
- `Performance`: Qwen3 8B.

O catálogo não contém URL, credencial ou código de download. IDs e nomes de arquivo são validados para impedir duplicidade e caminhos relativos ou com travessia de diretório.

`LocalAiModelManager` resolve o perfil automático usando o detector do bloco anterior, respeita uma escolha manual e calcula o estado real a partir dos arquivos locais. O resultado diferencia runtime e modelo disponíveis e só informa `Ready` quando ambos são arquivos não vazios. A inspeção não cria diretórios, não altera a configuração persistida e não executa nenhum binário.

## Testes

`FakeLocalAiBackend` está somente no projeto `Rota.Windows.Tests`. Ele devolve respostas determinísticas, não faz inferência, não usa rede e não aparece na interface do usuário.

## Próximo bloco

Definir um manifesto verificável para os artefatos do runtime/modelo e preparar o fluxo de instalação com staging, integridade e cancelamento, ainda sem iniciar inferência nem conectar uma tela incompleta à interface principal.
