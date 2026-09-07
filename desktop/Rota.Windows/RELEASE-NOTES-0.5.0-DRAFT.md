# Rota para Windows 0.5.0 — Aprendizagem Completa

> Em desenvolvimento. Esta versão mantém o calendário e os dados locais já existentes e acrescenta a base segura para cursos, conteúdos, habilidades e pré-requisitos.

## Fase 1 — Fundação educacional

- Estrutura de dados permanente para matérias, cursos, módulos, aulas, conteúdos, habilidades e pré-requisitos.
- Migração automática do estado 0.4.x, sem apagar o calendário, histórico, configurações ou backups existentes.
- Identificadores estáveis e validação estrita da nova base para que os próximos recursos de aula, prova e memória possam referenciar o mesmo conteúdo com segurança.

## Compatibilidade

Os dados continuam locais em `%LOCALAPPDATA%\\Rota`. Antes de abrir a nova estrutura, o Rota valida a migração e conserva a cópia íntegra anterior para recuperação automática.
