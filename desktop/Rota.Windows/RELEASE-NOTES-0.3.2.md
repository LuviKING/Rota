# Rota Windows 0.3.2

Atualização de qualidade, estabilidade e acabamento da edição Windows.

## Corrigido

- novos planos da Assistente IA agora respeitam os dias disponíveis, as horas diárias e o tamanho de bloco salvos nas Configurações;
- cada plano criado pela IA recebe uma identidade local exclusiva, evitando bloqueios ao gerar mais de um plano;
- fechar a Assistente IA cancela imediatamente uma geração em andamento;
- o histórico de propostas libera espaço com segurança quando atinge o limite, preservando propostas ativas e aplicadas;
- o resumo mensal conta matérias distintas restantes em vez de contar sessões;
- falhas ao abrir a pasta de dados são exibidas de forma controlada;
- o identificador de versão usado nos downloads acompanha a versão real do aplicativo.

## Polimento visual

- contraste de textos, ícones e estados reforçado nos temas escuro e claro;
- fundo da confirmação da IA corrigido no tema escuro;
- tamanhos, alinhamento e áreas clicáveis dos controles validados em todas as janelas;
- caixas de seleção seguem o tema e possuem alvo de clique mais confortável;
- janela principal passa a caber em áreas úteis a partir de 960 px de largura;
- campo de pedido da IA mostra o limite de 8.000 caracteres e melhora a digitação em português.

## Diagnóstico local

- a opção **Testar minha IA** mostra o estado real da instalação e o perfil ativo;
- processador, memória, placa de vídeo, armazenamento e perfil recomendado são analisados localmente;
- o novo botão **Medir agora** carrega a IA somente após o clique e executa uma resposta curta e descartável;
- o resultado informa tempo de inicialização, duração da resposta, tokens por segundo, perfil, CPU/GPU e uma avaliação simples;
- o teste não cria proposta, não grava conversa e não altera o calendário; um runtime iniciado somente para a medição é encerrado ao final.

## Validação

- 203 testes offline aprovados;
- gates adicionais para os pacotes oficiais CPU/Vulkan e para uma medição ponta a ponta com a IA instalada;
- todas as janelas WPF carregadas nos tamanhos mínimos em temas escuro e claro;
- build Release sem avisos ou erros;
- publicação `win-x64` self-contained e smoke tests do executável final.

O executável ainda não possui assinatura Authenticode comercial. Confira o SHA-256 fornecido junto do pacote.
