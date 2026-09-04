# Fundação da IA local do Rota Windows

Este namespace contém somente os contratos e serviços de base da IA local. Ele não é conectado à interface nesta etapa e não possui acesso ao `StudyRepository`.

## Limite de responsabilidade

O backend local recebe uma entrada estruturada e devolve uma `AiProposal`. Ele nunca aplica alterações no calendário. Uma proposta nova permanece em `Pending` mesmo depois da validação de contrato; o estado `Validated` fica reservado para uma etapa futura, depois que o motor determinístico do Rota produzir um preview válido.

Há dois formatos de proposta independentes:

- `AiStudyPlanDraft`: contém StudyPlan Code 0.2 e passa pelo `StudyPlanImporter` existente sem ser aplicado;
- `AiPlanChangeDraft`: contém operações estruturadas que o motor do Rota poderá interpretar futuramente.

## Configuração

`AiConfigurationStore` mantém a configuração separada em `%LOCALAPPDATA%\Rota\AI\config.json`. A escrita é atômica, cria uma cópia `.bak` e preserva arquivos inválidos antes de recuperar uma configuração íntegra ou criar os valores seguros padrão.

Os modelos e o runtime não fazem parte do estado ou dos backups do StudyPlan. Downloads, `llama.cpp`, detecção real de hardware e inferência ainda não são implementados.

## Testes

`FakeLocalAiBackend` está somente no projeto `Rota.Windows.Tests`. Ele devolve respostas determinísticas, não faz inferência, não usa rede e não aparece na interface do usuário.

## Próximo bloco

Implementar a detecção de CPU, RAM, GPU e VRAM por trás de `IAiHardwareProfileDetector`, mapear o resultado para os quatro perfis e testar os limites do modo automático sem instalar runtime ou modelos.
