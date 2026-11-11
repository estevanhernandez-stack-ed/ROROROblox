# Store listing — Français (fr)

> Paste-ready pour Partner Center → Store listings → **Français**. Refreshed 2026-11-05 for v1.36.0.0 (launcher-first short description; long description opens with the one-account story, three new bullets, Library in the tools-window bullet, account-facts privacy sentence; features merged/added to stay 20; v1.36 caption order with the two `-en` carry-over frames; what's-new; all matching the English sheet's ab48e6b edit; UI labels from this culture's catalog; translation verifier run pending). Refreshed 2026-10-26 for v1.35.0.0 (what's-new only; block drafted from whats-new-1.35.0.0.md, UI labels from this culture's catalog, translation verifier run pending). Refreshed 2026-10-23 for v1.34.0.0 (what's-new only; the Phase 2 listing audit found nothing else to change; block verified 6/6 by the translation verifier, UI labels from this culture's catalog). Refreshed 2026-10-07 for v1.33.0.0 (idle added to the alert-kind list in the long description, plus the repeat-interval and alert-sound controls — matching the English sheet's 1c84d28 edit; idle wording taken from this culture's shipped SettingsPage_AnAccountGoesIdle). Refreshed 2026-09-29 for v1.32.0.0 (phone-alert and alert fan-out entries merged into one; new Rejoin if it drops out entry; still 20; Rejoin bullet in the long description). Refreshed 2026-09-23 for v1.31.0.0 (feature entries for Known Roblox issues and per-account frame-rate caps, 18 → 20; privacy sentence; tools-window page list). Refreshed 2026-09-08 for v1.27.0.0 (what's-new + language claims). Drafted 2026-09-05 from
> `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo, Squad Launch,
> Friend Follow, Pushover, ntfy). The interface is now localized to six languages, so the
> earlier English-interface caveat is gone. Register: vous (Store norm).

## Short description (≤200 chars)

> **Rewritten 2026-11-05 (v1.36.0.0): launcher first.** Translated from the English sheet's
> ab48e6b snippet: what one account gets leads, alts come second (v1.36 spec §7.7). "7" counts
> English; the old "6" counted only the translations. 196/200 chars.

```
Un seul endroit pour lancer Roblox sous Windows : vos jeux, limites de FPS, alertes téléphone, thèmes, en 7 langues. Des alts au besoin : plusieurs comptes enregistrés côte à côte, un clic chacun.
```

## Long description

> **Rewritten 2026-11-05 (v1.36.0.0)**, same edits as the English sheet: the one-account story first,
> then the multi-account paragraph that used to open the listing; three bullets added (accounts at a
> glance, health badges, the Library); the tools-window bullet says Library; the privacy paragraph
> says where the account facts come from and where they are kept. 17 bullets, as English.

```
Un seul endroit pour lancer Roblox sous Windows.

Ajoutez votre compte Roblox une fois, via la page de connexion officielle de Roblox, et RoRoRo devient votre lanceur : tous vos jeux dans une seule Bibliothèque, un clic pour entrer dans un jeu ou un serveur privé enregistré, une limite de FPS qui tient, et une alerte sur votre téléphone si vous décrochez du jeu. Votre compte affiche d'un coup d'œil ses Robux, Plus et amis en ligne, et vous prévient quand il demande votre attention.

Quand vous voulez des alts, RoRoRo fait tourner plusieurs clients Roblox sur un même PC, chacun connecté à un compte différent que vous possédez. Lancez-les d'un clic — vers leur jeu par défaut, un serveur privé enregistré, ou n'importe quel lien de jeu collé.

Ce que vous obtenez :
• Multi-instance en un clic. RoRoRo détient le mutex singleton de Roblox : les clients supplémentaires s'ouvrent au lieu de ramener le premier au premier plan.
• Coffre de comptes chiffré (DPAPI). Les cookies enregistrés sont chiffrés avec l'API de protection des données de Windows, liés à votre compte Windows — un fichier copié sur un autre PC ne se déchiffre pas. Déplacer ses comptes entre ses propres PC passe par un export volontaire protégé par phrase secrète.
• Statut en direct pour chaque compte. Voyez quel compte est dans quel jeu, qui est inactif et depuis combien de temps, avec une limite de FPS par compte qui tient.
• Chaque compte d'un coup d'œil. Robux, Plus et amis en ligne s'affichent sous chaque compte, avec une note bien à vous. Ouvrez un compte pour voir ses jeux récents, ses favoris et ses serveurs privés, et jouez à n'importe lequel d'un clic.
• Sait quand un compte a besoin de vous. Session expirée, Vérification requise, Limité par Roblox : chacun a son propre badge, et un clic lance la correction.
• Une seule Bibliothèque pour tous vos jeux. Tout ce à quoi vous avez joué, mis en favori ou enregistré comme serveur privé, en grille ou en liste. Cherchez, filtrez, collez un lien de jeu, et choisissez quel compte joue.
• Squad Launch + Friend Follow. Envoyez tous les comptes sélectionnés dans le même serveur privé, suivez un ami dans le sien, ou réunissez vos comptes dans un serveur public.
• Surveillance mémoire + Recycle. RoRoRo apprend ce qu'un client Roblox coûte réellement en RAM sur votre machine et prévient avant la saturation. Un clic ferme un client trop lourd et le renvoie dans le serveur où il était.
• Rejoindre à nouveau s'il décroche. Activez-le compte par compte : un alt dont la fenêtre Roblox est toujours ouverte mais qui est sorti du jeu (une expulsion pour inactivité, une connexion échouée) est fermé et rejoint à nouveau tout seul. Au plus trois fois par heure, jamais votre compte principal, et un « Arrêter » de votre part l'emporte toujours.
• Une seule fenêtre d'outils. Bibliothèque, réglages, historique, diagnostics, plugins, Problèmes Roblox connus et À propos sont les pages d'une même fenêtre, à côté de vos comptes, avec raccourcis clavier partout — F1 affiche la liste.
• Thèmes. Quatre intégrés, dont un qui ne repose jamais sur la couleur seule, plus un éditeur pour créer le vôtre à partir de dix couleurs et le partager en fichier.
• Alertes en option — bureau, Discord ou téléphone. Routez chaque alerte vers n'importe quelle combinaison : notifications bureau, un webhook Discord que vous créez, ou votre téléphone via Pushover ou ntfy. Déconnexions, alertes mémoire, comptes inactifs, fin de Recycle, et un signal « tout va bien » toutes les deux heures pendant que vos comptes tournent. Choisissez à quelle fréquence au maximum chaque alerte peut se répéter et le son qu'elle émet. Une installation neuve n'émet aucun appel d'alerte — rien ne part tant que vous n'avez rien configuré.
• Icône de zone de notification colorée selon l'état — l'état multi-instance en un coup d'œil ; double-clic pour lancer le compte principal.
• Système de plugins. Des plugins optionnels tournent dans des processus séparés et n'ont aucune permission tant que vous ne les accordez pas un par un.
• Mise à jour automatique via Velopack. Une configuration distante suit la version et le nom de mutex Roblox connus, pour qu'un changement côté Roblox ne vous bloque pas longtemps.
• Accessibilité mesurée. Chaque contrôle annonce son nom aux technologies d'assistance, et le contraste est vérifié sur les pixels rendus dans chaque thème.
• Parle votre langue. Toute l'application — chaque menu, réglage, info-bulle et fenêtre, et les messages que RoRoRo écrit pendant que vous l'utilisez — est traduite en français, allemand, russe, portugais (Brésil), polonais et espagnol, selon la langue de votre Windows ou votre choix dans les Paramètres. Le changement est immédiat, et le sélecteur ne propose qu'une langue entièrement traduite, pour que vous ne tombiez jamais sur un écran à moitié en anglais.

Confidentialité et sécurité :
Votre mot de passe Roblox n'est jamais vu par RoRoRo. La connexion se fait entièrement dans la page de Roblox, intégrée dans un cadre Microsoft Edge WebView2 — même HTML, même connexion HTTPS que votre navigateur. RoRoRo ne capture que le cookie de session posé par Roblox après connexion, et le chiffre avant de l'écrire sur disque. Les informations de compte (Robux, Plus, amis en ligne) sont lues depuis Roblox avec la propre session de ce compte, et conservées uniquement dans le même coffre chiffré. Pas de télémétrie. Pas d'analytique. Rien de ce qui vous concerne ne quitte votre machine hormis les appels Roblox du lancement — les mêmes que ceux de Roblox.com dans votre navigateur — et, uniquement si vous les configurez vous-même, les alertes vers votre webhook Discord ou le service de notification choisi (Pushover ou ntfy). RoRoRo télécharge aussi ses propres fichiers signés depuis ses publications GitHub — les mises à jour, les réglages de compatibilité Roblox et la liste des problèmes Roblox connus — sans rien envoyer à votre sujet pour les obtenir.

Important : marques et affiliation.
« Roblox » et le logo Roblox sont des marques de Roblox Corporation. RoRoRo est un outil tiers indépendant, non affilié à, ni approuvé, ni sponsorisé par Roblox Corporation. Le terme n'est utilisé que pour décrire la compatibilité avec la plateforme Roblox. RoRoRo lance le client Roblox officiel sans le modifier — aucune injection, aucun hook, aucune altération du processus Roblox ; il ne fait que détenir un mutex nommé Windows avant le lancement, pour que les instances de client suivantes voient le contrôle singleton comme déjà pris.

Un produit 626 Labs.
```

## Product features (20 entries, ≤200 chars each)

> **2026-11-05 (v1.36.0.0):** still 20/20, as English. The two frame-rate entries merge into Live
> status, freeing a slot for accounts at a glance; join-by-link folds into the new Library entry; the
> tools-window entry says Library.

```
Lanceur multi-instance en un clic pour Roblox sur Windows
Coffre de comptes chiffré DPAPI, avec export protégé par phrase secrète
Statut en direct par compte — le jeu en cours, le temps d'inactivité, et des limites de FPS qui tiennent, relevées, abaissées ou supprimées client par client
Chaque compte d'un coup d'œil — Robux, Plus, amis en ligne et votre note, avec un badge de correction en un clic quand une session expire ou demande une vérification
Surveillance mémoire qui apprend le vrai coût RAM de chaque client, plus Recycle en un clic vers le même serveur
Squad Launch et Friend Follow — même serveur privé, ou un serveur public ensemble
Une seule Bibliothèque pour tous vos jeux — grille ou liste, recherche et filtres, collez n'importe quel lien roblox.com, et choisissez quel compte joue
Une fenêtre d'outils pour Bibliothèque, Réglages, Historique et plus, avec raccourcis clavier partout
Quatre thèmes intégrés plus un éditeur pour créer le vôtre
Alertes Discord en option vers un webhook que vous créez
Zone de notification avec icône d'état colorée et double-clic pour le compte principal
Système de plugins avec consentement par capacité et isolation hors processus
Mise à jour automatique qui résiste aux changements côté Roblox
Démarrage avec Windows si vous voulez — un seul réglage, et la liste Démarrage de Windows garde la main
Discord Join démarre RoRoRo même fermé, et demande toujours avant de lancer quoi que ce soit
Alertes téléphone via Pushover ou ntfy, et vers le bureau et les salons Discord dans n'importe quelle combinaison, par alerte — un alt se déconnecte et votre téléphone vibre
Signaux de bon fonctionnement toutes les deux heures — un signal manquant veut dire qu'il faut vérifier
Disponible en six langues — toute l'app, écrans et messages, en français, allemand, russe, portugais (Brésil), polonais ou espagnol, changement immédiat, selon Windows ou votre choix
Problèmes Roblox connus — les problèmes côté Roblox, que faire, et la fonction RoRoRo qui aide, tenus à jour sans mise à jour de l'app
Rejoindre à nouveau s'il décroche — un alt expulsé ou coincé hors du jeu est fermé et rejoint à nouveau tout seul, jamais votre compte principal
```

## What's new in this version (v1.37.0.0, ≤1500 chars)

```
v1.37.0.0

Moins d'impasses au démarrage

• Si ce PC n'a aucun Roblox que RoRoRo puisse lancer, RoRoRo
  vous le dit désormais et vous montre le téléchargement, au lieu
  d'un message Windows. Cela arrive quand Roblox vient du
  Microsoft Store : prenez-le sur roblox.com, les deux peuvent
  rester installés.
• Ajouter un compte déjà enregistré ne crée plus de doublon.
  RoRoRo renouvelle sa connexion et vous indique d'ajouter un
  autre compte Roblox pour en lancer deux à la fois.
• Nouvelles limites de FPS : 75, 100 et 180, pour les écrans qui
  tournent à ces fréquences.
• Après quelques bons jours, RoRoRo peut vous demander une fois
  une note sur le Microsoft Store. L'une ou l'autre réponse y met
  fin pour de bon.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Tous droits réservés. « Roblox » est une marque de Roblox Corporation. RoRoRo n'est ni affilié à, ni approuvé, ni sponsorisé par Roblox Corporation.
```

## Trademark info

```
« Roblox » et le logo Roblox sont des marques de Roblox Corporation. RORORO est un outil tiers indépendant, non affilié à, ni approuvé, ni sponsorisé par Roblox Corporation. Le terme n'est utilisé que pour décrire la compatibilité avec la plateforme Roblox. RORORO lance le client Roblox officiel sans le modifier.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts.png`, `02-account-panel.png`, `03-account-health.png`, `04-library.png`, `05-multi-instance-en.png`, `06-history.png`, `07-themes.png`, `08-theme-builder.png`, `09-plugins.png`, `10-compact-en.png` (v1.36 order, 2026-11-05; `05` and `10` carry over from English, hence `-en`; see `screenshots/README.md`).

```
Chaque compte d'un coup d'œil : Robux, Plus, amis en ligne, votre note, et le jeu où chacun se trouve en ce moment.
Ouvrez un compte et ses jeux sont là : récents, favoris et serveurs privés, un clic pour jouer.
Quand un compte a besoin de vous, il le dit. Session expirée, vérification requise, limité par Roblox, chacun avec sa correction en un clic.
Tous les jeux auxquels vous avez joué, mis en favori ou enregistrés, dans une seule Bibliothèque. Cherchez, filtrez, et choisissez quel compte joue.
Huit clients Roblox, huit comptes, un seul PC. Chaque titre de fenêtre porte le compte qui y est connecté, vous savez donc toujours qui est qui.
Chaque lancement est enregistré, vous voyez donc quel compte a joué à quoi et pendant combien de temps.
Quatre thèmes intégrés. Flatline ne fait passer aucune information par la couleur, donc rien ne se perd avec le daltonisme, un mauvais écran ou en plein soleil.
Composez un thème à partir de dix couleurs et il apparaît dans le sélecteur. C'est un fichier JSON, vous pouvez donc le donner à quelqu'un d'autre.
Les plugins tournent comme des processus séparés et demandent d'abord. Vous accordez chaque capacité par son nom, et vous pouvez la révoquer ensuite.
Le mode compact n'affiche que ce qui tourne. Épinglez-le dans un coin de l'écran et retournez jouer.
```

## Keywords (max 7, 40 chars each, 21 words total — one per box)

```
roblox
multi-instance
multi-compte
launcher
gestionnaire de comptes
comptes alt
multibox
```
