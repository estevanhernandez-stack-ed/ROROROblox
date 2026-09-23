# Store listing — Português (Brasil) (pt-BR)

> Paste-ready para Partner Center → Store listings → **Português (Brasil)**. Refreshed 2026-09-23 for v1.31.0.0 (feature entries for Known Roblox issues and per-account frame-rate caps, 18 → 20; privacy sentence; tools-window page list). Drafted
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
• Uma só janela de ferramentas. Jogos, configurações, histórico, diagnóstico, plugins, Problemas conhecidos do Roblox e Sobre são páginas de uma única janela ao lado das contas — com atalhos de teclado em tudo; F1 mostra a lista.
• Temas. Quatro embutidos, incluindo um que nunca depende só de cor, mais um editor para criar o seu a partir de dez cores e compartilhar como arquivo.
• Alertas opcionais — desktop, Discord ou celular. Direcione cada alerta para qualquer combinação: notificações no desktop, um webhook do Discord que você cria, ou seu celular via Pushover ou ntfy. Quedas de conta, avisos de memória, conclusões de Recycle e um sinal de "tudo certo" a cada duas horas. Uma instalação nova não faz nenhuma chamada de alerta — nada sai antes de você configurar.
• Bandeja do sistema com ícone colorido por estado — o status de multi-instância à primeira vista; clique duplo inicia sua conta principal.
• Sistema de plugins. Plugins opcionais rodam como processos separados e não têm permissão nenhuma até você conceder uma a uma.
• Atualização automática via Velopack. Uma configuração remota acompanha a versão do Roblox e o nome do mutex conhecidos, para uma mudança do lado do Roblox não te travar por muito tempo.
• Acessibilidade medida. Cada controle anuncia seu nome para tecnologias assistivas, e o contraste é verificado nos pixels renderizados em todos os temas.
• Fala o seu idioma. O app inteiro — cada menu, configuração, dica e janela, e as mensagens que o RoRoRo escreve enquanto você usa — é traduzido para francês, alemão, russo, português (Brasil), polonês e espanhol, seguindo o idioma do seu Windows ou sua escolha nas Configurações. A troca é imediata, e a lista só oferece idiomas totalmente traduzidos, para você nunca cair numa tela pela metade em inglês.

Privacidade e segurança:
Sua senha do Roblox nunca é vista pelo RoRoRo. O login acontece inteiramente na página do próprio Roblox, embutida num quadro Microsoft Edge WebView2 — mesmo HTML, mesma conexão HTTPS do seu navegador. O RoRoRo captura apenas o cookie de sessão que o Roblox define após o login, e o criptografa antes de gravar em disco. Sem telemetria. Sem analytics. Nada sobre você sai da sua máquina além das chamadas ao Roblox durante o lançamento — as mesmas que o Roblox.com faz do seu navegador — e, somente se você mesmo configurar, alertas para o seu webhook do Discord ou o serviço de push escolhido (Pushover ou ntfy). O RoRoRo também baixa os próprios arquivos assinados das suas versões publicadas no GitHub — atualizações, as configurações de compatibilidade com o Roblox e a lista de problemas conhecidos do Roblox — e não envia nada sobre você para obtê-los.

Importante: aviso de marcas e afiliação.
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RoRoRo é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo é usado apenas para descrever compatibilidade com a plataforma Roblox. O RoRoRo inicia o cliente oficial do Roblox sem modificá-lo — sem injeção, sem hooks, sem alterar o processo do Roblox; ele apenas segura um mutex nomeado do Windows antes do lançamento, para que as instâncias seguintes do cliente vejam a checagem de singleton como já ocupada.

Um produto 626 Labs.
```

## Product features (20 entries, ≤200 chars each)

```
Launcher multi-instância para Roblox no Windows, com um clique
Cofre de contas criptografado com DPAPI e export protegido por frase-senha
Status ao vivo por conta — em qual jogo está, tempo parado e limite de FPS por conta
Limites de FPS por conta — aumentados, reduzidos ou removidos de vez, cliente por cliente
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
Problemas conhecidos do Roblox — problemas do lado do Roblox, o que fazer e o recurso do RoRoRo que ajuda, sempre atualizados sem atualizar o app
```

## What's new in this version (v1.30.0.0, ≤1500 chars)

```
v1.30.0.0

Os alertas disparam uma vez, quando algo muda
• Uma regra que continuava verdadeira alertava de novo a cada
  poucos minutos, enquanto continuasse verdadeira. Agora ela
  alerta uma vez, quando o número cruza o limite, e fica
  quieta até cruzar de volta.
• Se alguma regra sua falava muito, ela vai ficar quieta. É a
  correção, não um defeito.
• Uma regra precisa de duas leituras antes de poder dizer
  qualquer coisa, e reiniciar não anuncia de novo um problema
  que já estava acontecendo.

Peça para avisarem quando voltar ao normal
• Marque "Also tell me when it comes right again" numa regra e
  um segundo alerta chega quando o número volta a cruzar o
  limite.
• Desligado até você marcar, e vai para os mesmos lugares que
  os alertas dessa regra já vão.
• A frase do alerta ainda sai em inglês em todos os idiomas.

Plugin instala em conexão lenta
• A instalação de um plugin desistia depois de 100 segundos:
  quem não conseguia puxar o arquivo inteiro nesse tempo nunca
  instalava. Agora são dez minutos, e ele diz o que tentar se
  ainda assim estourar.
• Um alerta sobre um número que não pertence a nenhuma conta
  agora começa com o nome da regra, em vez de um espaço vazio.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Todos os direitos reservados. "Roblox" é uma marca da Roblox Corporation. O RoRoRo não é afiliado, endossado ou patrocinado pela Roblox Corporation.
```

## Trademark info

```
"Roblox" e o logotipo Roblox são marcas da Roblox Corporation. O RORORO é uma ferramenta independente de terceiros, não afiliada, endossada ou patrocinada pela Roblox Corporation. O termo é usado apenas para descrever compatibilidade com a plataforma Roblox. O RORORO inicia o cliente oficial do Roblox sem modificações.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts-running.png`, `02-themes.png`, `03-about.png`, `04-games.png`, `05-diagnostics.png`, `06-history.png`, `07-plugins.png`, `08-theme-builder.png`, `09-compact.png`, `10-multi-instance.png`.

```
Três contas rodando ao mesmo tempo, cada uma com seu próprio uso de memória e seu próprio botão de parada. Os cookies são criptografados por usuário com o Windows DPAPI e nunca saem da máquina.
Quatro temas nativos. O Flatline não transmite nenhum significado pela cor, então nada se perde com daltonismo, um monitor ruim ou sol direto.
Multi-launcher para Windows. Segura o mutex singleton do Roblox para que o próximo cliente abra em vez de brigar com o primeiro. Uma reimplementação limpa, não um fork.
Salve os jogos e servidores privados que você realmente joga e escolha um diferente para cada conta antes de iniciar.
O Diagnóstico mostra o que o RoRoRo enxerga agora: versões, saúde e onde ficam os logs, para quando você precisar relatar algo.
Cada inicialização fica registrada, então você vê qual conta jogou o quê e por quanto tempo.
Os plugins rodam como processos separados e perguntam antes. Você concede cada permissão pelo nome e pode revogá-la depois.
Monte um tema com dez cores e ele aparece no seletor. É um arquivo JSON, então dá para passar para outra pessoa.
O modo compacto mostra só o que está rodando. Fixe num canto da tela e volte para o jogo.
Oito clientes Roblox, oito contas, um PC. O título de cada janela carrega a conta conectada nela, então você sempre sabe qual é qual.
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
