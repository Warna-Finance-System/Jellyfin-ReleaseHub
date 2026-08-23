<div align="center">

<img src="media/logo.png" alt="Jellyfin ReleaseHub" width="180" />

# Jellyfin ReleaseHub

**Calendrier unifié de diffusion des séries et animés, directement dans Jellyfin.**

[![CI](https://github.com/Warna-Finance-System/Jellyfin-ReleaseHub/actions/workflows/ci.yml/badge.svg)](https://github.com/Warna-Finance-System/Jellyfin-ReleaseHub/actions/workflows/ci.yml)
![Jellyfin 10.11.x](https://img.shields.io/badge/Jellyfin-10.11.x-00a4dc)
![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512bd4)
![Licence GPL-3.0](https://img.shields.io/badge/licence-GPL--3.0-blue)

</div>

---

ReleaseHub ajoute un calendrier de diffusion, un système de suivi et une recherche de séries et
d'animés **nativement dans Jellyfin**. Il lit vos bibliothèques existantes, résout chaque série auprès
de TVMaze ou d'AnimeSchedule à partir des identifiants que Jellyfin possède déjà, et vous montre ce qui
arrive.

**Aucun Sonarr, Radarr, serveur externe, Node.js, Python ou Docker n'est nécessaire.**
**Aucun média n'est renommé, déplacé, supprimé ou téléchargé.** ReleaseHub est strictement en lecture seule.

## Captures d'écran

> _À compléter._

| Calendrier | À venir | Découvrir | Configuration |
|---|---|---|---|
| _(capture à venir)_ | _(capture à venir)_ | _(capture à venir)_ | _(capture à venir)_ |

## Fonctionnalités

- **Calendrier** sur 7, 14 ou 30 jours, groupé par jour, avec affiche, saison, épisode, heure et plateforme.
- **À venir** — vue compacte répartie en *Aujourd'hui*, *Demain*, *Cette semaine*, *Plus tard*.
- **Suivis** — suivez des séries et des animés **absents de votre bibliothèque**, sans rien télécharger.
- **Découvrir** — recherche sur TVMaze et AnimeSchedule, avec badges *Dans la bibliothèque* et *Suivi*.
- Filtres **Tout / Animés / Séries** sur toutes les vues.
- **Français et anglais**, avec détection automatique de la langue du client Jellyfin.
- **Compatible thèmes** — réutilise les classes CSS natives de Jellyfin ; fonctionne avec le thème par
  défaut, Abyss et les autres, et continue de fonctionner si vous changez de thème.
- **Responsive** — bureau, tablette et mobile.
- **File d'attente de confirmation** — une correspondance incertaine n'est jamais utilisée
  automatiquement ; elle attend votre validation.

### Ce que ReleaseHub ne fait jamais

| Interdit | Pourquoi |
|---|---|
| Renommer, déplacer ou supprimer un média | ReleaseHub est un outil d'information, pas un gestionnaire de fichiers |
| Créer des fichiers NFO ou modifier des affiches | Vos dossiers médias ne sont jamais écrits |
| Modifier les métadonnées Jellyfin | Il les **lit** uniquement |
| Télécharger des médias | Aucun téléchargement, jamais |
| Inventer une date ou une heure | Une date approximative est marquée comme telle ; sans heure, rien n'est affiché |
| Contourner une limite de débit | Les quotas des fournisseurs sont respectés, y compris leurs en-têtes |

## Prérequis

| | |
|---|---|
| **Jellyfin** | 10.11.11 (compatible 10.11.x) |
| **Framework** | .NET 9.0 — fourni par Jellyfin, rien à installer |
| **Clé API TVMaze** | ❌ Aucune |
| **Clé API AnimeSchedule** | ✅ Requise uniquement pour la partie animés |
| **File Transformation** | ⚠️ Optionnel — voir ci-dessous |

### À propos de File Transformation

Jellyfin 10.11 **n'offre aucun mécanisme officiel** pour exposer une page de plugin aux utilisateurs
non-administrateurs : la route `#/configurationpage` est protégée par un garde `level:"admin"`.

- **Sans** [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) :
  ReleaseHub fonctionne pleinement, mais reste accessible depuis **Tableau de bord → ReleaseHub**,
  donc réservé aux administrateurs.
- **Avec** File Transformation : ReleaseHub ajoute lui-même une entrée **ReleaseHub** dans le menu
  principal, visible par **tous les utilisateurs**.

La détection est automatique et se fait par réflexion, sans aucune dépendance de compilation. Si le
plugin est absent, ReleaseHub le signale dans sa page de configuration et continue de fonctionner.

## Installation

### Via le dépôt de plugins (recommandé)

1. Dans Jellyfin : **Tableau de bord → Extensions → Dépôts → `+`**
2. Nom : `ReleaseHub` — URL :
   ```
   https://raw.githubusercontent.com/Warna-Finance-System/Jellyfin-ReleaseHub/main/manifest.json
   ```
3. **Catalogue** → catégorie *General* → **Jellyfin ReleaseHub** → *Installer*
4. **Redémarrez Jellyfin.** Les plugins ne sont chargés qu'au démarrage.

### Installation manuelle

1. Téléchargez `ReleaseHub_<version>.zip` depuis la
   [page des releases](https://github.com/Warna-Finance-System/Jellyfin-ReleaseHub/releases).
2. Décompressez-le dans `<données Jellyfin>/plugins/ReleaseHub_<version>/`
   - Windows : `C:\ProgramData\Jellyfin\Server\plugins\`
   - Linux : `/var/lib/jellyfin/plugins/`
   - Docker : `/config/plugins/`
3. Redémarrez Jellyfin.

## Configuration

**Tableau de bord → Extensions → Jellyfin ReleaseHub**

### Général
| Réglage | Défaut | Rôle |
|---|---|---|
| Langue | Suivre le client | Force `fr-FR` / `en-US`, ou suit la langue de chaque utilisateur |
| Plage du calendrier | 7 jours | Vue par défaut : 7, 14 ou 30 jours |
| Activer les séries / les animés | Activés | Désactive complètement l'une des deux moitiés |

### TVMaze
| Réglage | Défaut |
|---|---|
| Activé | ✅ |
| URL de base | `https://api.tvmaze.com` |

TVMaze **ne nécessite aucune clé API** pour un usage public normal — c'est pourquoi aucun champ de clé
n'existe pour ce fournisseur. L'URL de base est configurable uniquement au cas où TVMaze déplacerait
son point d'entrée.

### AnimeSchedule
| Réglage | Défaut |
|---|---|
| Activé | ❌ |
| URL de base | `https://animeschedule.net/api/v3` |
| Clé API | _(vide)_ |

**Obtenir une clé :** créez un compte sur [animeschedule.net](https://animeschedule.net/), puis une
*Application* dans l'onglet **API** des paramètres de votre compte.

Le bouton **Test connection** vérifie la clé sans jamais l'afficher.

### Synchronisation
| Réglage | Défaut | Valeurs |
|---|---|---|
| Intervalle | 6 h | 1, 3, 6, 12, 24 h, ou manuel |
| Cache métadonnées série | 24 h | |
| Cache diffusions | 6 h | |
| Requêtes max par synchronisation | 500 | Plafond dur, en plus du limiteur de débit |

Deux tâches apparaissent dans **Tableau de bord → Tâches planifiées** :
*ReleaseHub - Synchronize Releases* et *ReleaseHub - Clear Cache*.

## Gestion de la clé API

La clé AnimeSchedule est traitée comme un secret :

- Stockée côté serveur dans `plugins/configurations/Jellyfin.Plugin.ReleaseHub.xml`.
- **Jamais renvoyée par l'API** — le point d'entrée `/ReleaseHub/Status` n'expose qu'un booléen
  `AnimeScheduleApiKeyConfigured`.
- **Jamais journalisée** — les messages d'erreur ne mentionnent que le code HTTP.
- **Jamais envoyée au navigateur** — toutes les requêtes partent du backend.
- Un champ vide dans la page de configuration signifie « conserver la clé existante » : enregistrer les
  paramètres ne peut pas l'effacer par accident.
- Le fichier de configuration est listé dans `.gitignore`.

## Limites de débit

| Fournisseur | Limite annoncée | Ce que fait ReleaseHub |
|---|---|---|
| TVMaze | « au moins 20 requêtes / 10 s » | Se limite à **12 / 10 s** |
| AnimeSchedule | 120 requêtes / min (par IP **et** par application) | Se limite à **90 / min** |

En plus de ces fenêtres glissantes :

- Les en-têtes `X-RateLimit-Remaining` / `X-RateLimit-Reset` d'AnimeSchedule sont respectés : ReleaseHub
  s'arrête **avant** de recevoir un 429.
- Un `429` avec `Retry-After` est honoré à la lettre et prime sur la fenêtre locale.
- Backoff exponentiel avec gigue sur les erreurs réseau et les `5xx`.
- Déduplication des requêtes simultanées : N appels concurrents vers la même URL ne produisent
  qu'un seul aller-retour.

**Aucun contournement de limite n'est implémenté**, conformément aux conditions d'utilisation des deux
fournisseurs.

## Cache

Base SQLite dans le dossier de données du plugin (`releasehub.db`), totalement séparée de celle de
Jellyfin. Elle contient les correspondances de fournisseurs, les métadonnées de séries, les diffusions,
les suivis et la file d'attente de confirmation.

Ouvrir le calendrier **ne déclenche jamais d'appel** à un fournisseur : la lecture se fait uniquement
depuis le cache. Si un fournisseur devient indisponible, les données en cache continuent d'être
affichées avec leur horodatage de dernière mise à jour, et la panne de l'un n'affecte jamais l'autre.

*ReleaseHub - Clear Cache* vide les données des fournisseurs mais **conserve** vos suivis et vos
correspondances confirmées manuellement — ce sont vos décisions, pas des données mises en cache.

## Langues

`fr-FR` et `en-US`. La langue est résolue dans cet ordre : réglage du plugin → attribut `data-culture`
de jellyfin-web → `navigator.language` → anglais.

Aucune chaîne visible n'est codée en dur : tout passe par
[`Localization/`](src/Jellyfin.Plugin.ReleaseHub/Localization/). Pour ajouter une langue, copiez
`en-US.json`, traduisez-le et déclarez la culture dans `Plugin.LocalizationCultures`.

## Dépannage

| Symptôme | Cause probable |
|---|---|
| Le plugin n'apparaît pas | Jellyfin n'a pas été redémarré ; les plugins ne se chargent qu'au démarrage |
| Statut *Not Supported* | Version de Jellyfin ≠ `targetAbi`. ReleaseHub cible 10.11.11 |
| Calendrier vide | Aucune synchronisation n'a encore eu lieu → *Synchronize now* dans la configuration |
| Une série de ma bibliothèque n'apparaît jamais | Regardez **Matches awaiting confirmation** : la correspondance était trop incertaine pour être utilisée automatiquement |
| Aucun animé | AnimeSchedule désactivé ou sans clé API |
| Pas d'entrée ReleaseHub dans le menu principal | File Transformation absent — ReleaseHub reste accessible depuis le tableau de bord |
| Heure absente sur un épisode | Le fournisseur n'en a pas donné. ReleaseHub n'invente pas d'heure |

Pour un diagnostic détaillé : activez **Enable debug logging** dans la configuration, puis consultez
**Tableau de bord → Journaux**. Les clés API n'y apparaissent jamais.

## Vie privée

ReleaseHub contacte uniquement les fournisseurs que vous activez, et n'envoie que ce qui est
nécessaire pour identifier une série : un titre, une année, ou un identifiant public (IMDb, TVDB,
AniDB, AniList, MyAnimeList).

Aucune donnée personnelle, aucun contenu de bibliothèque, aucune statistique de lecture et aucune
télémétrie ne quitte votre serveur. Il n'existe aucun service ReleaseHub distant.

## Attribution

- Données séries fournies par **[TVMaze](https://www.tvmaze.com/)**, sous licence
  [CC BY-SA](https://creativecommons.org/licenses/by-sa/4.0/). L'attribution est satisfaite par un lien
  retour, présent dans l'interface et ici.
- Données de diffusion animés fournies par **[AnimeSchedule.net](https://animeschedule.net/)**. Leurs
  conditions imposent un crédit visible (présent dans l'interface), interdisent l'usage commercial sans
  autorisation et interdisent l'utilisation des données pour entraîner des modèles d'IA.

> ReleaseHub n'est **affilié à, ni approuvé par** Jellyfin, TVMaze, AnimeSchedule, IMDb, TMDb, TVDB,
> AniDB, AniList ou tout autre fournisseur.

## Développement

### Prérequis
- SDK .NET 9.0 ou supérieur (testé avec le SDK 10.0.400 compilant vers `net9.0`)
- Un serveur Jellyfin 10.11.x local

### Compiler
```bash
dotnet build -c Release
dotnet test
```

### Compiler et installer sur un Jellyfin local (Windows)
```powershell
# Jellyfin verrouille le DLL du plugin tant qu'il tourne.
.\build.ps1 -RestartJellyfin

# Compiler sans toucher au serveur
.\build.ps1 -SkipInstall
```

Le script lit `build.yaml`, compile, copie le DLL et `logo.png` dans
`%ProgramData%\Jellyfin\Server\plugins\ReleaseHub_<version>\` et génère `meta.json`.

### Architecture

```
src/Jellyfin.Plugin.ReleaseHub/
├── Plugin.cs                  BasePlugin + IHasWebPages (sert tous les assets web)
├── PluginServiceRegistrator   Enregistrement DI
├── Api/                       Contrôleur /ReleaseHub/* + DTO client
├── Configuration/             Configuration persistée + page admin
├── Integration/               Hook File Transformation (réflexion, optionnel)
├── Localization/              fr-FR.json, en-US.json
├── Models/                    Modèle normalisé, commun aux fournisseurs
├── Providers/                 IReleaseProvider + TvMaze/ + AnimeSchedule/
├── ScheduledTasks/            Synchronisation, vidage du cache
├── Services/                  Cache, résolution, limiteur de débit, orchestration
└── Web/                       app.html, app.js, boot.js, releasehub.css
```

Les assets web sont des ressources embarquées servies par le mécanisme natif de Jellyfin, sur
`GET /web/ConfigurationPage?name=...`. Aucune dépendance frontend, aucune étape de build JavaScript.

### Publier une version

```bash
git tag v1.0.0.1
git push --tags
```

Le workflow [`release.yml`](.github/workflows/release.yml) compile, teste, crée l'archive, calcule son
MD5, publie la release GitHub et ajoute automatiquement l'entrée dans `manifest.json`. Le numéro de
version doit comporter **quatre parties** (`1.0.0.1`) : Jellyfin les compare comme des `System.Version`.

## Licence

[GPL-3.0](LICENSE) — comme Jellyfin lui-même.
