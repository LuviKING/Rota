# Rota

Rota é um planejador de estudos orientado por calendário para **Android e Windows**. O usuário descreve seu objetivo em linguagem natural para uma IA, importa um **StudyPlan Code** declarativo e acompanha no aplicativo exatamente o que precisa estudar em cada dia.

O mesmo modelo de plano, histórico protegido e revisões por conclusão real é compartilhado entre as duas edições.

## Estado atual

### Android

- Versão: **0.3.1**
- Android mínimo: **6.0 / API 23**
- Target/compile SDK: **API 36**
- Dados locais: **SQLite**
- Internet: o app atual não solicita permissão de Internet
- UI principal: agenda diária + calendário mensal

### Windows

- Versão: **0.3.2**
- Compatibilidade: **Windows 10 e Windows 11 x64**
- Tecnologia: **WPF / .NET 8**
- Distribuição CI: executável **self-contained**, sem exigir instalação separada do .NET
- Dados locais: `%LOCALAPPDATA%\Rota\desktop-state.json`, com troca atômica, cópia íntegra anterior e recuperação de corrupção
- UI principal: calendário mensal em grade + agenda detalhada do dia selecionado
- Tema: **escuro e claro**, com preferência local em `%LOCALAPPDATA%\Rota\theme.txt`
- Primeira abertura: boas-vindas, rotina guiada e primeiro plano pela IA local; dificuldades, objetivo, prazo, dias e horas seguem para uma proposta que só altera o calendário após prévia e confirmação final
- Navegação: meses, dias e retorno para hoje diretamente na tela principal
- Atrasos: sessões passadas ainda pendentes são identificadas em modo somente leitura, com quantidade, minutos e data mais antiga, sem reorganização automática
- Contexto mensal: plano ativo, objetivo, carga e progresso calculados dos dados reais
- Importação: editor, arquivo `.json` e arrastar/soltar StudyPlan na janela
- IA: assistente local/offline com perfis Leve, Equilibrado e Desempenho, instalação verificada, diagnóstico real de hardware/desempenho, conversa persistente, prévia, confirmação e desfazer seguro
- Backup: exportação do estado local pelas Configurações e proteção contra duas instâncias simultâneas

### Comportamento compartilhado

- Revisões automáticas: **D+1, D+3 e D+7**, calculadas a partir da data real de conclusão de sessões `study`;
- StudyPlan declarativo: nenhuma edição executa código recebido da IA;
- histórico concluído protegido contra importações futuras;
- revisões runtime preservadas quando o calendário futuro é atualizado.

## Princípios de segurança do plano

O Rota não executa código vindo da IA. A IA fornece apenas JSON declarativo e o app valida o conteúdo antes de alterar o calendário.

Regras centrais:

- histórico concluído é imutável;
- importações alteram apenas sessões de plano futuras e não concluídas;
- revisões automáticas já criadas pelo runtime são preservadas;
- sessões importadas no passado são ignoradas;
- um plano sem sessões futuras aplicáveis é recusado antes de qualquer remoção;
- o limite diário configurado considera também revisões automáticas futuras já agendadas;
- se `objective.date` estiver preenchida, sessões posteriores ao prazo são recusadas;
- a revisão de cada `plan.id` precisa crescer monotonamente, mesmo depois de alternar para outro plano;
- tipos JSON são validados estritamente: texto não é convertido de números e campos inteiros não aceitam strings ou frações;
- somente um objeto JSON é aceito; conteúdo adicional após o objeto é recusado;
- IDs de sessão não podem usar `::review::`, namespace reservado às revisões automáticas;
- a identidade persistida de uma sessão é o par **(`plan.id`, `session.id`)**, então planos diferentes podem reutilizar o mesmo ID externo sem sobrescrever histórico;
- IDs de sessões concluídas ou de revisões runtime não podem ser reutilizados dentro do mesmo plano para sobrescrever esses registros;
- a migração de bancos Android anteriores preserva o histórico concluído ao adotar a identidade por plano.

## Fluxo

```text
Usuário
  ↓
Comando em linguagem natural
  ↓
IA externa
  ↓
StudyPlan Code (JSON)
  ↓
StudyPlanImporter / validação
  ↓
StudyRepository / aplicação segura
  ↓
Calendário diário
  ↓
Conclusão real
  ↓
Revisões runtime D+1 / D+3 / D+7
  ↓
Histórico protegido
```

## StudyPlan Code 0.2

Exemplo mínimo:

```json
{
  "format": "studyplan",
  "format_version": "0.2",
  "plan": {
    "id": "pre-calculo-2026",
    "revision": 1,
    "title": "Pré-Cálculo"
  },
  "objective": {
    "name": "Dominar Pré-Cálculo",
    "date": "2026-09-30"
  },
  "sessions": [
    {
      "id": "precalc-2026-09-02-fatoracao",
      "date": "2026-09-02",
      "subject": "Matemática",
      "topic": "Fatoração e produtos notáveis",
      "minutes": 60,
      "target": "Resolver 10 exercícios e corrigir todos os erros",
      "kind": "study"
    }
  ]
}
```

`kind` aceita `study`, `review` e `assessment`. As revisões espaçadas normais **não devem ser geradas pela IA**; o próprio Rota cria D+1/D+3/D+7 quando uma sessão `study` é concluída. O marcador `::review::` é reservado ao runtime e não pode aparecer em IDs importados. IDs precisam ser únicos dentro de cada plano; planos diferentes podem usar o mesmo `session.id` com segurança.

Cada objeto de `sessions` representa uma sessão literal no calendário. O Rota não inventa sessões omitidas pela IA; por isso o prompt gerado pelo aplicativo exige que a IA produza o calendário completo necessário para executar o objetivo.

## Build Android

Requisitos:

- JDK 17
- Android SDK Platform 36
- Android Build Tools 35.0.0
- Gradle 8.11.1

Na raiz do projeto:

```powershell
gradle --no-daemon testDebugUnitTest
gradle --no-daemon lintDebug
gradle --no-daemon assembleDebug
```

APK debug:

```text
app/build/outputs/apk/debug/app-debug.apk
```

## Build Windows

Requisito para desenvolvimento: .NET SDK 8 em Windows 10/11.

```powershell
dotnet build .\desktop\Rota.Windows\Rota.Windows.csproj -c Release
dotnet run --project .\desktop\Rota.Windows\Rota.Windows.csproj
```

Publicação x64 self-contained:

```powershell
dotnet publish .\desktop\Rota.Windows\Rota.Windows.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false -o .\artifacts\Rota-Windows
```

Mais detalhes: `desktop/Rota.Windows/README-WINDOWS.md`.

## CI

### Android CI

Valida, nesta ordem:

1. testes unitários;
2. Android Lint com warnings tratados como erro;
3. build debug;
4. `zipalign`, assinatura e metadados do APK;
5. testes instrumentados de invariantes e migração + instalação e smoke test em Android 10 / API 29;
6. instalação, home e Configurações em Android 16 / API 36;
7. screenshots e artefato final somente quando todos os gates passam.

### Windows Desktop CI

Valida:

1. restore e build Release em runner Windows;
2. testes de invariantes do importador, repositório, histórico, revisões, configuração inicial e IA local, além da carga real das onze janelas WPF;
3. publicação `win-x64` self-contained em arquivo executável;
4. metadados e SHA-256 do executável;
5. smoke test de inicialização real do `Rota.exe`;
6. artefato final somente quando todos os gates passam.

## Assinatura e distribuição

Os artefatos Android atuais de CI são **debug-signed**. Isso é suficiente para testes, mas não é uma estratégia de distribuição estável. Uma publicação Android futura deve usar uma chave de release persistente armazenada fora do repositório, preferencialmente via GitHub Actions Secrets e/ou Play App Signing.

O executável Windows de desenvolvimento ainda não possui assinatura **Authenticode** comercial. Ele é funcional e validado pelo CI, mas o Windows SmartScreen pode exibir aviso de reputação na primeira execução. Uma distribuição pública definitiva deve usar um certificado de code signing persistente e protegido fora do repositório.

Nunca versione chaves privadas ou certificados com chave privada no Git.

