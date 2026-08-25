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
- **À venir** — vue compacte répartie en *Aujourd'hui*, *Demain*, *Cette semaine*, *Plus tard*, avec un
  bouton **Voir plus** qui charge la suite **à la suite** de ce qui est déjà affiché, sans reconstruire
  la page ni vous ramener en haut.
- **Suivis** — suivez des séries et des animés **absents de votre bibliothèque**, sans rien télécharger.
- **Films** — les sorties à venir d'une saga dont vous possédez déjà un film, via TMDb.
- **Découvrir** — recherche sur TVMaze, AnimeSchedule et TMDb, avec badges *Dans la bibliothèque* et *Suivi*.
- Filtres **Tout / Animés / Séries / Films** sur toutes les vues.
- **Français et anglais**, avec détection automatique de la langue du client Jellyfin.
- **Compatible thèmes** — réutilise les classes CSS natives de Jellyfin ; fonctionne avec le thème par
  défaut, Abyss et les autres, et continue de fonctionner si vous changez de thème.
- **Responsive** — bureau, tablette et mobile.
- **File d'attente de confirmation** — une correspondance incertaine n'est jamais utilisée
  automatiquement ; elle attend votre validation.
- **Retouches de l'interface web** — notamment un interrupteur pour masquer le carrousel *spotlight*
  du thème Abyss lorsqu'il se superpose à celui du plugin Media Bar. Voir
  [Interface web](#interface-web).

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
| **Jeton TMDb** | ✅ Requis uniquement pour la partie films |
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

Le bouton **Test connection** valide ce qui est actuellement dans le champ — une clé peut donc
être vérifiée avant d'être enregistrée. Un bouton afficher/masquer permet de la relire et de la
copier.

### TMDb (films)
| Réglage | Défaut |
|---|---|
| Activé | ❌ |
| Suivre les collections de films | ✅ |
| URL de base | `https://api.themoviedb.org/3` |
| URL de base des images | `https://image.tmdb.org/t/p/w500` |
| Jeton | _(vide)_ |

**Obtenir un jeton :** compte sur [themoviedb.org](https://www.themoviedb.org/) → *Paramètres* → *API* →
copiez l'**API Read Access Token**. C'est le long jeton v4, pas la clé v3 plus courte.

*Suivre les collections* est ce qui fait remonter le prochain film d'une saga dont vous possédez déjà
un épisode. C'est le seul endroit où ReleaseHub regarde au-delà des items exacts de votre
bibliothèque, d'où l'option séparée.

Deux particularités des films, assumées dans l'affichage : TMDb donne une **date sans horaire** (aucune
heure n'est donc inventée), et cette date n'est marquée *confirmée* que lorsque le film est terminé —
sinon elle est signalée comme approximative. Un film annoncé sans date reste affiché, marqué comme tel.

### Synchronisation
| Réglage | Défaut | Valeurs |
|---|---|---|
| Intervalle | 6 h | 1, 3, 6, 12, 24 h, ou manuel |
| Cache métadonnées série | 24 h | |
| Cache diffusions | 6 h | |
| Requêtes max par synchronisation | 500 | Plafond dur, en plus du limiteur de débit |

Deux tâches apparaissent dans **Tableau de bord → Tâches planifiées** :
*ReleaseHub - Synchronize Releases* et *ReleaseHub - Clear Cache*.

*Synchronize Releases* se lance aussi au démarrage du serveur, parce qu'un déclencheur par intervalle
est réarmé à zéro à chaque redémarrage : sur une machine qui redémarre plus souvent que l'intervalle,
il ne se déclencherait jamais. Une exécution dont les données ont moins de 30 minutes est ignorée pour
que ça reste sans coût — sauf si **vous** l'avez demandée, auquel cas elle part toujours.

#### Horizon de synchronisation

ReleaseHub récupère les sorties jusqu'à **365 jours** à l'avance. Passé cette limite, rien n'a jamais
été demandé à un fournisseur : le bouton *Voir plus* s'arrête donc là et le dit, au lieu d'afficher
des pages vides qui se liraient comme « rien n'est prévu » alors qu'elles signifient « rien n'a été
cherché ».

Cette année ne coûte **aucune requête supplémentaire**, parce que chaque fournisseur déclare jusqu'où
il vaut la peine de l'interroger :

| Fournisseur | Coût d'une fenêtre plus large | Horizon interrogé |
|---|---|---|
| TVMaze | **nul** — la liste d'épisodes complète arrive en une requête | 365 jours |
| TMDb | **nul** — une requête par film, indépendante de la fenêtre | 365 jours |
| AnimeSchedule | **une requête par semaine** | 182 jours |

AnimeSchedule garde une fenêtre plus courte pour deux raisons : c'est le seul dont le coût croît avec
la fenêtre, et sa grille ne va pas jusqu'à un an — les saisons sont annoncées environ un trimestre à
l'avance, donc les semaines au-delà coûteraient une requête chacune pour ne rien rapporter. Le reste
de l'année reste couvert par les deux fournisseurs que ça ne coûte rien d'interroger.

### Interface web

Page dédiée : **Tableau de bord → ReleaseHub → *Web interface adjustments***.

Elle regroupe les retouches que ReleaseHub applique à la page servie par jellyfin-web. Chacune est
appliquée à la **réponse HTTP**, jamais à un fichier sur le disque — donc rien de ce qu'un autre
plugin ou un thème a installé n'est modifié ni supprimé, et une mise à jour ne peut pas défaire votre
choix. Ces retouches nécessitent le plugin **File Transformation** ; sans lui, la page le dit
explicitement au lieu de laisser une case sans effet.

#### Masquer le spotlight d'Abyss

Le thème Abyss s'installe en écrivant dans jellyfin-web. Il ajoute entre autres à `index.html` une
balise qui monte son propre carrousel (une iframe) en haut de l'onglet Accueil :

```html
<script src="ui/spotlight-loader.js" data-abyss-spotlight></script>
```

Sur un serveur qui fait aussi tourner le plugin **Media Bar**, les deux carrousels se montent au même
endroit et se superposent. Celui du dessous transparaît sous l'autre : texte fantôme, bouton
d'information en trop, seconde rangée de points de pagination qui défile à son propre rythme.

Comme cette balise vit dans un fichier et non dans un plugin, désinstaller des plugins ne l'enlève
jamais, et modifier le fichier à la main est défait par la mise à jour suivante du thème ou du
serveur. ReleaseHub la retire de la réponse à la place. Tout ce qu'Abyss a installé reste intact sur
le disque : décocher la case fait revenir le carrousel immédiatement.

La page indique aussi ce qu'elle observe réellement — balise détectée ou non, actuellement retirée ou
non — ce qui distingue trois situations qu'un simple « ça ne marche pas » confondrait : File
Transformation absent, aucune page encore servie depuis le démarrage, ou Abyss réellement absent.

## Gestion de la clé API

La clé AnimeSchedule est stockée côté serveur dans
`plugins/configurations/Jellyfin.Plugin.ReleaseHub.xml`, et la frontière qui compte est celle entre un
administrateur et un utilisateur ordinaire :

- **L'API de ReleaseHub ne l'expose jamais.** `/ReleaseHub/Status` est accessible à tout utilisateur
  connecté et ne renvoie qu'un booléen `AnimeScheduleApiKeyConfigured`.
- **Aucun appel provider ne part du navigateur.** Toutes les requêtes vers AnimeSchedule sont émises
  par le serveur ; la clé ne transite jamais vers un client non-administrateur.
- **Elle n'est jamais journalisée.** Les messages d'erreur ne rapportent que le code HTTP.
- **La page de configuration l'affiche** à l'administrateur, avec un bouton afficher/masquer pour
  pouvoir la relire et la copier. Ce n'est pas un affaiblissement : `GET /Plugins/{id}/Configuration`,
  l'endpoint de Jellyfin que cette page appelle, est déjà réservé aux administrateurs et renvoie la
  configuration complète, clé comprise. La masquer dans le champ n'aurait rien caché.
- **Vider le champ et enregistrer supprime la clé** — c'est une action délibérée, pas un accident.
- Le bouton *Test connection* valide ce que contient le champ, donc une clé peut être vérifiée
  **avant** de remplacer celle qui fonctionne.
- Le fichier de configuration est listé dans `.gitignore`.

## Limites de débit

| Fournisseur | Limite annoncée | Ce que fait ReleaseHub |
|---|---|---|
| TVMaze | « au moins 20 requêtes / 10 s » | Se limite à **12 / 10 s** |
| AnimeSchedule | 120 requêtes / min (par IP **et** par application) | Se limite à **90 / min** |
| TMDb | Plus de plafond publié (remplacé par un lissage côté serveur) | Se limite à **20 / 10 s** |

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
| Aucun film | TMDb désactivé ou sans jeton |
| Un film de ma bibliothèque n'a pas de suite affichée | La saga n'a pas d'entrée non sortie chez TMDb, ou *Suivre les collections* est décoché |
| Pas d'entrée ReleaseHub dans le menu principal | File Transformation absent — ReleaseHub reste accessible depuis le tableau de bord |
| Heure absente sur un épisode | Le fournisseur n'en a pas donné. ReleaseHub n'invente pas d'heure |
| *Voir plus* n'affiche plus rien | Vous avez atteint l'horizon de synchronisation ; ReleaseHub le dit alors explicitement plutôt que de charger des pages vides |
| Deux carrousels superposés sur l'accueil | Le spotlight d'Abyss et le plugin Media Bar se montent au même endroit → [Interface web](#interface-web) |
| *Synchronize now* semble ignoré | Corrigé : une synchronisation demandée explicitement s'exécute toujours, même si les données sont récentes |

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
- Données films fournies par **[TMDb](https://www.themoviedb.org/)**. Leurs conditions imposent la
  mention ci-dessous ainsi que l'affichage de leur logo ou de leur nom là où les données sont montrées.

> Ce produit utilise l'API de TMDb mais n'est **ni approuvé ni certifié** par TMDb.
>
> ReleaseHub n'est **affilié à, ni approuvé par** Jellyfin, TVMaze, AnimeSchedule, TMDb, IMDb, TVDB,
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
├── Configuration/             Configuration persistée + pages admin
├── Integration/               Hook File Transformation (réflexion, optionnel)
├── Localization/              fr-FR.json, en-US.json
├── Models/                    Modèle normalisé, commun aux fournisseurs
├── Providers/                 IReleaseProvider + TvMaze/ + AnimeSchedule/ + Tmdb/
├── ScheduledTasks/            Synchronisation, vidage du cache
├── Services/                  Cache, résolution, limiteur de débit, orchestration
└── Web/                       app.html, app.js, boot.js, releasehub.css
```

Les assets web sont des ressources embarquées servies par le mécanisme natif de Jellyfin, sur
`GET /web/ConfigurationPage?name=...`. Aucune dépendance frontend, aucune étape de build JavaScript.

### Publier une version

La version n'est déclarée qu'à un seul endroit, `build.yaml`. Montez-la, décrivez la nouveauté dans le
même fichier, poussez sur `main` — c'est tout :

```yaml
version: "1.0.0.1"
changelog: |
  Ce que cette version apporte.
```

```bash
git commit -am "Release 1.0.0.1" && git push
```

Le workflow [`release.yml`](.github/workflows/release.yml) crée le tag `v1.0.0.1`, compile avec ce
numéro, lance les tests, produit l'archive, calcule son MD5, publie la release GitHub et ajoute
l'entrée dans `manifest.json`. Les notes de release s'ouvrent sur le `changelog` de `build.yaml` — le
même que lit le catalogue de plugins Jellyfin, donc les deux ne peuvent pas diverger — suivi du
changelog par commits généré par GitHub et d'un tableau de compatibilité.

Un commit qui ne monte pas la version ne publie rien : une version dont le tag existe déjà n'est jamais
republiée. Pousser un tag `v*` à la main, ou lancer le workflow depuis l'onglet *Actions*, reste
possible et publie sans condition.

Le numéro doit comporter **quatre parties** (`1.0.0.1`) : Jellyfin les compare comme des
`System.Version`, et le workflow refuse tout autre format plutôt que de laisser s'installer une version
qui se trierait ensuite n'importe comment.

## Licence

[GPL-3.0](LICENSE) — comme Jellyfin lui-même.
