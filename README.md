# Cofre

Gerenciador de senhas **local** para Windows. Fica na bandeja (ícones ocultos, perto do relógio), sem janela
principal: um painel pequeno aparece quando você precisa de uma senha e some sozinho. Nada de nuvem, conta ou servidor.

## Como usar

1. Execute `publish\Cofre.exe` (precisa do **.NET 10 Desktop Runtime**, já presente nesta máquina).
2. Na primeira vez abre a tela **Crie seu cofre**: defina a senha mestra (mínimo 10 caracteres).
3. O ícone do cofre fica na bandeja. Para abrir o painel:
   - clique no ícone, ou
   - pressione o atalho global **Ctrl + Alt + P** (muda em Configurações), ou
   - execute o `Cofre.exe` de novo (um atalho na área de trabalho serve de botão "abrir cofre").
4. Digite para buscar. Em cada entrada:
   - **Copiar usuário** / **Copiar senha**: a área de transferência é limpa depois de 20 s (configurável);
   - **Digitar**: o app digita *usuário, Tab, senha, Enter* na janela onde você estava antes de abrir o painel
     (funciona em qualquer programa, não só no navegador). `Enter` na lista faz o mesmo; `Ctrl+Enter` copia a senha.
   - **Editar**.
5. Se você abrir o painel estando numa página/janela cujo título combina com uma entrada (ex.: "Gmail"), ela aparece
   em **Sugeridas para esta janela**.

Outras funções: gerador de senhas (tamanho, tipos de caractere, evitar ambíguos), favoritas, bloqueio automático por
inatividade / ao bloquear o Windows / ao suspender, iniciar com o Windows, tema claro/escuro, backup do cofre e troca da
senha mestra.

Dicas de uso do "Digitar": clique no campo de login **antes** de abrir o painel. Se o app não achar onde digitar, ele
copia a senha para você colar com `Ctrl+V`. Programas que bloqueiam digitação simulada (alguns bancos, jogos com
anti-cheat) exigem copiar e colar.

## Segurança

| Item | Como é feito |
|---|---|
| Criptografia | AES-256-GCM sobre o cofre inteiro (autenticada: qualquer alteração no arquivo é detectada) |
| Chave | Argon2id (64 MiB, 3 passadas, 4 threads) derivada da senha mestra + salt aleatório de 16 bytes |
| Senha mestra | Nunca é gravada, nem como hash |
| Arquivo | `%APPDATA%\Cofre\cofre.dat` (cabeçalho com parâmetros KDF/salt/nonce autenticado como AAD); novo nonce a cada gravação |
| Gravação | Atômica (arquivo temporário + `File.Replace`), mantendo `cofre.dat.bak` da versão anterior |
| Em memória | A chave só existe com o cofre aberto; ao bloquear ela é zerada |
| Área de transferência | Fora do histórico do Windows e da sincronização em nuvem; limpa por tempo |
| Tentativas | Argon2 já encarece cada tentativa; erros seguidos na tela de desbloqueio ganham um atraso crescente |

Limites conhecidos (sendo honesto):

- **Sem recuperação.** Esqueceu a senha mestra, os dados são irrecuperáveis. Faça backup do `cofre.dat` (Configurações →
  Backup) e guarde a senha mestra num lugar seguro.
- Strings do .NET não podem ser apagadas da memória de forma garantida; as senhas descriptografadas ficam em memória
  enquanto o cofre está aberto, e o bloqueio automático existe para encurtar essa janela.
- Quem já controla seu Windows (malware com keylogger, por exemplo) está fora do que um gerenciador local consegue
  proteger.

## Desenvolvimento

```
Passwords\
  src\Cofre.Core   biblioteca: criptografia, formato do arquivo, cofre, gerador, busca (sem dependência de UI)
  src\Cofre.App    app WPF (WPF-UI): bandeja, painel, atalho global, digitação automática
  tests\           testes xUnit do núcleo (55 testes)
  tools\           make-icons.ps1 gera os ícones/logo
```

```
dotnet test tests\Cofre.Core.Tests                                   # testes
dotnet run --project src\Cofre.App                                   # executar em desenvolvimento
dotnet publish src\Cofre.App -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
```

Parâmetros úteis do executável: `--data-dir <pasta>` (guarda cofre e configurações em outra pasta, ex.: pendrive) e
`--show` (abre o painel ao iniciar). Em builds **Debug** existe também `--render <pasta> --password <senha>`, que
desenha todas as telas em PNG sem abrir janela, útil para revisar o visual.

Para um executável que não depende do runtime instalado, troque `-p:SelfContained=false` por `-p:SelfContained=true`
(o arquivo passa de ~7 MB para ~150 MB).
