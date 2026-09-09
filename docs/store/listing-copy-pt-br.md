# Store listing — Português (Brasil) (pt-BR)

> Paste-ready para Partner Center → Store listings → **Português (Brasil)**. Drafted
> 2026-09-05 from `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo,
> Squad Launch, Friend Follow, Pushover, ntfy); the app UI is now localized to six languages
> (v1.26.0.0). Register: você.

## Short description (≤200 chars)

```
Multi-launcher para Windows em 6 idiomas: vários clientes Roblox lado a lado, cada um na sua conta. Cofre criptografado, Squad Launch, vigia de memória, alertas no celular, status ao vivo, temas.
```

## Long description

```
Multi-launcher para Windows.

O RoRoRo é um launcher para Windows que roda vários clientes Roblox ao mesmo tempo no mesmo PC, cada um conectado a uma conta salva diferente que pertence a você. Adicione suas contas uma vez pela própria página de login do Roblox e depois inicie qualquer uma com um clique — no jogo padrão, num servidor privado salvo ou em qualquer link de jogo colado.

O que você leva:
• Multi-instância com um clique. O RoRoRo segura o mutex singleton do Roblox, então clientes adicionais abrem em vez de trazer o primeiro para a frente.
• Cofre de contas criptografado (DPAPI). Os cookies salvos são criptografados com a API de Proteção de Dados do Windows, presos à sua conta do Windows — um arquivo copiado para outro PC não descriptografa. Mover contas entre os seus próprios PCs é um export deliberado, protegido por frase-senha.
• Status ao vivo de cada conta. Veja qual conta está em qual jogo, quem está parado e há quanto tempo, com limite de FPS por conta que não se perde.
• Squad Launch + Friend Follow. Mande todas as contas selecionadas para o mesmo servidor privado, siga um amigo até o dele, ou junte suas contas num servidor público.
• Vigia de memória + Recycle. O RoRoRo aprende quanto um cliente Roblox realmente custa de RAM na sua máquina e avisa antes de acabar. Um clique fecha um cliente pesado e o devolve ao mesmo servidor em que estava.
• Uma só janela de ferramentas. Jogos, configurações, histórico, diagnóstico, plugins e Sobre são páginas de uma única janela ao lado das contas — com atalhos de teclado em tudo; F1 mostra a lista.
• Temas. Quatro embutidos, incluindo um que nunca depende só de cor, mais um editor para criar o seu a partir de dez cores e compartilhar como arquivo.
• Alertas opcionais — desktop, Discord ou celular. Direcione cada alerta para qualquer combinação: notificações no desktop, um webhook do Discord que você cria, ou seu celular via Pushover ou ntfy. Quedas de conta, avisos de memória, conclusões de Recycle e um sinal de "tudo certo" a cada duas horas. Uma instalação nova não faz nenhuma chamada de alerta — nada sai antes de você configurar.
• Bandeja do sistema com ícone colorido por estado — o status de multi-instância à primeira vista; clique duplo inicia sua conta principal.
• Sistema de plugins. Plugins opcionais rodam como processos separados e não têm permissão nenhuma até você conceder uma a uma.
• Atualização automática via Velopack. Uma configuração remota acompanha a versão do Roblox e o nome do mutex conhecidos, para uma mudança do lado do Roblox não te travar por muito tempo.
• Acessibilidade medida. Cada controle anuncia seu nome para tecnologias assistivas, e o contraste é verificado nos pixels renderizados em todos os temas.
• Fala o seu idioma. O app inteiro — cada menu, configuração, dica e janela, e as mensagens que o RoRoRo escreve enquanto você usa — é traduzido para francês, alemão, russo, português (Brasil), polonês e espanhol, seguindo o idioma do seu Windows ou sua escolha nas Configurações. A troca é imediata, e a lista só oferece idiomas totalmente traduzidos, para você nunca cair numa tela pela metade em inglês.

Privacidade e segurança:
Sua senha do Roblox nunca é vista pelo RoRoRo. O login acontece inteiramente na página do próprio Roblox, embutida num quadro Microsoft Edge WebView2 — mesmo HTML, mesma conexão HTTPS do seu navegador. O RoRoRo captura apenas o cookie de sessão que o Roblox define após o login, e o criptografa antes de gravar em disco. Sem telemetria. Sem analytics. Nada sai da sua máquina além das chamadas ao Roblox durante o lançamento — as mesmas que o Roblox.com faz do seu navegador — e, somente se você mesmo configurar, alertas para o seu webhook do Discord ou o serviço de push escolhido (Pushover ou ntfy).

Importante: aviso de marcas e afiliação.
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RoRoRo é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo é usado apenas para descrever compatibilidade com a plataforma Roblox. O RoRoRo inicia o cliente oficial do Roblox sem modificá-lo — sem injeção, sem hooks, sem alterar o processo do Roblox; ele apenas segura um mutex nomeado do Windows antes do lançamento, para que as instâncias seguintes do cliente vejam a checagem de singleton como já ocupada.

Um produto 626 Labs.
```

## Product features (18 entries, ≤200 chars each)

```
Launcher multi-instância para Roblox no Windows, com um clique
Cofre de contas criptografado com DPAPI e export protegido por frase-senha
Status ao vivo por conta — em qual jogo está, tempo parado e limite de FPS por conta
Vigia de memória que aprende o custo real de RAM de cada cliente, mais Recycle de um clique ao mesmo servidor
Squad Launch e Friend Follow — mesmo servidor privado, ou um servidor público juntos
Entrar por link de qualquer URL roblox.com, com servidores privados salvos por conta
Uma janela de ferramentas para Jogos, Configurações, Histórico e mais, com atalhos de teclado
Quatro temas embutidos mais um editor para criar o seu
Alertas opcionais do Discord para um webhook que você cria
Bandeja do sistema com ícone colorido por estado e clique duplo para a conta principal
Sistema de plugins com consentimento por capacidade e isolamento fora do processo
Atualização automática que continua funcionando quando o Roblox muda por baixo
Iniciar com o Windows se quiser — um botão só, e a lista de Inicialização do Windows continua no comando
Discord Join abre o RoRoRo mesmo fechado, e sempre pergunta antes de iniciar qualquer coisa
Alertas no celular via Pushover ou ntfy — um alt cai e seu celular vibra, mesmo com o Discord fechado
Sinais de "tudo certo" a cada duas horas enquanto as contas rodam — silêncio significa problema
Alertas em leque — desktop, canais do Discord e celular em qualquer combinação, por alerta
Disponível em seis idiomas — o app inteiro, telas e mensagens, em francês, alemão, russo, português (Brasil), polonês ou espanhol, troca imediata, seguindo o Windows ou sua escolha
```

## What's new in this version (v1.27.0.0, ≤1500 chars)

```
v1.27.0.0

O RoRoRo fala o seu idioma
• O app inteiro — menus, configurações, cada janela e as
  mensagens que o RoRoRo escreve enquanto você usa — está
  traduzido para francês, alemão, russo, português (Brasil),
  polonês e espanhol. Se o Windows estiver em um desses, o
  RoRoRo adota sozinho.

Troque quando quiser, na hora
• Configurações > Aparência lista só os idiomas para os quais
  o RoRoRo está totalmente traduzido — você nunca cai numa
  tela pela metade. Escolha um e o app muda na hora, sem
  reiniciar.

Ainda inglês no fundo
• O inglês continua sendo o padrão, e os nomes de recursos —
  Squad Launch, Recycle, RoRoRo — se leem igual em qualquer
  idioma. Nada muda nas suas contas, temas ou configurações.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Todos os direitos reservados. "Roblox" é uma marca da Roblox Corporation. O RoRoRo não é afiliado, endossado ou patrocinado pela Roblox Corporation.
```

## Trademark info

```
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RORORO é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo é usado apenas para descrever compatibilidade com a plataforma Roblox. O RORORO inicia o cliente oficial do Roblox sem modificações.
```
