# Store listing — Español (es)

> Paste-ready para Partner Center → Store listings → **Español** (neutral — un solo texto para
> España y Latinoamérica). Refreshed 2026-11-05 for v1.36.0.0 (launcher-first short description; long description opens with the one-account story, three new bullets, Library in the tools-window bullet, account-facts privacy sentence; features merged/added to stay 20; v1.36 caption order with the two `-en` carry-over frames; what's-new; all matching the English sheet's ab48e6b edit; UI labels from this culture's catalog; translation verifier run pending). Refreshed 2026-10-26 for v1.35.0.0 (what's-new only; block drafted from whats-new-1.35.0.0.md, UI labels from this culture's catalog, translation verifier run pending). Refreshed 2026-10-23 for v1.34.0.0 (what's-new only; the Phase 2 listing audit found nothing else to change; block verified 6/6 by the translation verifier, UI labels from this culture's catalog). Refreshed 2026-10-07 for v1.33.0.0 (idle added to the alert-kind list in the long description, plus the repeat-interval and alert-sound controls — matching the English sheet's 1c84d28 edit; idle wording taken from this culture's shipped SettingsPage_AnAccountGoesIdle). Refreshed 2026-09-29 for v1.32.0.0 (phone-alert and alert fan-out entries merged into one; new Rejoin if it drops out entry; still 20; Rejoin bullet in the long description). Refreshed 2026-09-23 for v1.31.0.0 (feature entries for Known Roblox issues and per-account frame-rate caps, 18 → 20; privacy sentence; tools-window page list). Refreshed 2026-09-08 for v1.27.0.0 (what's-new + language claims). Drafted 2026-09-05 from `listing-copy.md` (v1.25.0.0 state).
> Product nouns stay in English (RoRoRo, Squad Launch, Friend Follow, Pushover, ntfy); the
> app UI is now localized to Spanish (v1.26.0.0). Register: tú.

## Short description (≤200 chars)

> **Rewritten 2026-11-05 (v1.36.0.0): launcher first.** Translated from the English sheet's
> ab48e6b snippet: what one account gets leads, alts come second (v1.36 spec §7.7). "7" counts
> English; the old "6" counted only the translations. 188/200 chars.

```
Un solo lugar para lanzar Roblox en Windows: tus juegos, límites de FPS, alertas al móvil, temas, en 7 idiomas. Alts cuando quieras: varias cuentas guardadas lado a lado, un clic cada una.
```

## Long description

> **Rewritten 2026-11-05 (v1.36.0.0)**, same edits as the English sheet: the one-account story first,
> then the multi-account paragraph that used to open the listing; three bullets added (accounts at a
> glance, health badges, the Library); the tools-window bullet says Library; the privacy paragraph
> says where the account facts come from and where they are kept. 17 bullets, as English.

```
Un solo lugar para lanzar Roblox en Windows.

Añade tu cuenta de Roblox una vez, desde la propia página de inicio de sesión de Roblox, y RoRoRo se convierte en tu launcher: cada juego al que juegas en una sola Biblioteca, un clic para entrar en un juego o en un servidor privado guardado, un límite de FPS que se mantiene, y una alerta en tu móvil si te caes del juego. Tu cuenta muestra de un vistazo sus Robux, Plus y amigos en línea, y te avisa cuando necesita atención.

Cuando quieras alts, RoRoRo ejecuta varios clientes de Roblox a la vez en un mismo PC, cada uno con una cuenta guardada distinta que te pertenece. Lanza cualquiera con un clic: a su juego predeterminado, a un servidor privado guardado o a cualquier enlace de juego que pegues.

Lo que obtienes:
• Multi-instancia con un clic. RoRoRo retiene el mutex singleton de Roblox, así que los clientes adicionales se abren en lugar de traer el primero al frente.
• Baúl de cuentas cifrado (DPAPI). Las cookies guardadas se cifran con la API de Protección de Datos de Windows, ligadas a tu cuenta de Windows: un archivo copiado a otro PC no se descifra. Mover cuentas entre tus propios equipos es una exportación deliberada, protegida con frase de contraseña.
• Estado en vivo de cada cuenta. Ve qué cuenta está en qué juego, quién lleva cuánto tiempo inactivo, y fija límites de FPS por cuenta que se mantienen.
• Todas tus cuentas de un vistazo. Robux, Plus y amigos en línea aparecen bajo cada cuenta, con una nota tuya. Abre una cuenta para ver sus juegos recientes, favoritos y servidores privados, y juega a cualquiera con un clic.
• Sabe cuándo una cuenta te necesita. Sesión caducada, Requiere verificación, Limitado por Roblox: cada caso tiene su propia insignia, y un clic inicia el arreglo.
• Una sola Biblioteca para todos tus juegos. Todo lo que has jugado, marcado como favorito o guardado como servidor privado, en cuadrícula o en lista. Búscalo, fíltralo, pega un enlace de juego y elige qué cuenta juega.
• Squad Launch + Friend Follow. Manda todas las cuentas seleccionadas al mismo servidor privado, sigue a un amigo hasta el suyo o junta tus cuentas en un servidor público.
• Vigilante de memoria + Recycle. RoRoRo aprende cuánta RAM cuesta de verdad un cliente de Roblox en tu máquina y avisa antes de que se agote. Un clic cierra un cliente pesado y lo devuelve al mismo servidor donde estaba.
• Volver a unirse si se desconecta. Actívalo por cuenta, y un alt cuya ventana de Roblox sigue abierta pero que ha quedado fuera del juego (una expulsión por inactividad, un intento fallido de unirse) se cierra y vuelve a unirse solo. Como máximo tres veces por hora, nunca tu cuenta principal, y tu propio "Detener" siempre tiene prioridad.
• Una sola ventana de herramientas. Biblioteca, ajustes, historial, diagnóstico, plugins, Problemas conocidos de Roblox y Acerca de son páginas de una misma ventana junto a tus cuentas, con atajos de teclado en todas partes — F1 muestra la lista.
• Temas. Cuatro integrados, incluido uno que nunca transmite significado solo con color, más un editor para crear el tuyo a partir de diez colores y compartirlo como archivo.
• Alertas opcionales: escritorio, Discord o móvil. Dirige cada alerta a cualquier combinación: notificaciones de escritorio, un webhook de Discord que tú creas, o tu móvil mediante Pushover o ntfy. Desconexiones, avisos de memoria, cuentas inactivas, finales de Recycle y una señal de "todo bien" cada dos horas mientras tus cuentas corren. Elige con qué frecuencia como máximo puede repetirse cada alerta y cómo suena. Una instalación nueva no hace ninguna llamada de alertas: nada sale hasta que tú lo configuras.
• Bandeja del sistema con icono coloreado por estado — el estado de multi-instancia de un vistazo; doble clic lanza tu cuenta principal.
• Sistema de plugins. Los plugins opcionales corren como procesos separados y no tienen permisos hasta que se los concedes uno a uno.
• Actualización automática vía Velopack. Una configuración remota sigue la versión de Roblox y el nombre del mutex conocidos, para que un cambio del lado de Roblox no te deje fuera mucho tiempo.
• Accesibilidad medida. Cada control anuncia su nombre a las tecnologías de asistencia, y el contraste se verifica sobre píxeles renderizados en todos los temas.
• Habla tu idioma. Toda la app —cada menú, ajuste, información sobre herramientas y ventana, y los mensajes que RoRoRo escribe mientras la usas— está traducida al francés, alemán, ruso, portugués (Brasil), polaco y español, según el idioma de tu Windows o tu elección en Configuración. El cambio es inmediato, y la lista solo ofrece idiomas totalmente traducidos, así que nunca acabas en una pantalla a medias en inglés.

Privacidad y seguridad:
RoRoRo nunca ve tu contraseña de Roblox. El inicio de sesión ocurre por completo en la página del propio Roblox, incrustada en un marco Microsoft Edge WebView2: el mismo HTML, la misma conexión HTTPS que haría tu navegador. RoRoRo captura solo la cookie de sesión que Roblox establece tras iniciar sesión, y la cifra antes de escribirla en disco. Los datos de la cuenta (Robux, Plus, amigos en línea) se leen de Roblox con la propia sesión de esa cuenta y se guardan solo en el mismo baúl cifrado. Sin telemetría. Sin analítica. Nada sobre ti sale de tu máquina salvo las llamadas a Roblox durante el lanzamiento —las mismas que hace Roblox.com desde tu navegador— y, solo si tú los configuras, las alertas a tu webhook de Discord o al servicio de push que elegiste (Pushover o ntfy). RoRoRo también descarga sus propios archivos firmados desde sus versiones publicadas en GitHub —las actualizaciones, los ajustes de compatibilidad con Roblox y la lista de problemas conocidos de Roblox— y no envía nada sobre ti para obtenerlos.

Importante: aviso de marcas y afiliación.
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RoRoRo es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término de marca registrada se usa únicamente para describir compatibilidad con la plataforma Roblox. RoRoRo lanza el cliente oficial de Roblox sin modificarlo: sin inyección, sin hooks, sin alterar el proceso de Roblox; solo retiene un mutex con nombre de Windows antes del lanzamiento, para que las siguientes instancias del cliente vean la comprobación de singleton como ya ocupada.

Un producto de 626 Labs.
```

## Product features (20 entries, ≤200 chars each)

> **2026-11-05 (v1.36.0.0):** still 20/20, as English. The two frame-rate entries merge into Live
> status, freeing a slot for accounts at a glance; join-by-link folds into the new Library entry; the
> tools-window entry says Library.

```
Launcher multi-instancia para Roblox en Windows, con un clic
Baúl de cuentas cifrado con DPAPI y exportación protegida con frase de contraseña
Estado en vivo por cuenta: en qué juego está, tiempo inactivo y límites de FPS que se mantienen, que puedes subir, bajar o quitar cliente por cliente
Todas tus cuentas de un vistazo: Robux, Plus, amigos en línea y tu nota, con una insignia para arreglarlo en un clic cuando una sesión caduca o requiere verificación
Vigilante de memoria que aprende el costo real de RAM de cada cliente, más Recycle de un clic al mismo servidor
Squad Launch y Friend Follow: el mismo servidor privado, o un servidor público juntos
Una sola Biblioteca para cada juego al que juegas: cuadrícula o lista, búsqueda y filtros, pega cualquier enlace de roblox.com y elige qué cuenta juega
Una ventana de herramientas para Biblioteca, Ajustes, Historial y más, con atajos de teclado
Cuatro temas integrados más un editor para crear el tuyo
Alertas opcionales de Discord a un webhook que tú creas
Bandeja del sistema con icono de estado y doble clic para la cuenta principal
Sistema de plugins con consentimiento por capacidad y aislamiento fuera de proceso
Actualización automática que sigue funcionando cuando Roblox cambia por debajo
Iniciar con Windows si quieres: un solo interruptor, y la lista de Inicio de Windows manda
Discord Join arranca RoRoRo aunque esté cerrado, y siempre pregunta antes de lanzar nada
Alertas al móvil vía Pushover o ntfy, y al escritorio y a canales de Discord en cualquier combinación, por alerta: un alt se cae y tu móvil vibra
Señales de "todo bien" cada dos horas mientras tus cuentas corren: el silencio significa problema
Disponible en seis idiomas — toda la app, pantallas y mensajes, en francés, alemán, ruso, portugués (Brasil), polaco o español, cambio inmediato, según Windows o tu elección
Problemas conocidos de Roblox — problemas del lado de Roblox, qué hacer y la función de RoRoRo que ayuda, al día sin actualizar la app
Volver a unirse si se desconecta: un alt expulsado o atascado fuera del juego se cierra y vuelve a unirse solo, nunca tu cuenta principal
```

## What's new in this version (v1.36.0.0, ≤1500 chars)

```
v1.36.0.0

Tus cuentas, de un vistazo

• Cada cuenta muestra sus Robux, Plus y amigos en línea, con
  una nota que escribes tú.
• Abre una cuenta para ver sus juegos recientes, favoritos y
  servidores privados, y juega a cualquiera con un clic.
• Cuando una cuenta te necesita, una insignia dice por qué:
  «Sesión caducada», «Requiere verificación» o «Limitado por
  Roblox». Un clic inicia el arreglo.
• «Juegos» ahora es la «Biblioteca»: todos los juegos que has
  jugado, marcado como favoritos o guardado, en cuadrícula o en
  lista. Búscalos, fíltralos, pega un enlace de juego y elige
  quién juega con «Jugar como».
• El recorrido de bienvenida empieza con una cuenta; los
  consejos para varias cuentas vienen después.
• El tiempo inactivo ahora cuenta para la cuenta con la que
  jugaste de verdad, aunque cambies de ventana rápido.
• Los procesos de Roblox en segundo plano que nunca abrieron
  una ventana se cierran solos a los dos minutos.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir».

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Todos los derechos reservados. "Roblox" es una marca de Roblox Corporation. RoRoRo no está afiliada, avalada ni patrocinada por Roblox Corporation.
```

## Trademark info

```
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RORORO es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término de marca registrada se usa únicamente para describir compatibilidad con la plataforma Roblox. RORORO lanza el cliente oficial de Roblox sin modificaciones.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts.png`, `02-account-panel.png`, `03-account-health.png`, `04-library.png`, `05-multi-instance-en.png`, `06-history.png`, `07-themes.png`, `08-theme-builder.png`, `09-plugins.png`, `10-compact-en.png` (v1.36 order, 2026-11-05; `05` and `10` carry over from English, hence `-en`; see `screenshots/README.md`).

```
Todas tus cuentas de un vistazo: Robux, Plus, amigos en línea, tu nota y el juego en el que está cada una ahora mismo.
Abre una cuenta y sus juegos están ahí: recientes, favoritos y servidores privados, un clic para jugar.
Cuando una cuenta te necesita, te lo dice. Sesión caducada, requiere verificación, limitado por Roblox, cada uno con su arreglo en un clic.
Todos los juegos que has jugado, marcado como favoritos o guardado, en una sola Biblioteca. Busca, filtra y elige qué cuenta juega.
Ocho clientes de Roblox, ocho cuentas, un PC. El título de cada ventana lleva la cuenta que ha iniciado sesión en ella, así siempre sabes cuál es cuál.
Cada lanzamiento queda registrado, así ves qué cuenta jugó a qué y durante cuánto tiempo.
Cuatro temas integrados. Flatline no transmite ningún significado mediante el color, así que no se pierde nada con daltonismo, un panel malo o sol directo.
Crea un tema con diez colores y aparece en el selector. Es un archivo JSON, así que puedes pasárselo a otra persona.
Los plugins se ejecutan como procesos separados y preguntan primero. Concedes cada permiso por su nombre y puedes revocarlo después.
El modo compacto muestra solo lo que está en marcha. Fíjalo en una esquina de la pantalla y vuelve al juego.
```

## Keywords (max 7, 40 chars each, 21 words total — one per box)

```
roblox
multi instancia
multicuenta
launcher
gestor de cuentas
cuentas alt
multibox
```
