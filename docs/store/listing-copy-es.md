# Store listing — Español (es)

> Paste-ready para Partner Center → Store listings → **Español** (neutral — un solo texto para
> España y Latinoamérica). Refreshed 2026-09-08 for v1.27.0.0 (what's-new + language claims). Drafted 2026-09-05 from `listing-copy.md` (v1.25.0.0 state).
> Product nouns stay in English (RoRoRo, Squad Launch, Friend Follow, Pushover, ntfy); the
> app UI is now localized to Spanish (v1.26.0.0). Register: tú.

## Short description (≤200 chars)

```
Multi-launcher para Windows en 6 idiomas: varios clientes de Roblox a la vez, cada uno con su cuenta. Baúl cifrado, Squad Launch, vigilante de memoria, alertas al móvil, estado en vivo y temas.
```

## Long description

```
Multi-launcher para Windows.

RoRoRo es un launcher para Windows que ejecuta varios clientes de Roblox a la vez en un mismo PC, cada uno con una cuenta guardada distinta que te pertenece. Añade tus cuentas una sola vez desde la propia página de inicio de sesión de Roblox y luego lánzalas con un clic: a su juego predeterminado, a un servidor privado guardado o a cualquier enlace de juego que pegues.

Lo que obtienes:
• Multi-instancia con un clic. RoRoRo retiene el mutex singleton de Roblox, así que los clientes adicionales se abren en lugar de traer el primero al frente.
• Baúl de cuentas cifrado (DPAPI). Las cookies guardadas se cifran con la API de Protección de Datos de Windows, ligadas a tu cuenta de Windows: un archivo copiado a otro PC no se descifra. Mover cuentas entre tus propios equipos es una exportación deliberada, protegida con frase de contraseña.
• Estado en vivo de cada cuenta. Ve qué cuenta está en qué juego, quién lleva cuánto tiempo inactivo, y fija límites de FPS por cuenta que se mantienen.
• Squad Launch + Friend Follow. Manda todas las cuentas seleccionadas al mismo servidor privado, sigue a un amigo hasta el suyo o junta tus cuentas en un servidor público.
• Vigilante de memoria + Recycle. RoRoRo aprende cuánta RAM cuesta de verdad un cliente de Roblox en tu máquina y avisa antes de que se agote. Un clic cierra un cliente pesado y lo devuelve al mismo servidor donde estaba.
• Una sola ventana de herramientas. Juegos, ajustes, historial, diagnóstico, plugins y Acerca de son páginas de una misma ventana junto a tus cuentas, con atajos de teclado en todas partes — F1 muestra la lista.
• Temas. Cuatro integrados, incluido uno que nunca transmite significado solo con color, más un editor para crear el tuyo a partir de diez colores y compartirlo como archivo.
• Alertas opcionales: escritorio, Discord o móvil. Dirige cada alerta a cualquier combinación: notificaciones de escritorio, un webhook de Discord que tú creas, o tu móvil mediante Pushover o ntfy. Desconexiones, avisos de memoria, finales de Recycle y una señal de "todo bien" cada dos horas. Una instalación nueva no hace ninguna llamada de alertas: nada sale hasta que tú lo configuras.
• Bandeja del sistema con icono coloreado por estado — el estado de multi-instancia de un vistazo; doble clic lanza tu cuenta principal.
• Sistema de plugins. Los plugins opcionales corren como procesos separados y no tienen permisos hasta que se los concedes uno a uno.
• Actualización automática vía Velopack. Una configuración remota sigue la versión de Roblox y el nombre del mutex conocidos, para que un cambio del lado de Roblox no te deje fuera mucho tiempo.
• Accesibilidad medida. Cada control anuncia su nombre a las tecnologías de asistencia, y el contraste se verifica sobre píxeles renderizados en todos los temas.
• Habla tu idioma. Toda la app —cada menú, ajuste, información sobre herramientas y ventana, y los mensajes que RoRoRo escribe mientras la usas— está traducida al francés, alemán, ruso, portugués (Brasil), polaco y español, según el idioma de tu Windows o tu elección en Configuración. El cambio es inmediato, y la lista solo ofrece idiomas totalmente traducidos, así que nunca acabas en una pantalla a medias en inglés.

Privacidad y seguridad:
RoRoRo nunca ve tu contraseña de Roblox. El inicio de sesión ocurre por completo en la página del propio Roblox, incrustada en un marco Microsoft Edge WebView2: el mismo HTML, la misma conexión HTTPS que haría tu navegador. RoRoRo captura solo la cookie de sesión que Roblox establece tras iniciar sesión, y la cifra antes de escribirla en disco. Sin telemetría. Sin analítica. Nada sale de tu máquina salvo las llamadas a Roblox durante el lanzamiento —las mismas que hace Roblox.com desde tu navegador— y, solo si tú los configuras, las alertas a tu webhook de Discord o al servicio de push que elegiste (Pushover o ntfy).

Importante: aviso de marcas y afiliación.
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RoRoRo es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término se usa únicamente para describir compatibilidad con la plataforma Roblox. RoRoRo lanza el cliente oficial de Roblox sin modificarlo: sin inyección, sin hooks, sin alterar el proceso de Roblox; solo retiene un mutex con nombre de Windows antes del lanzamiento, para que las siguientes instancias del cliente vean la comprobación de singleton como ya ocupada.

Un producto de 626 Labs.
```

## Product features (18 entries, ≤200 chars each)

```
Launcher multi-instancia para Roblox en Windows, con un clic
Baúl de cuentas cifrado con DPAPI y exportación protegida con frase de contraseña
Estado en vivo por cuenta: en qué juego está, tiempo inactivo y límite de FPS por cuenta
Vigilante de memoria que aprende el costo real de RAM de cada cliente, más Recycle de un clic al mismo servidor
Squad Launch y Friend Follow: el mismo servidor privado, o un servidor público juntos
Unirse por enlace desde cualquier URL de roblox.com, con servidores privados guardados por cuenta
Una ventana de herramientas para Juegos, Ajustes, Historial y más, con atajos de teclado
Cuatro temas integrados más un editor para crear el tuyo
Alertas opcionales de Discord a un webhook que tú creas
Bandeja del sistema con icono de estado y doble clic para la cuenta principal
Sistema de plugins con consentimiento por capacidad y aislamiento fuera de proceso
Actualización automática que sigue funcionando cuando Roblox cambia por debajo
Iniciar con Windows si quieres: un solo interruptor, y la lista de Inicio de Windows manda
Discord Join arranca RoRoRo aunque esté cerrado, y siempre pregunta antes de lanzar nada
Alertas al móvil vía Pushover o ntfy: un alt se cae y tu móvil vibra, aun con Discord cerrado
Señales de "todo bien" cada dos horas mientras tus cuentas corren: el silencio significa problema
Las alertas se reparten: escritorio, canales de Discord y móvil en cualquier combinación, por alerta
Disponible en seis idiomas — toda la app, pantallas y mensajes, en francés, alemán, ruso, portugués (Brasil), polaco o español, cambio inmediato, según Windows o tu elección
```

## What's new in this version (v1.27.0.0, ≤1500 chars)

```
v1.27.0.0

RoRoRo habla tu idioma
• Toda la app — menús, ajustes, cada ventana y los mensajes
  que RoRoRo escribe mientras la usas — está traducida al
  francés, alemán, ruso, portugués (Brasil), polaco y español.
  Si Windows está en uno de ellos, RoRoRo lo adopta solo.

Cámbialo cuando quieras, al instante
• Configuración > Apariencia solo lista los idiomas a los que
  RoRoRo está totalmente traducido, así nunca acabas en una
  pantalla a medias. Elige uno y la app cambia al momento, sin
  reiniciar.

Sigue siendo inglés de fondo
• El inglés sigue siendo el idioma predeterminado, y los
  nombres de funciones — Squad Launch, Recycle, RoRoRo — se
  leen igual en todos los idiomas. Tus cuentas, temas y
  ajustes no cambian.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Todos los derechos reservados. "Roblox" es una marca de Roblox Corporation. RoRoRo no está afiliada, avalada ni patrocinada por Roblox Corporation.
```

## Trademark info

```
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RORORO es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término se usa únicamente para describir compatibilidad con la plataforma Roblox. RORORO lanza el cliente oficial de Roblox sin modificaciones.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts-running.png`, `02-themes.png`, `03-about.png`, `04-games.png`, `05-diagnostics.png`, `06-history.png`, `07-plugins.png`, `08-theme-builder.png`, `09-compact.png`, `10-multi-instance.png`.

```
Tres cuentas ejecutándose a la vez, cada una con su propio uso de memoria y su propio botón de parada. Las cookies se cifran por usuario con Windows DPAPI y nunca salen de la máquina.
Cuatro temas integrados. Flatline no transmite ningún significado mediante el color, así que no se pierde nada con daltonismo, un panel malo o sol directo.
Multi-launcher para Windows. Retiene el mutex singleton de Roblox para que el siguiente cliente se abra en vez de pelear con el primero. Una reimplementación limpia, no un fork.
Guarda los juegos y servidores privados a los que juegas de verdad y elige uno distinto por cuenta antes de lanzar.
Diagnóstico muestra lo que RoRoRo ve ahora mismo: versiones, estado y dónde están los registros, para cuando necesites reportar algo.
Cada lanzamiento queda registrado, así ves qué cuenta jugó a qué y durante cuánto tiempo.
Los plugins se ejecutan como procesos separados y preguntan primero. Concedes cada permiso por su nombre y puedes revocarlo después.
Crea un tema con diez colores y aparece en el selector. Es un archivo JSON, así que puedes pasárselo a otra persona.
El modo compacto muestra solo lo que está en marcha. Fíjalo en una esquina de la pantalla y vuelve al juego.
Ocho clientes de Roblox, ocho cuentas, un PC. El título de cada ventana lleva la cuenta que ha iniciado sesión en ella, así siempre sabes cuál es cuál.
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
