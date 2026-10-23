# Spécification : PWA de caisse (premier parcours)

**Statut :** brouillon. Les écrans respectent `DESIGN.md` mais **les maquettes ne sont pas validées avec les caissiers du pilote** (T030
reste ouverte) : la structure et les états sont fixés ici, le détail visuel peut encore changer.

## Objectif

Un caissier ouvre sa caisse, compose une vente au scan ou à la recherche, l'encaisse en espèces, Mobile Money ou en paiement mixte, et
clôture, même sans réseau. Les REQ-01 à REQ-08 de `docs/specs/mvp-caisse/spec.md` sont couverts côté client.

## Périmètre de ce lot

Inclus : configuration de l'appareil, connexion PIN, ouverture, vente, paiement (espèces, Mobile Money, mixte), bandeau réseau,
journal local et synchronisation, file en attente et rejets, clôture avec écart.

Hors lot (V1 suivants) : remises et annulations, dépenses de caisse, tickets imprimés ou WhatsApp, retours,
tableau de bord propriétaire, comptage par billets et pièces (la clôture saisit un montant compté global).

## Parcours et écrans

| # | Écran | Rôle |
|---|---|---|
| S1 | Configuration de l'appareil | Saisir le code du commerce (une fois) |
| S2 | Connexion | Identifiant + PIN |
| S3 | Ouverture de caisse | Déclarer le fond de caisse |
| S4 | Vente | Catalogue (recherche, scan, touches rapides) + panier |
| S5 | Paiement | Espèces, Mobile Money, paiement mixte, récapitulatif |
| S6 | File de synchronisation | Ventes en attente, envoyées, rejetées |
| S7 | Clôture | Montant compté, écart attendu/déclaré |

### États à couvrir sur chaque écran
Chargement, vide, erreur, hors ligne, rejet de synchronisation. Le bandeau réseau (« En ligne », « Hors ligne »,
« N vente(s) en attente de synchronisation ») est visible partout, jamais une simple icône.

## Exigences

### PWA-01 Appareil et connexion
Au premier lancement, l'appareil enregistre le code du commerce, un identifiant de caisse et un identifiant de point de vente
(UUID générés localement). La connexion appelle `POST /api/v1/auth/login`; une erreur 401 ou 423 est expliquée en français
(identifiants invalides ; compte verrouillé, avec le moment du déverrouillage si connu).

### PWA-02 Session de caisse
L'ouverture appelle `POST /api/v1/register-sessions` avec un identifiant généré localement. Hors ligne, la session est
créée localement et envoyée au retour du réseau, **avant** les ventes qui en dépendent. Une seule session ouverte par caisse.

### PWA-03 Catalogue local
Le catalogue (`GET /api/v1/products`) est copié dans IndexedDB à chaque connexion et à chaque retour du réseau. La vente n'utilise
que cette copie. Les produits désactivés ou sans prix n'apparaissent pas. Le stock local baisse à chaque vente confirmée (SC-01).

### PWA-04 Panier
Ajout par scan (code-barres exact) ou par recherche de nom ; quantité modifiable explicitement ; retrait d'une ligne avant validation.
Prix et nom sont figés à l'ajout (RG-21). Total toujours visible. Un produit sans suivi de stock (prestation) n'a pas de stock affiché.

### PWA-05 Paiement
- Espèces : montant reçu saisi, monnaie à rendre calculée ; le montant reçu ne peut pas être inférieur au dû.
- Mobile Money : opérateur (Orange Money, MTN MoMo, Moov Money, Wave) et référence obligatoire, distincte du montant ; une référence déjà
  utilisée **sur cet appareil** est refusée avant l'envoi (le serveur reste l'arbitre global, SC-05).
- Mixte : plusieurs paiements dont la somme égale exactement le total net ; le crédit n'est pas proposé tant que les clients n'existent pas.
- Récapitulatif avant validation ; un double tap sur « Valider » ne crée qu'une vente.

### PWA-06 Journal durable (REQ-05)
La vente est écrite dans IndexedDB (journal + décrément du stock local + numéro de séquence de la caisse) **dans une seule transaction**
avant tout accusé visuel. L'écran de confirmation indique « Enregistrée sur l'appareil, en attente de synchronisation » tant que le
serveur n'a pas acquitté.

### PWA-07 Synchronisation (REQ-06)
- File ordonnée par séquence locale ; envoi par lots (200 au plus) vers `POST /api/v1/sales/sync`, session ouverte envoyée en premier.
- `Accepted` et `AlreadyProcessed` passent la vente à « synchronisée ». `Rejected` la garde visible avec le code et le message du serveur ;
  elle n'est jamais supprimée en silence et n'empêche pas l'envoi des suivantes.
- Échec réseau ou 5xx : nouvel essai avec délai progressif (2 s, 4 s… plafonné à 60 s) ; 409 `concurrent_conflict` : rejeu immédiat.
- Toute opération est idempotente (UUID d'origine) : rejouer ne crée pas de doublon.

### PWA-08 Clôture (REQ-08)
Saisie du montant compté, envoi de la clôture (`POST …/close`) après vidage de la file de la session ; affichage de l'attendu, du compté et
de l'écart avec un libellé explicite (manque / excédent / aucun écart). Hors ligne, la clôture est enregistrée localement puis envoyée.

### PWA-09 Accessibilité et responsive
Utilisable à 320 px sans défilement horizontal ; cibles de 48 px ; focus clavier visible ; états annoncés aux technologies d'assistance
(`aria-live`) ; couleur jamais seule porteuse de sens ; `prefers-reduced-motion` respecté.

### PWA-10 Panier durable et vente en attente
- Le panier en cours est enregistré dans IndexedDB à chaque modification : un rechargement de la page ou un redémarrage de l'appareil ne le perd pas.
- « Mettre en attente » range le panier courant (avec un libellé court facultatif, sinon l'heure) et vide l'écran pour servir le client suivant.
- Une vente en attente n'est **pas** une vente : aucune opération dans la file d'envoi, aucun décrément de stock, aucune séquence consommée.
- « Reprendre » n'est possible que lorsque le panier courant est vide (aucune fusion implicite). Les prix restent ceux figés à l'ajout (RG-21).
- « Supprimer » une vente en attente demande une confirmation explicite : c'est la seule suppression de l'écran de vente.
- À la clôture, les ventes encore en attente sont supprimées après un avertissement clair qui en donne le nombre.
- La base locale passe en version 2 sans perdre le journal, le catalogue, l'appareil ni le jeton déjà présents.

## Scénarios d'acceptation

- **SC-P1** Hors ligne, trois ventes sont confirmées ; après rechargement brutal de la page elles sont toujours dans la file, en attente.
- **SC-P2** Au retour du réseau, les trois ventes sont envoyées dans l'ordre ; un envoi répété ne crée aucun doublon côté serveur.
- **SC-P3** Une vente rejetée par le serveur reste visible avec son motif ; la suivante est tout de même synchronisée.
- **SC-P4** Un double tap sur « Valider » enregistre une seule vente.
- **SC-P5** Une référence Mobile Money déjà utilisée sur l'appareil est refusée avec un message clair.
- **SC-P6** Le stock local d'un produit baisse après la vente et reste correct après rechargement.
- **SC-P7** Deux onglets ouverts n'attribuent jamais le même numéro de séquence.
- **SC-P8** Un panier de trois articles survit à un rechargement de la page.
- **SC-P9** Une vente mise en attente puis reprise retrouve exactement ses lignes et ses prix, sans toucher au stock ni à la file d'envoi.
- **SC-P10** Une base locale de la version 1 s'ouvre en version 2 en gardant toutes ses données.

## Décisions provisoires et questions ouvertes

Voir `docs/decisions/0004-pwa-session-locale-et-jeton.md`. Restent ouverts : PIN hors ligne et durée d'accès local (le jeton expire côté serveur
après 8 h), enrôlement réel de l'appareil (aujourd'hui : code du commerce saisi), numérotation des tickets, validité d'un paiement Mobile Money déclaré hors
ligne, politique de conflit de stock.
