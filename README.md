# Cofre

Gerenciador de senhas **local** para Windows. A versão 1.3 tem sugestões nos campos dos sites, gerador configurável,
uma nova marca geométrica e uma janela principal para gerenciar entradas,
pastas, tags, favoritos, segurança e códigos TOTP, mantendo o painel rápido na bandeja. Inclui uma extensão
para Chrome e Edge com integração nativa local. Nada de nuvem, conta ou servidor de rede.

## Como usar

1. Execute `publish\1.3.2\Cofre.exe` ou o atalho **Cofre**. A publicação inclui o runtime necessário.
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

### Janela principal

A versão 1.2 usa uma interface de cantos retos, sem cartões arredondados, com grafite e verde oliva no tema escuro
e marfim e oliva no tema claro. A barra superior permite arrastar, minimizar, maximizar/restaurar e fechar a janela.
Fechar mantém o Cofre na bandeja; na primeira vez, uma notificação explica como reabrir ou encerrar o aplicativo.

A versão 1.2.2 abre a lista sem selecionar contas nem reservar espaço para os detalhes. A lista mostra site, pasta e data de atualização; ao selecionar uma conta, fica compacta para acomodar o painel lateral. Use o botão **Fechar detalhes** ou **Esc** para recolhê-lo. Busca, troca de seção e desbloqueio não abrem outra conta automaticamente.

O acabamento da versão 1.2.2 usa ações secundárias sem contorno, ícones consistentes e lápis ao lado de **Editar**. A cópia exibe uma confirmação junto ao botão. Descarte de alterações, exclusão e autorização do navegador usam diálogos próprios, com fundo escurecido e foco inicial na opção de cancelar. Esses diálogos fecham se o cofre bloquear; autorizações do navegador também fecham quando a solicitação expira.

Selecionar uma conta abre uma visualização com **Copiar usuário**, **Copiar senha**, **Exibir/Ocultar** e **Abrir site**.
O formulário aparece somente em **Editar** ou **+ Adicionar**. **Salvar** volta aos detalhes; **Cancelar** descarta
a edição com confirmação quando houver alterações. O bloqueio limpa os detalhes e a edição pendente.

O executável abre a janela principal. Pelo ícone da bandeja, escolha **Janela principal**; o clique simples e
Ctrl+Alt+P continuam abrindo o painel rápido. Fechar a janela mantém o aplicativo na bandeja; **Sair** encerra tudo.

- Crie, edite e exclua entradas; organize-as com **Pasta**, **Tags** e **Favorito**.
- A busca encontra título, usuário, URL, pasta e tags. Clique numa pasta para filtrar; **Minhas contas** remove o filtro.
- **Segurança** mostra contagens e as entradas com senhas fracas ou repetidas. A avaliação de força é heurística,
  feita localmente; não verifica vazamentos. “Sem TOTP cadastrado” não determina se o site tem 2FA habilitado.
- **Autenticador** filtra entradas com TOTP. Nas propriedades de uma entrada, informe a chave Base32 ou a URI
  `otpauth://totp/...` fornecida pelo serviço e salve. O código atual e o tempo restante aparecem no topo dos detalhes.
  Aceita SHA1, SHA256 e SHA512, 6 ou 8 dígitos e períodos entre 15 e 120 segundos. Mantenha o relógio do Windows correto.
- As chaves TOTP são criptografadas junto com o cofre. Esta versão recebe texto; não lê imagens QR.
- Ao bloquear, a lista, os campos em edição e os códigos são limpos. Alterações não salvas são descartadas no bloqueio.

### Extensão para Chrome e Edge

Esta versão é instalada em **modo de desenvolvedor**; ainda não está publicada nas lojas dos navegadores.

Na versão 1.3.2, selecionar uma conta preenche o login diretamente, sem uma segunda confirmação no Windows.
O domínio e a porta continuam sendo verificados. Se o cofre estiver bloqueado, o painel de desbloqueio abre;
depois da senha mestra, a solicitação continua enquanto estiver válida. Salvar e atualizar contas ainda exigem confirmação.

Na versão 1.3.1, a ativação dos ícones funciona de forma independente das solicitações ao aplicativo, inclusive
enquanto uma confirmação estiver pendente ou o cofre estiver bloqueado. O popup reativa os ícones em sites já
autorizados e informa quantos campos visíveis encontrou. Confirmações do navegador aparecem em destaque no Windows,
com título próprio na barra de tarefas. Depois de atualizar a extensão, recarregue também a página do site (F5).

1. Compile e registre a integração com `pwsh -File tools\publish.ps1` (requer SDK .NET 10). Nesta instalação isso já foi feito.
2. No Chrome, abra `chrome://extensions`; no Edge, `edge://extensions`.
3. Ative **Modo do desenvolvedor** e selecione **Carregar sem compactação** / **Carregar descompactada**.
4. Selecione a pasta `extension` deste repositório. Fixe o ícone **Cofre** na barra do navegador. Se já instalou,
   use **Recarregar** na página de extensões para receber a versão 1.3 e a nova logo.
5. Em cada site HTTPS que deseja usar, abra o popup e clique **Ativar ícones neste site**. Autorize o acesso solicitado
   pelo navegador. Essa autorização vale para o hostname; a busca de contas ainda confere origem e porta exatas.
   Nas próximas visitas, os ícones aparecem automaticamente. Remova o acesso nas permissões da extensão quando quiser.
6. Abra e desbloqueie o aplicativo Windows. Na entrada, cadastre a URL completa, por exemplo `https://example.com/login`.
7. Clique no campo de senha ou na logo do Cofre dentro dele:
   - **Login:** a lista de contas desse domínio aparece. Escolha a conta para preencher diretamente no **domínio exato**.
     Se necessário, desbloqueie o cofre no aplicativo para continuar. Apenas focar o campo não libera senhas salvas.
   - **Cadastro:** o painel oferece uma senha aleatória de 20 caracteres. Em **Personalizar senha**, ajuste tamanho
     (12 a 128), letras maiúsculas/minúsculas, números, símbolos permitidos e filtro de caracteres ambíguos. As
     preferências são lembradas neste navegador. **Usar esta senha** também preenche a confirmação quando identificada.
   - **Troca de senha:** os campos de senha nova e confirmação recebem a sugestão; a senha atual é preservada.
8. Use **Salvar no Cofre antes de enviar o cadastro**, para guardar a senha mesmo se a página redirecionar. O seletor
   oferece criar uma conta ou atualizar uma conta existente desse domínio. A gravação exige confirmação no app;
   atualizar mantém notas, pasta, tags, favorito e TOTP. O site pode rejeitar o cadastro; confira o resultado no site.
9. O popup da barra do navegador continua disponível para preencher e salvar manualmente. **Ativar ícones neste site**
   também injeta o assistente em uma página que já estava aberta, sem recarregá-la.

O assistente usa `autocomplete`, quantidade de senhas e sinais do formulário para distinguir login, cadastro e troca.
A identificação é heurística: sites com campos ou regras incomuns podem exigir o popup ou o app desktop. Não há
leitura automática de senhas digitadas nem captura de envios; salvar exige o botão explícito. A geração funciona
mesmo com o app bloqueado; listar contas, preencher e salvar dependem do app aberto e desbloqueado.

A conexão usa Native Messaging e um named pipe restrito ao usuário atual do Windows, sem porta HTTP. O registro é
feito somente em HKCU para Chrome e Edge. A chave pública em `extension/manifest.json` mantém o ID de desenvolvimento
`ligdohljiagpoepikbojmfkfflenndlb` estável; ela não é uma senha nem uma chave de criptografia do cofre.

Restrições deliberadas do preenchimento:

- Somente HTTPS, origem exata (domínio e porta) e página principal. Cada ícone identifica seu próprio formulário,
  inclusive quando existem vários na página. Não confia em títulos
  de janela nem em correspondências parciais de domínio. Subdomínios diferentes precisam de entradas próprias.
- Recusa formulários com destino em outra origem e confere novamente a URL antes de preencher. Nunca envia o formulário.
- Sem preenchimento de iframes, Shadow DOM, fluxos de várias etapas ou passkeys nesta versão.
- O acesso contínuo é autorizado individualmente por site. O armazenamento local contém apenas preferências do
  gerador; senhas do cofre não são retornadas ao content script. Sugestões geradas ficam na memória do painel por
  até dois minutos e são limpas ao fechá-lo. Não há cópia automática para a área de transferência.
- Até 20 logins são listados por origem; entradas com usuário acima de 1.000 caracteres ou senha acima de 4.096 devem
  ser usadas pelo aplicativo. Solicitações de desbloqueio e confirmação expiram; se demorar, solicite novamente.

Se a conexão falhar, confira se o aplicativo está aberto e desbloqueado e execute
`pwsh -File tools\register-extension.ps1`. Para remover o registro: `pwsh -File tools\register-extension.ps1 -Uninstall`.
Recarregue a extensão após atualizar seus arquivos. Um badge **OK** indica operação concluída; **!** indica falha,
com o motivo no texto exibido ao passar o mouse sobre o ícone. O Native Messaging não protege de malware que já
controla a mesma conta do Windows.

Se a versão antiga estiver aberta, encerre-a pelo menu **Sair** da bandeja antes de abrir a versão nova.

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
  src\Cofre.NativeHost ponte stdio entre navegador e named pipe do aplicativo
  extension\       extensão Manifest V3 para Chrome/Edge, sem etapa de build
  tests\           testes xUnit do núcleo e integração do executável nativo
  tools\           publicação, registro da extensão e geração dos ícones
```

```
dotnet test tests\Cofre.Core.Tests                                   # testes
dotnet run --project src\Cofre.App                                   # executar em desenvolvimento
pwsh -File tools\publish.ps1                                       # publica app + host e registra ambos os navegadores
node --test extension/tests/*.test.mjs                              # testes da extensão com APIs/DOM simulados
```

Parâmetros úteis do executável: `--data-dir <pasta>` (guarda cofre e configurações em outra pasta, ex.: pendrive) e
`--show` (abre o painel ao iniciar). Em builds **Debug** existe também `--render <pasta> --password <senha>`, que
desenha todas as telas em PNG sem abrir janela, útil para revisar o visual.

`tools\publish.ps1` publica uma versão independente do runtime em `publish\1.3.2` e o host em sua subpasta `native`.
As bibliotecas nativas do WPF são incluídas com `IncludeNativeLibrariesForSelfExtract=true`; o runtime as extrai ao
iniciar. Isso evita uma publicação que compila, mas encerra ao criar a janela por falta de DLLs nativas.
Os atalhos da Área de Trabalho e do menu Iniciar são atualizados durante a publicação. Aceita `-Dotnet <caminho-do-SDK>` e `-OutputDirectory <pasta>`. Feche a versão de destino antes de recompilar.
Para uma instalação personalizada, registre com `tools\register-extension.ps1 -AppDirectory <pasta-do-Cofre.exe>`.

Para os testes do host publicado no PowerShell:

```powershell
$env:COFRE_NATIVE_HOST = Join-Path (Get-Location) 'publish\1.3.2\native\Cofre.NativeHost.exe'
$env:COFRE_TEST_IDENTITY = [Environment]::UserDomainName + '\' + [Environment]::UserName
node --test tests/native-host.test.mjs
```

Execute esse teste com a versão 1.2 fechada: ele usa um servidor de pipe simulado para conferir enquadramento,
registro e identificação do chamador. Os testes JS simulam navegador e DOM; não substituem uma verificação manual
em Chrome/Edge após carregar a extensão. Os testes TOTP cobrem todos os vetores do apêndice B da
[RFC 6238](https://www.rfc-editor.org/rfc/rfc6238.html). A integração segue a documentação de
[Native Messaging do Chrome](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) e do
[Microsoft Edge](https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/developer-guide/native-messaging).

Para os testes de navegador, instale as dependências com `npm ci --prefix extension` e execute:

```powershell
$env:COFRE_TEST_BROWSER = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$env:COFRE_TEST_EXTENSION_BROWSER = $env:COFRE_TEST_BROWSER
node --test extension/tests/*.test.mjs
```

Esses testes abrem Edge headless com perfil temporário e páginas fictícias em `cofre.test`; não usam abas pessoais.
Uma cópia descartável da extensão recebe permissão somente para o domínio fictício. O teste exercita a extensão
MV3 real, seus mundos isolados e o worker; somente o transporte Native Messaging é simulado, com contas fictícias.
Os testes do núcleo cobrem a confirmação para salvar/atualizar, bloqueio e mudanças concorrentes de entradas.
Sem as variáveis de ambiente, os testes de navegador são pulados. Prévias das páginas ficam em `test-results/`, ignorada
pelo Git. `tools/make-icons.ps1` gera a mesma marca para o executável, bandeja e extensão, sem ferramentas externas.
