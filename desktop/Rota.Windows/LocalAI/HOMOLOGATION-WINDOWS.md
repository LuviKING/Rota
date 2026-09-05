# Homologação da IA local no Windows

Homologação executada em 05/09/2026 com o instalador, verificador de integridade, detector de hardware, host do `llama-server`, backend e serviço de planejamento usados em produção. O ensaio gerou um StudyPlan Code 0.2 para o ENEM e não abriu nem gravou o calendário do Rota.

## Máquina de referência

- AMD Ryzen 7 5700X, 8 núcleos e 16 processadores lógicos;
- 15,9 GiB de memória física;
- NVIDIA GeForce RTX 3070, 8 GiB;
- driver NVIDIA 32.0.16.1664;
- perfil automático selecionado pelo Rota: **Desempenho**.

## Resultados reais

Os tempos de instalação abaixo usam os arquivos oficiais já presentes em cache local; incluem cópia, SHA-256, extração do runtime e ativação atômica, mas não simulam a velocidade da internet. Inicialização e geração são inferências reais. Memória é o pico observado no processo do runtime.

| Perfil | Modelo e execução | Instalação limpa | Inicialização | Geração | Pico de trabalho | Pico privado | Resultado |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| Leve | Qwen3 1.7B Q8_0, CPU, contexto 4096 | 7,4 s | 7,2 s | 84,2 s | 2,32 GiB | 0,68 GiB | aprovado |
| Equilibrado | Qwen3 4B Q4_K_M, CPU, contexto 4096 | 7,7 s | 10,7 s | 100,1 s | 4,74 GiB | 2,51 GiB | aprovado |
| Desempenho | Qwen3 8B Q4_K_M, Vulkan, contexto 8192 | 21,8 s | 17,4 s | 8,9 s | 5,19 GiB | 6,73 GiB | aprovado |

Na rodada final conjunta, o uso total informado pela NVIDIA subiu de 1.071 MiB para um pico de 7.084 MiB durante o perfil Desempenho, variação de 6.013 MiB. O Windows em modo WDDM não forneceu a memória por processo, por isso esse número é a variação total da placa e pode incluir pequenas oscilações de outros aplicativos.

Na homologação inicial da 0.3.0, todos os perfis receberam o pedido somente como texto livre, como ocorre na tela do Assistente IA, e produziram exatamente uma sessão válida de 30 minutos para `Matemática` / `Álgebra, funções, equações e gráficos`, na data solicitada e dentro do catálogo ENEM fixado. A rodada final conjunta passou nos três perfis.

## Regressão corrigida na 0.3.1

Um pedido genérico feito na interface conseguiu gerar uma proposta, mas o modelo atribuiu datas de 2025 às sessões. Como todas já estavam no passado em 05/09/2026, a prévia foi corretamente bloqueada e a pessoa não conseguia aplicar o plano.

A correção passou a enviar a data local de referência em todo pedido e a exigir que expressões relativas sejam calculadas a partir dela. Como defesa adicional, um cronograma devolvido no passado é deslocado integralmente para o futuro, preservando os dias da semana e respeitando a data do objetivo; se isso for impossível, a proposta continua bloqueada.

O pedido real relatado — `Quero me preparar pra prova do ENEM do ano que vem` — foi repetido no perfil Desempenho com o modelo Qwen3 8B e runtime Vulkan reais. Em 05/09/2026, o resultado foi um plano para o ENEM 2027 com 20 sessões futuras, 600 minutos e prévia aplicável. O runtime iniciou em 18,8 s e a geração terminou em 37,7 s. O calendário real não foi aberto nem alterado.

## Correções encontradas durante o ensaio

A primeira execução dentro do caminho longo do repositório foi encerrada pelo Windows com `0xC0000106` (`STATUS_NAME_TOO_LONG`). A raiz normal `%LOCALAPPDATA%\Rota\AI` não sofre desse problema; a ferramenta de homologação passou a ser executada em uma raiz temporária curta.

Depois de iniciar os modelos, a validação detectou que o schema de geração aceitava alguns textos maiores que o StudyPlan Code 0.2. Os limites de título, IDs, matéria, conteúdo, duração e meta foram alinhados ao importador. O nome e a data do objetivo agora são fixados no schema com os valores já validados informados pela pessoa; quando o ENEM é reconhecido no texto livre, o nome canônico também é fixado. Matérias e conteúdos ficam restritos aos nomes canônicos do catálogo, e cada proposta nova é limitada a 20 sessões para terminar integralmente dentro do contexto local. O conteúdo continuou sendo rejeitado até ficar válido; nenhuma saída foi truncada ou aceita por tolerância.

## Orçamento aprovado

O produto permanece limitado aos três perfis existentes. Leve exige 8 GiB de sistema e limita o modelo a 2 GiB; Equilibrado exige 12 GiB e limita o modelo a 3 GiB; Desempenho exige 15 GiB de sistema, 7,5 GiB de GPU e limita o modelo a 6 GiB. O manifesto falha na inicialização se um modelo fixado ultrapassar seu orçamento ou contexto.

## Repetição do teste

Os cinco artefatos devem estar previamente no diretório de cache com os nomes do manifesto. Em seguida:

```powershell
dotnet run --project .\desktop\Rota.Windows.Homologation\Rota.Windows.Homologation.csproj -c Release -- --root "$env:TEMP\RotaAI-Homologation" --cache .\artifacts\model-cache --output .\artifacts\homologation-results.json
```

O relatório JSON inclui hardware detectado, perfil automático, modelo, processamento, contexto, tempos, memória, quantidade de sessões, minutos planejados, resumo, erro e resultado por perfil. A ferramenta usa uma configuração e um calendário temporário isolados por perfil; ela não referencia `%LOCALAPPDATA%\Rota\desktop-state.json` nem o serviço que aplica propostas.

## Artefatos verificados

| Artefato | Bytes | SHA-256 |
| --- | ---: | --- |
| llama.cpp b10795 CPU x64 | 18.389.848 | `24b865773e7ef99996197a85a3b9bec88a9dedd75e490010d426e5a6a9353eab` |
| llama.cpp b10795 Vulkan x64 | 35.208.196 | `d6f81f2cbbaf457aa4392080a6b8d7675e0cb4ef0163ee26076439dfc956aab4` |
| Qwen3 1.7B Q8_0 | 1.834.426.016 | `061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a` |
| Qwen3 4B Q4_K_M | 2.497.280.256 | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| Qwen3 8B Q4_K_M | 5.027.783.488 | `d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785` |
