# Spécification — Connexion par PIN sans réseau

**Statut :** brouillon à valider ; décisions de `docs/decisions/0019-connexion-hors-ligne.md`.
**Lots :** (1) preuve locale du PIN, connexion sans réseau, verrouillage ; (2) reprise de la file au retour du réseau, effacement, journal et écran Appareils.

## Objectif

Qu'un caissier **puisse ouvrir la caisse et vendre quand le réseau est coupé**, y compris après un rechargement de la page ou à la relève, sans affaiblir ce que le serveur contrôle.

## Exigences

### HLC-01 Preuve locale à la connexion en ligne
Après une connexion par PIN réussie, l'appareil calcule sur lui-même un condensat du PIN (PBKDF2-SHA256, 600 000 tours, sel aléatoire de 16 octets, Web Crypto) et le range dans IndexedDB avec : identifiant du compte, nom, rôle, date de la connexion en ligne, compteurs d'essais. **Le PIN n'est jamais gardé**, ni en mémoire au-delà de l'appel, ni écrit nulle part, ni journalisé. Un compte par entrée ; une nouvelle connexion en ligne remplace l'entrée (nouveau sel).

### HLC-02 Quand la connexion sans réseau est proposée
Seulement si l'appel de connexion échoue **faute de réponse** du serveur (réseau coupé, serveur injoignable, délai dépassé). Toute réponse du serveur (401, 423, 403, 429…) est affichée telle quelle, sans bascule. L'écran dit en clair : « Pas de réseau. Vous pouvez ouvrir la caisse sans réseau avec votre code. »

### HLC-03 Contrôle local
Le PIN tapé est transformé avec le sel du compte et comparé en temps constant au condensat. L'identifiant tapé est comparé sans tenir compte de la casse. Réponse **identique** pour « compte inconnu sur cet appareil » et « PIN faux » : « Identifiant ou code incorrect. » Un compte jamais connecté en ligne sur cet appareil ne peut pas entrer hors ligne : « Ce compte doit d'abord se connecter une fois avec le réseau. »

### HLC-04 Profils concernés
Caissier et gérant seulement. Le propriétaire, le magasinier et le comptable n'ont pas de connexion hors ligne (le message dit « Cette connexion exige le réseau »). Aucune preuve locale n'est créée pour ces profils.

### HLC-05 Durée de validité
La preuve est valable 3 jours (72 h) après la dernière connexion en ligne du compte sur l'appareil. Passé ce délai : « Il faut du réseau pour vous reconnecter. » (la preuve est effacée).

### HLC-06 Essais limités
5 PIN faux de suite sur un compte : verrouillage de 15 minutes, avec le temps restant affiché (« Réessayez dans 12 minutes »). Compteurs gardés dans IndexedDB : un rechargement ne les remet pas à zéro. Une connexion réussie (en ligne ou non) remet le compteur d'essais à zéro. 3 verrouillages consécutifs sans connexion en ligne réussie : le compte est bloqué hors ligne jusqu'à une connexion en ligne (« Ce compte demande le réseau pour se reconnecter. »).

### HLC-07 La session hors ligne
La session locale porte l'identifiant, le nom et le rôle du compte, **sans jeton**. Elle permet : ouvrir une session de caisse, vendre, clôturer (comme aujourd'hui, journal local). Elle interdit tout écran de gestion, la modification d'un prix, la remise et l'annulation (déjà refusées sans serveur). Le bandeau « Sans réseau — ventes gardées sur cet appareil » reste affiché. La session locale se termine à « Se déconnecter » ; un rechargement la retrouve (même délai de 3 jours).

### HLC-08 Retour du réseau
Quand le réseau revient et qu'il n'y a pas de jeton valable, la caisse n'efface rien et affiche : « Le réseau est revenu. Tapez votre code pour envoyer vos ventes. » Le PIN est vérifié **en ligne** (connexion normale). Si la personne qui tape n'est pas le caissier titulaire de la session, la file part uniquement si c'est un gérant (règle serveur existante). Les ventes sont envoyées dans l'ordre, session d'abord, comme aujourd'hui ; chaque vente reste attribuée au caissier de sa session.

### HLC-09 Effacement de la preuve locale
La preuve d'un compte est effacée : (a) quand le serveur refuse l'appareil (révoqué, `device_not_authorized`) : **toutes** les preuves ; (b) quand une connexion en ligne de ce compte est refusée parce que le compte est désactivé ou que son PIN a changé (401 `invalid_credentials` après avoir eu une preuve) ou que sa session est périmée (`session_outdated`) ; (c) sur « Oublier les comptes de cet appareil » (gérant, écran Appareils, avec confirmation). « Se déconnecter » ne l'efface pas.

### HLC-10 Journal
Chaque connexion sans réseau et chaque fin de session hors ligne sont notées dans le journal local et envoyées avec la file au retour du réseau ; le serveur les inscrit au journal d'audit (`login.offline`, compte, appareil, heures de début et de fin). Visible du propriétaire et du gérant, rubrique « Sécurité ». Une heure d'appareil manifestement fausse (futur de plus de 24 h) est signalée dans le détail.

### HLC-11 Écrans et vocabulaire
Aucun mot technique à l'écran : « sans réseau », « code », jamais « hors ligne », « hash », « jeton ». Chaque refus dit ce qu'il faut faire ensuite. Cibles tactiles de 48 px minimum, contraste AA. États : connexion en cours, pas de réseau, réseau revenu, verrouillé (avec minutes), bloqué.

### HLC-12 Non-régression et sécurité
- Une connexion en ligne qui répond 401 ne tente **jamais** la voie locale, même si une preuve existe.
- Aucune requête n'est envoyée au serveur avec un PIN pour la voie locale.
- Un PIN faux hors ligne ne modifie rien côté serveur (il ne peut pas verrouiller un compte réel).
- La preuve locale d'un compte n'est jamais lisible par un autre compte via l'interface (aucun écran n'affiche d'identifiants ou de condensats).

## Scénarios

1. **Coupure le matin.** Awa s'est connectée hier avec le réseau. Ce matin, plus de réseau : elle tape son code, la caisse s'ouvre avec le bandeau « sans réseau », elle vend. À 14 h le réseau revient : « Tapez votre code pour envoyer vos ventes » ; elle le tape, la file part, le bandeau disparaît.
2. **Relève pendant une coupure.** Kofi, qui s'était connecté sur ce téléphone il y a 2 jours, prend la relève : il entre. Mariam, jamais venue sur ce téléphone, voit « Ce compte doit d'abord se connecter une fois avec le réseau ».
3. **Code faux.** Après 5 codes faux : « Réessayez dans 15 minutes. » Recharger la page garde le verrou.
4. **Quatre jours sans réseau.** « Il faut du réseau pour vous reconnecter. »
5. **Serveur qui répond.** Le réseau marche mais le code est faux : « Identifiant ou code PIN incorrect », sans bascule locale.
6. **Appareil retiré par le gérant.** Au prochain contact avec le serveur : toutes les preuves sont effacées ; sans réseau il reste utilisable jusqu'aux 3 jours.
7. **Compte désactivé.** Le gérant désactive Kofi ; le téléphone de Kofi est sans réseau. Il peut encore vendre. À son retour en ligne, son code est refusé : sa preuve est effacée ; un gérant envoie la file restante.

## Hors lot

PIN de secours donné par le gérant, connexion du propriétaire sans réseau, deuxième facteur local, changement de PIN depuis la caisse, lecture de l'heure du serveur pour contrôler l'horloge de l'appareil.

## Plan d'essai (avant le code)

- **Unitaires (front)** : condensat et comparaison, verrouillage et compteurs, délai de 3 jours (horloge simulée), refus identique pour compte inconnu et PIN faux, aucune écriture du PIN (stockage inspecté), pas de bascule sur une réponse 401.
- **Composants** : écran de connexion (3 états), bandeau, invite « réseau revenu », écran Appareils « Oublier les comptes ».
- **Serveur** : réception et inscription de `login.offline` avec la file ; refus de la file pour appareil révoqué ; attribution au caissier de la session quel que soit l'expéditeur.
- **Navigateur** : coupure réelle (réseau simulé coupé), rechargement de page, retour du réseau, sur une pile isolée.
