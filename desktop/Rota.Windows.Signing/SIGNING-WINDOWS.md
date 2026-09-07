# Assinatura digital do Rota Windows

O pipeline está preparado para assinar com Authenticode o `Rota.exe`, o instalador e o desinstalador embutido. O certificado e sua senha nunca pertencem ao repositório.

## Requisitos para a distribuição pública

- certificado comercial de **Code Signing** em formato PFX;
- senha forte do PFX;
- Windows SDK com `signtool.exe`;
- timestamp RFC 3161 disponível.

O processo usa SHA-256 para o arquivo e para o timestamp. Sem timestamp, a assinatura deixa de ser válida quando o certificado expira.

## Segredos do GitHub

Cadastre em **Settings > Secrets and variables > Actions**:

- `WINDOWS_CERTIFICATE_BASE64`: conteúdo Base64 do PFX;
- `WINDOWS_CERTIFICATE_PASSWORD`: senha do PFX.

Base64 não é criptografia; a proteção vem do armazenamento de Secrets do GitHub. Não grave a Base64, a senha ou o PFX em arquivos versionados.

Com os dois segredos presentes, o workflow:

1. recria o PFX apenas na pasta temporária do runner;
2. importa o certificado na conta temporária;
3. assina e carimba o executável;
4. compila o instalador assinando também o desinstalador embutido;
5. valida a confiança Authenticode de todos os executáveis externos;
6. apaga o PFX e remove do armazenamento local somente o certificado importado por esse job.

Sem esses segredos, o mesmo workflow continua gerando artefatos de desenvolvimento não assinados e informa claramente o estado.

## Execução local

```powershell
.\desktop\Rota.Windows.Signing\Build-WindowsRelease.ps1 `
  -PublishDir .\artifacts\win-x64 `
  -OutputDir .\artifacts\installer `
  -Version 0.4.0 `
  -PfxPath C:\segredos\rota-code-signing.pfx `
  -CertificatePassword 'senha-fornecida-fora-do-repositorio'
```

O script recusa certificados expirados ou sem a finalidade Code Signing. `-SkipTimestamp` e `-AllowUntrustedCertificate` existem somente para homologação local com certificado de teste; não devem ser usados em uma versão pública.
