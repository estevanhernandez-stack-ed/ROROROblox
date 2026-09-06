# Store listing — Español (es)

> Paste-ready para Partner Center → Store listings → **Español** (neutral — un solo texto para
> España y Latinoamérica). Drafted 2026-09-05 from `listing-copy.md` (v1.25.0.0 state).
> Product nouns stay in English (RoRoRo, Squad Launch, Friend Follow, Pushover, ntfy); the
> long description says plainly that the app's interface is in English. Register: tú.

## Short description (≤200 chars)

```
Multi-launcher para Windows: varios clientes de Roblox a la vez, cada uno con su cuenta. Baúl cifrado, Squad Launch, vigilante de memoria, alertas al móvil, estado en vivo, temas.
```

## Long description

```
Multi-launcher para Windows.

RoRoRo es un launcher para Windows que ejecuta varios clientes de Roblox a la vez en un mismo PC, cada uno con una cuenta guardada distinta que te pertenece. Añade tus cuentas una sola vez desde la propia página de inicio de sesión de Roblox y luego lánzalas con un clic: a su juego predeterminado, a un servidor privado guardado o a cualquier enlace de juego que pegues.

Aviso: la interfaz de la aplicación está en inglés por ahora.

Lo que obtienes:
• Multi-instancia con un clic. RoRoRo retiene el mutex singleton de Roblox, así que los clientes adicionales se abren en lugar de traer el primero al frente.
• Baúl de cuentas cifrado (DPAPI). Las cookies guardadas se cifran con la API de Protección de Datos de Windows, ligadas a tu cuenta de Windows: un archivo copiado a otro PC no se descifra. Mover cuentas entre tus propios equipos es una exportación deliberada, protegida con frase de contraseña.
• Estado en vivo de cada cuenta. Ve qué cuenta está en qué juego, quién lleva cuánto tiempo inactivo, y fija límites de FPS por cuenta que se mantienen.
• Squad Launch + Friend Follow. Manda todas las cuentas seleccionadas al mismo servidor privado, sigue a un amigo hasta el suyo o junta tus cuentas en un servidor público.
• Vigilante de memoria + Recycle. RoRoRo aprende cuánta RAM cuesta de verdad un cliente de Roblox en tu máquina y avisa antes de que se agote. Un clic cierra un cliente pesado y lo devuelve al mismo servidor donde estaba.
• Una sola ventana de herramientas. Juegos, ajustes, historial, diagnóstico, plugins y Acerca de son páginas de una misma ventana junto a tus cuentas, con atajos de teclado en todas partes.
• Temas. Cuatro integrados, incluido uno que nunca transmite significado solo con color, más un editor para crear el tuyo y compartirlo como archivo.
• Alertas opcionales: escritorio, Discord o móvil. Dirige cada alerta a cualquier combinación: notificaciones de escritorio, un webhook de Discord que tú creas, o tu móvil mediante Pushover o ntfy. Desconexiones, avisos de memoria, finales de Recycle y una señal de "todo bien" cada dos horas. Una instalación nueva no hace ninguna llamada de alertas: nada sale hasta que tú lo configuras.
• Bandeja del sistema con icono coloreado por estado; doble clic lanza tu cuenta principal.
• Sistema de plugins. Los plugins opcionales corren como procesos separados y no tienen permisos hasta que se los concedes uno a uno.
• Actualización automática vía Velopack. Una configuración remota sigue la versión de Roblox y el nombre del mutex conocidos, para que un cambio del lado de Roblox no te deje fuera mucho tiempo.
• Accesibilidad medida. Cada control anuncia su nombre a las tecnologías de asistencia, y el contraste se verifica sobre píxeles renderizados en todos los temas.

Privacidad y seguridad:
RoRoRo nunca ve tu contraseña de Roblox. El inicio de sesión ocurre por completo en la página del propio Roblox, incrustada en un marco Microsoft Edge WebView2: el mismo HTML, la misma conexión HTTPS que haría tu navegador. RoRoRo captura solo la cookie de sesión que Roblox establece tras iniciar sesión, y la cifra antes de escribirla en disco. Sin telemetría. Sin analítica. Nada sale de tu máquina salvo las llamadas a Roblox durante el lanzamiento —las mismas que hace Roblox.com desde tu navegador— y, solo si tú los configuras, las alertas a tu webhook de Discord o al servicio de push que elegiste (Pushover o ntfy).

Importante: aviso de marcas y afiliación.
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RoRoRo es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término se usa únicamente para describir compatibilidad con la plataforma Roblox. RoRoRo lanza el cliente oficial de Roblox sin modificarlo: sin inyección, sin hooks, sin alterar el proceso de Roblox; solo retiene un mutex con nombre de Windows antes del lanzamiento.

Un producto de 626 Labs.
```

## Product features (17 entries, ≤200 chars each)

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
```

## What's new in this version (v1.25.0.0, ≤1500 chars)

```
v1.25.0.0

Tu móvil ya puede vibrar
• Ajustes > Alerts: dirige cualquier alerta a tu móvil mediante
  Pushover o ntfy. Un alt se cae y tu móvil lo sabe, incluso con
  Discord cerrado. Configuración única, y el botón de prueba
  demuestra que funciona. Tus claves quedan cifradas en tu PC; no
  se envía nada salvo cuando salta una alerta, y solo al servicio
  que elegiste.

Las alertas van a donde tú marques
• Escritorio, tu canal de Discord, el del clan, tu móvil: cualquier
  combinación por alerta. Tu enrutado anterior se conservó
  automáticamente.

Dos alertas nuevas, apagadas por defecto
• Un Recycle terminado te dice cuánta memoria recuperó. Las señales
  de actividad dicen "4h up — 6 accounts in" cada dos horas: una
  señal que no llega significa que el PC o la app murieron.

Roblox se queda en ventana
• Si un fallo o Alt+Intro dejó Roblox guardado en pantalla completa,
  RoRoRo lo corrige antes de cada lanzamiento. Configurable,
  activado por defecto.

¿Vuelves a una versión anterior?
• Si vuelves atrás después de configurar el nuevo enrutado de
  alertas, configúralo allí de nuevo: las versiones antiguas omiten
  en silencio las opciones que no conocen.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. "Roblox" es una marca de Roblox Corporation. RoRoRo no está afiliada, avalada ni patrocinada por Roblox Corporation.
```

## Trademark info

```
"Roblox" y el logotipo de Roblox son marcas de Roblox Corporation. RORORO es una herramienta independiente de terceros, no afiliada, avalada ni patrocinada por Roblox Corporation. El término se usa únicamente para describir compatibilidad con la plataforma Roblox. RORORO lanza el cliente oficial de Roblox sin modificaciones.
```
