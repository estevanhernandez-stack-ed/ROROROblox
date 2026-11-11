# Store listing — Português (Brasil) (pt-BR)

> Paste-ready para Partner Center → Store listings → **Português (Brasil)**. Refreshed 2026-11-05 for v1.36.0.0 (launcher-first short description; long description opens with the one-account story, three new bullets, Library in the tools-window bullet, account-facts privacy sentence; features merged/added to stay 20; v1.36 caption order with the two `-en` carry-over frames; what's-new; all matching the English sheet's ab48e6b edit; UI labels from this culture's catalog; translation verifier run pending). Refreshed 2026-10-26 for v1.35.0.0 (what's-new only; block drafted from whats-new-1.35.0.0.md, UI labels from this culture's catalog, translation verifier run pending). Refreshed 2026-10-23 for v1.34.0.0 (what's-new only; the Phase 2 listing audit found nothing else to change; block verified 6/6 by the translation verifier, UI labels from this culture's catalog). Refreshed 2026-10-07 for v1.33.0.0 (idle added to the alert-kind list in the long description, plus the repeat-interval and alert-sound controls — matching the English sheet's 1c84d28 edit; idle wording taken from this culture's shipped SettingsPage_AnAccountGoesIdle). Refreshed 2026-09-29 for v1.32.0.0 (phone-alert and alert fan-out entries merged into one; new Rejoin if it drops out entry; still 20; Rejoin bullet in the long description). Refreshed 2026-09-23 for v1.31.0.0 (feature entries for Known Roblox issues and per-account frame-rate caps, 18 → 20; privacy sentence; tools-window page list). Drafted
> 2026-09-05 from `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo,
> Squad Launch, Friend Follow, Pushover, ntfy); the app UI is now localized to six languages
> (v1.26.0.0). Register: você.

## Short description (≤200 chars)

> **Rewritten 2026-11-05 (v1.36.0.0): launcher first.** Translated from the English sheet's
> ab48e6b snippet: what one account gets leads, alts come second (v1.36 spec §7.7). "7" counts
> English; the old "6" counted only the translations. 187/200 chars.

```
Um só lugar para abrir o Roblox no Windows: seus jogos, limites de FPS, alertas no celular, temas, em 7 idiomas. Alts quando você quiser: várias contas salvas lado a lado, um clique cada.
```

## Long description

> **Rewritten 2026-11-05 (v1.36.0.0)**, same edits as the English sheet: the one-account story first,
> then the multi-account paragraph that used to open the listing; three bullets added (accounts at a
> glance, health badges, the Library); the tools-window bullet says Library; the privacy paragraph
> says where the account facts come from and where they are kept. 17 bullets, as English.

```
Um só lugar para abrir o Roblox no Windows.

Adicione sua conta do Roblox uma vez, pela própria página de login do Roblox, e o RoRoRo vira o seu launcher: todo jogo que você joga numa só Biblioteca, um clique para entrar num jogo ou num servidor privado salvo, um limite de FPS que não se perde, e um alerta no celular se você cair do jogo. Sua conta mostra Robux, Plus e amigos online à primeira vista, e avisa quando precisa de atenção.

Quando você quiser alts, o RoRoRo roda vários clientes Roblox ao mesmo tempo no mesmo PC, cada um conectado a uma conta salva diferente que pertence a você. Inicie qualquer uma com um clique — no jogo padrão, num servidor privado salvo ou em qualquer link de jogo colado.

O que você leva:
• Multi-instância com um clique. O RoRoRo segura o mutex singleton do Roblox, então clientes adicionais abrem em vez de trazer o primeiro para a frente.
• Cofre de contas criptografado (DPAPI). Os cookies salvos são criptografados com a API de Proteção de Dados do Windows, presos à sua conta do Windows — um arquivo copiado para outro PC não descriptografa. Mover contas entre os seus próprios PCs é um export deliberado, protegido por frase-senha.
• Status ao vivo de cada conta. Veja qual conta está em qual jogo, quem está parado e há quanto tempo, com limite de FPS por conta que não se perde.
• Todas as contas à primeira vista. Robux, Plus e amigos online aparecem embaixo de cada conta, com uma nota só sua. Abra uma conta para ver os jogos recentes, favoritos e servidores privados dela, e jogue qualquer um com um clique.
• Sabe quando uma conta precisa de você. Sessão expirada, Requer verificação, Limitado pelo Roblox: cada caso tem seu próprio selo, e um clique começa a correção.
• Uma Biblioteca para todos os jogos. Tudo o que você jogou, favoritou ou salvou como servidor privado, em grade ou lista. Pesquise, filtre, cole um link de jogo e escolha qual conta joga.
• Squad Launch + Friend Follow. Mande todas as contas selecionadas para o mesmo servidor privado, siga um amigo até o dele, ou junte suas contas num servidor público.
• Vigia de memória + Recycle. O RoRoRo aprende quanto um cliente Roblox realmente custa de RAM na sua máquina e avisa antes de acabar. Um clique fecha um cliente pesado e o devolve ao mesmo servidor em que estava.
• Entrar de novo se cair. Ative por conta, e um alt cuja janela do Roblox ainda está aberta, mas que caiu do jogo (uma expulsão por inatividade, uma entrada que falhou), é fechado e entra de novo sozinho. No máximo três vezes por hora, nunca a sua conta principal, e um "Parar" feito por você sempre tem prioridade.
• Uma só janela de ferramentas. Biblioteca, configurações, histórico, diagnóstico, plugins, Problemas conhecidos do Roblox e Sobre são páginas de uma única janela ao lado das contas — com atalhos de teclado em tudo; F1 mostra a lista.
• Temas. Quatro embutidos, incluindo um que nunca depende só de cor, mais um editor para criar o seu a partir de dez cores e compartilhar como arquivo.
• Alertas opcionais — desktop, Discord ou celular. Direcione cada alerta para qualquer combinação: notificações no desktop, um webhook do Discord que você cria, ou seu celular via Pushover ou ntfy. Quedas de conta, avisos de memória, contas inativas, conclusões de Recycle e um sinal de "tudo certo" a cada duas horas enquanto suas contas rodam. Escolha com que frequência no máximo cada alerta pode repetir e como ele soa. Uma instalação nova não faz nenhuma chamada de alerta — nada sai antes de você configurar.
• Bandeja do sistema com ícone colorido por estado — o status de multi-instância à primeira vista; clique duplo inicia sua conta principal.
• Sistema de plugins. Plugins opcionais rodam como processos separados e não têm permissão nenhuma até você conceder uma a uma.
• Atualização automática via Velopack. Uma configuração remota acompanha a versão do Roblox e o nome do mutex conhecidos, para uma mudança do lado do Roblox não te travar por muito tempo.
• Acessibilidade medida. Cada controle anuncia seu nome para tecnologias assistivas, e o contraste é verificado nos pixels renderizados em todos os temas.
• Fala o seu idioma. O app inteiro — cada menu, configuração, dica e janela, e as mensagens que o RoRoRo escreve enquanto você usa — é traduzido para francês, alemão, russo, português (Brasil), polonês e espanhol, seguindo o idioma do seu Windows ou sua escolha nas Configurações. A troca é imediata, e a lista só oferece idiomas totalmente traduzidos, para você nunca cair numa tela pela metade em inglês.

Privacidade e segurança:
Sua senha do Roblox nunca é vista pelo RoRoRo. O login acontece inteiramente na página do próprio Roblox, embutida num quadro Microsoft Edge WebView2 — mesmo HTML, mesma conexão HTTPS do seu navegador. O RoRoRo captura apenas o cookie de sessão que o Roblox define após o login, e o criptografa antes de gravar em disco. Os dados da conta (Robux, Plus, amigos online) são lidos do Roblox com a própria sessão daquela conta e guardados só no mesmo cofre criptografado. Sem telemetria. Sem analytics. Nada sobre você sai da sua máquina além das chamadas ao Roblox durante o lançamento — as mesmas que o Roblox.com faz do seu navegador — e, somente se você mesmo configurar, alertas para o seu webhook do Discord ou o serviço de push escolhido (Pushover ou ntfy). O RoRoRo também baixa os próprios arquivos assinados das suas versões publicadas no GitHub — atualizações, as configurações de compatibilidade com o Roblox e a lista de problemas conhecidos do Roblox — e não envia nada sobre você para obtê-los.

Importante: aviso de marcas e afiliação.
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RoRoRo é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo registrado é usado apenas para descrever compatibilidade com a plataforma Roblox. O RoRoRo inicia o cliente oficial do Roblox sem modificá-lo — sem injeção, sem hooks, sem alterar o processo do Roblox; ele apenas segura um mutex nomeado do Windows antes do lançamento, para que as instâncias seguintes do cliente vejam a checagem de singleton como já ocupada.

Um produto 626 Labs.
```

## Product features (20 entries, ≤200 chars each)

> **2026-11-05 (v1.36.0.0):** still 20/20, as English. The two frame-rate entries merge into Live
> status, freeing a slot for accounts at a glance; join-by-link folds into the new Library entry; the
> tools-window entry says Library.

```
Launcher multi-instância para Roblox no Windows, com um clique
Cofre de contas criptografado com DPAPI e export protegido por frase-senha
Status ao vivo por conta — em qual jogo está, tempo parado e limites de FPS que não se perdem, aumentados, reduzidos ou removidos cliente por cliente
Todas as contas à primeira vista — Robux, Plus, amigos online e sua nota, com um selo de correção em um clique quando a sessão expira ou requer verificação
Vigia de memória que aprende o custo real de RAM de cada cliente, mais Recycle de um clique ao mesmo servidor
Squad Launch e Friend Follow — mesmo servidor privado, ou um servidor público juntos
Uma Biblioteca para todo jogo que você joga — grade ou lista, pesquisa e filtros, cole qualquer link roblox.com e escolha qual conta joga
Uma janela de ferramentas para Biblioteca, Configurações, Histórico e mais, com atalhos de teclado
Quatro temas embutidos mais um editor para criar o seu
Alertas opcionais do Discord para um webhook que você cria
Bandeja do sistema com ícone colorido por estado e clique duplo para a conta principal
Sistema de plugins com consentimento por capacidade e isolamento fora do processo
Atualização automática que continua funcionando quando o Roblox muda por baixo
Iniciar com o Windows se quiser — um botão só, e a lista de Inicialização do Windows continua no comando
Discord Join abre o RoRoRo mesmo fechado, e sempre pergunta antes de iniciar qualquer coisa
Alertas no celular via Pushover ou ntfy, e no desktop e em canais do Discord em qualquer combinação, por alerta — um alt cai e seu celular vibra
Sinais de "tudo certo" a cada duas horas enquanto as contas rodam — silêncio significa problema
Disponível em seis idiomas — o app inteiro, telas e mensagens, em francês, alemão, russo, português (Brasil), polonês ou espanhol, troca imediata, seguindo o Windows ou sua escolha
Problemas conhecidos do Roblox — problemas do lado do Roblox, o que fazer e o recurso do RoRoRo que ajuda, sempre atualizados sem atualizar o app
Entrar de novo se cair — um alt expulso ou preso fora do jogo é fechado e entra de novo sozinho, nunca a sua conta principal
```

## What's new in this version (v1.37.0.0, ≤1500 chars)

```
v1.37.0.0

Menos becos sem saída na entrada

• Se este PC não tem um Roblox que o RoRoRo consiga abrir, o
  RoRoRo agora avisa e mostra o download, em vez de uma mensagem
  do Windows. Isso acontece quando o Roblox veio da Microsoft
  Store: baixe em roblox.com; os dois podem continuar
  instalados.
• Adicionar uma conta que você já salvou não cria mais uma
  segunda cópia. O RoRoRo renova o login dela e avisa para
  adicionar uma conta do Roblox diferente se quiser rodar duas
  ao mesmo tempo.
• Novos limites de FPS: 75, 100 e 180, para monitores nessas
  taxas.
• Depois de alguns dias bons, o RoRoRo pode pedir uma vez uma
  avaliação na Microsoft Store. Qualquer resposta encerra o
  pedido de vez.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Todos os direitos reservados. "Roblox" é uma marca da Roblox Corporation. O RoRoRo não é afiliado, endossado ou patrocinado pela Roblox Corporation.
```

## Trademark info

```
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RORORO é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo registrado é usado apenas para descrever compatibilidade com a plataforma Roblox. O RORORO inicia o cliente oficial do Roblox sem modificações.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts.png`, `02-account-panel.png`, `03-account-health.png`, `04-library.png`, `05-multi-instance-en.png`, `06-history.png`, `07-themes.png`, `08-theme-builder.png`, `09-plugins.png`, `10-compact-en.png` (v1.36 order, 2026-11-05; `05` and `10` carry over from English, hence `-en`; see `screenshots/README.md`).

```
Todas as contas à primeira vista: Robux, Plus, amigos online, sua nota e o jogo em que cada uma está agora.
Abra uma conta e os jogos dela estão ali: recentes, favoritos e servidores privados, um clique para jogar.
Quando uma conta precisa de você, ela avisa. Sessão expirada, requer verificação, limitado pelo Roblox, cada um com correção em um clique.
Todo jogo que você jogou, favoritou ou salvou, numa só Biblioteca. Pesquise, filtre e escolha qual conta joga.
Oito clientes Roblox, oito contas, um PC. O título de cada janela carrega a conta conectada nela, então você sempre sabe qual é qual.
Cada inicialização fica registrada, então você vê qual conta jogou o quê e por quanto tempo.
Quatro temas nativos. O Flatline não transmite nenhum significado pela cor, então nada se perde com daltonismo, um monitor ruim ou sol direto.
Monte um tema com dez cores e ele aparece no seletor. É um arquivo JSON, então dá para passar para outra pessoa.
Os plugins rodam como processos separados e perguntam antes. Você concede cada permissão pelo nome e pode revogá-la depois.
O modo compacto mostra só o que está rodando. Fixe num canto da tela e volte para o jogo.
```

## Keywords (max 7, 40 chars each, 21 words total — one per box)

```
roblox
multi instância
multi-conta
launcher
gerenciador de contas
contas alt
multibox
```
