# Rota Windows 0.4.0

Atualização focada em acompanhamento de progresso, organização segura do calendário e distribuição mais simples no Windows.

## Progresso e rotina

- configuração inicial guiada para objetivo, prazo, dias e horas disponíveis;
- identificação e recuperação assistida de blocos atrasados;
- lembrete diário opcional pelo Agendador de Tarefas do Windows;
- resumo semanal, progresso por matéria e histórico mensal com comparação entre semanas.

## Calendário mais flexível

- sessões pendentes podem ser arrastadas para outro dia;
- prazo, dias disponíveis e limite diário são revalidados antes de mover;
- sessões concluídas e revisões automáticas continuam protegidas;
- cada movimento exibe uma prévia, pede confirmação e pode ser desfeito enquanto nenhuma alteração posterior tiver ocorrido.

## Instalação

- novo instalador por usuário, sem exigir permissão de administrador;
- atalhos no menu Iniciar e, opcionalmente, na área de trabalho;
- atualização sobre a mesma instalação e desinstalação registradas no Windows;
- a desinstalação remove o aplicativo e os atalhos, mas preserva os planos e o histórico local.

## Atualizações

- nova opção **Verificar atualizações** nas Configurações;
- as novidades e o tamanho do pacote são mostrados antes de qualquer download;
- baixar e instalar exige confirmação explícita;
- tamanho e SHA-256 são conferidos antes de abrir o instalador;
- qualquer falha mantém a versão atual intacta e remove downloads parciais.

## Assinatura digital

- pipeline Authenticode preparado para aplicativo, instalador e desinstalador;
- SHA-256 e timestamp RFC 3161 são obrigatórios para uma publicação assinada;
- o certificado e a senha ficam somente nos Secrets do GitHub e no runner temporário;
- enquanto o certificado comercial não for configurado, o pacote é identificado como artefato de desenvolvimento não assinado.

O aplicativo continua self-contained e não exige a instalação separada do .NET 8. Os modelos da IA local continuam sendo baixados somente após confirmação dentro do Rota.

## Validação

- 241 testes offline aprovados;
- 18 janelas WPF carregadas no tamanho mínimo com controles acessíveis;
- build Release sem avisos ou erros;
- publicação x64 self-contained;
- smoke tests nos temas escuro e claro;
- instalação, atualização sobre a mesma pasta, abertura da cópia instalada e desinstalação verificadas automaticamente.
