# Rota Windows 0.3.0

Esta versão adiciona ao Rota para Windows um assistente de IA executado localmente, sem enviar o calendário ou a conversa para serviços externos.

## Principais novidades

- perfis Automático, Leve, Equilibrado e Desempenho, com seleção baseada no hardware;
- modelos Qwen3 fixados e runtime llama.cpp CPU/Vulkan com verificação de tamanho e SHA-256;
- instalação guiada, transparente e cancelável, sem download ao simplesmente abrir o aplicativo;
- conversa persistente com o assistente e cinco ações rápidas;
- catálogo ENEM local com matérias e conteúdos canônicos;
- propostas isoladas do calendário até a revisão e confirmação final;
- aplicação atômica, proteção do histórico e desfazer seguro;
- limites de memória, contexto e tamanho de modelo por perfil;
- inferência real homologada nos três perfis no Ryzen 7 5700X / RTX 3070 de referência.

## Instalação da IA

O EXE não contém os modelos. Ao escolher um perfil nas configurações da IA, o Rota mostra download e espaço necessário e pede confirmação antes de baixar. O perfil Desempenho usa a GPU quando o hardware atende aos limites; os demais funcionam por CPU.

## Validação da versão

- 179 testes offline do núcleo e das janelas WPF;
- 180 testes quando os pacotes oficiais CPU e Vulkan são conferidos;
- build Release sem avisos ou erros;
- publicação Windows x64 self-contained em arquivo único;
- smoke test de inicialização nos temas escuro e claro;
- inferência real em Leve, Equilibrado e Desempenho, sem alteração do calendário.

As medições completas estão em `LocalAI/HOMOLOGATION-WINDOWS.md` no código-fonte.

## Observação sobre o Windows

Este artefato ainda não possui assinatura Authenticode comercial. O SmartScreen pode mostrar um aviso de reputação na primeira execução. Confira o arquivo SHA-256 fornecido junto do executável.
