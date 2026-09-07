# Store listing — Français (fr)

> Paste-ready pour Partner Center → Store listings → **Français**. Drafted 2026-09-05 from
> `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo, Squad Launch,
> Friend Follow, Pushover, ntfy); the long description says plainly that the app's interface
> is in English — the never-lie rule applied to listings. Register: vous (Store norm).

## Short description (≤200 chars)

```
Multi-lanceur Windows : plusieurs clients Roblox côte à côte, chacun sur son compte. Coffre chiffré, Squad Launch, surveillance mémoire, alertes téléphone, statut en direct, thèmes, auto-update.
```

## Long description

```
Multi-lanceur pour Windows.

RoRoRo est un lanceur Windows qui fait tourner plusieurs clients Roblox sur un même PC, chacun connecté à un compte différent que vous possédez. Ajoutez vos comptes une fois via la page de connexion officielle de Roblox, puis lancez-les d'un clic — vers leur jeu par défaut, un serveur privé enregistré, ou n'importe quel lien de jeu collé.

À noter : l'interface de l'application est en anglais pour le moment.

Ce que vous obtenez :
• Multi-instance en un clic. RoRoRo détient le mutex singleton de Roblox : les clients supplémentaires s'ouvrent au lieu de ramener le premier au premier plan.
• Coffre de comptes chiffré (DPAPI). Les cookies enregistrés sont chiffrés avec l'API de protection des données de Windows, liés à votre compte Windows — un fichier copié sur un autre PC ne se déchiffre pas. Déplacer ses comptes entre ses propres PC passe par un export volontaire protégé par phrase secrète.
• Statut en direct pour chaque compte. Voyez quel compte est dans quel jeu, qui est inactif et depuis combien de temps, avec une limite de FPS par compte qui tient.
• Squad Launch + Friend Follow. Envoyez tous les comptes sélectionnés dans le même serveur privé, suivez un ami dans le sien, ou réunissez vos comptes dans un serveur public.
• Surveillance mémoire + Recycle. RoRoRo apprend ce qu'un client Roblox coûte réellement en RAM sur votre machine et prévient avant la saturation. Un clic ferme un client trop lourd et le renvoie dans le serveur où il était.
• Une seule fenêtre d'outils. Jeux, réglages, historique, diagnostics, plugins et À propos sont les pages d'une même fenêtre, avec raccourcis clavier partout — F1 affiche la liste.
• Thèmes. Quatre intégrés, dont un qui ne repose jamais sur la couleur seule, plus un éditeur pour créer le vôtre à partir de dix couleurs et le partager en fichier.
• Alertes en option — bureau, Discord ou téléphone. Routez chaque alerte vers n'importe quelle combinaison : notifications bureau, un webhook Discord que vous créez, ou votre téléphone via Pushover ou ntfy. Déconnexions, alertes mémoire, fin de Recycle, et un signal « tout va bien » toutes les deux heures. Une installation neuve n'émet aucun appel d'alerte — rien ne part tant que vous n'avez rien configuré.
• Icône de zone de notification colorée selon l'état ; double-clic pour lancer le compte principal.
• Système de plugins. Des plugins optionnels tournent dans des processus séparés et n'ont aucune permission tant que vous ne les accordez pas un par un.
• Mise à jour automatique via Velopack. Une configuration distante suit la version et le nom de mutex Roblox connus, pour qu'un changement côté Roblox ne vous bloque pas longtemps.
• Accessibilité mesurée. Chaque contrôle annonce son nom aux technologies d'assistance, et le contraste est vérifié sur les pixels rendus dans chaque thème.

Confidentialité et sécurité :
Votre mot de passe Roblox n'est jamais vu par RoRoRo. La connexion se fait entièrement dans la page de Roblox, intégrée dans un cadre Microsoft Edge WebView2 — même HTML, même connexion HTTPS que votre navigateur. RoRoRo ne capture que le cookie de session posé par Roblox après connexion, et le chiffre avant de l'écrire sur disque. Pas de télémétrie. Pas d'analytique. Rien ne quitte votre machine hormis les appels Roblox du lancement — les mêmes que ceux de Roblox.com dans votre navigateur — et, uniquement si vous les configurez vous-même, les alertes vers votre webhook Discord ou le service de notification choisi (Pushover ou ntfy).

Important : marques et affiliation.
« Roblox » et le logo Roblox sont des marques de Roblox Corporation. RoRoRo est un outil tiers indépendant, non affilié à, ni approuvé, ni sponsorisé par Roblox Corporation. Le terme n'est utilisé que pour décrire la compatibilité avec la plateforme Roblox. RoRoRo lance le client Roblox officiel sans le modifier — aucune injection, aucun hook, aucune altération du processus Roblox ; il ne fait que détenir un mutex nommé Windows avant le lancement, pour que les instances de client suivantes voient le contrôle singleton comme déjà pris.

Un produit 626 Labs.
```

## Product features (17 entries, ≤200 chars each)

```
Lanceur multi-instance en un clic pour Roblox sur Windows
Coffre de comptes chiffré DPAPI, avec export protégé par phrase secrète
Statut en direct par compte — le jeu en cours, le temps d'inactivité, et une limite de FPS par compte
Surveillance mémoire qui apprend le vrai coût RAM de chaque client, plus Recycle en un clic vers le même serveur
Squad Launch et Friend Follow — même serveur privé, ou un serveur public ensemble
Rejoindre par lien depuis toute URL roblox.com, avec serveurs privés enregistrés par compte
Une fenêtre d'outils pour Jeux, Réglages, Historique et plus, avec raccourcis clavier partout
Quatre thèmes intégrés plus un éditeur pour créer le vôtre
Alertes Discord en option vers un webhook que vous créez
Zone de notification avec icône d'état colorée et double-clic pour le compte principal
Système de plugins avec consentement par capacité et isolation hors processus
Mise à jour automatique qui résiste aux changements côté Roblox
Démarrage avec Windows si vous voulez — un seul réglage, et la liste Démarrage de Windows garde la main
Discord Join démarre RoRoRo même fermé, et demande toujours avant de lancer quoi que ce soit
Alertes téléphone via Pushover ou ntfy — un alt se déconnecte et votre téléphone vibre, même sans Discord
Signaux de bon fonctionnement toutes les deux heures — un signal manquant veut dire qu'il faut vérifier
Les alertes se diffusent — bureau, salons Discord et téléphone, dans n'importe quelle combinaison
```

## What's new in this version (v1.25.0.0, ≤1500 chars)

```
v1.25.0.0

Votre téléphone peut vibrer
• Settings > Alerts : routez toute alerte vers votre téléphone via
  Pushover ou ntfy. Un alt se déconnecte — votre téléphone le sait,
  même Discord fermé. Configuration unique, et Test my phone
  prouve que ça marche. Vos clés restent chiffrées sur votre PC ;
  rien n'est envoyé sauf quand une alerte part, et uniquement vers
  le service que vous avez choisi.

Les alertes vont partout où vous cochez
• Bureau, votre salon Discord, celui du clan, votre téléphone —
  cochez n'importe quelle combinaison par alerte. Votre ancien
  routage a été repris automatiquement.

Deux nouvelles alertes, désactivées par défaut
• La fin d'un Recycle indique la mémoire récupérée. Les signaux de
  bon fonctionnement disent « 4h up — 6 accounts in » toutes les
  deux heures — un signal qui n'arrive pas signifie que le PC ou
  l'application s'est arrêté — la seule panne que rien ne peut
  annoncer directement.

Roblox reste en fenêtré
• Si un crash ou Alt+Entrée a laissé Roblox en plein écran, RoRoRo
  corrige cela avant chaque lancement. Option dans Settings > Startup, activée par défaut.

Vous revenez en arrière ?
• Si vous repassez à une version plus ancienne après avoir configuré
  le nouveau routage d'alertes, reconfigurez-le là-bas — les
  anciennes versions ignorent en silence les options inconnues.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Tous droits réservés. « Roblox » est une marque de Roblox Corporation. RoRoRo n'est ni affilié à, ni approuvé, ni sponsorisé par Roblox Corporation.
```

## Trademark info

```
« Roblox » et le logo Roblox sont des marques de Roblox Corporation. RORORO est un outil tiers indépendant, non affilié à, ni approuvé, ni sponsorisé par Roblox Corporation. Le terme n'est utilisé que pour décrire la compatibilité avec la plateforme Roblox. RORORO lance le client Roblox officiel sans le modifier.
```
