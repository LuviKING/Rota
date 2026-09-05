# Rota Windows 0.3.1

Correção do fluxo de geração de planos da Assistente IA local.

## Corrigido

- a data local atual agora acompanha todo pedido enviado ao modelo;
- expressões como “amanhã” e “ano que vem” são interpretadas a partir dessa data;
- se o modelo devolver sessões datadas no passado, o Rota realoca o cronograma para o futuro, preservando os mesmos dias da semana;
- a correção é bloqueada quando o cronograma não cabe antes da data do objetivo;
- “Ajustar plano atual” e suas ações rápidas ficam indisponíveis até existir um plano aplicado com sessões futuras;
- a homologação usa um pedido realista de plano para o ENEM do próximo ano e exige uma prévia aplicável.

## Validação

- 181 testes offline aprovados e o teste adicional dos pacotes oficiais aprovado;
- build Release sem avisos ou erros;
- inferência real no perfil Desempenho usando o pedido que apresentou o problema;
- 20 sessões futuras e 600 minutos produzidos para o ENEM 2027;
- prévia do calendário aprovada sem modificar os dados reais do usuário.

O executável ainda não possui assinatura Authenticode comercial. Confira o SHA-256 fornecido junto do pacote.
