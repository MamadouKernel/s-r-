# 0019 — Connexion par PIN sans réseau

**Statut :** proposé le 26/10/2026, à valider avec le porteur du projet. Lève la question ouverte de `docs/decisions/0004` (PIN hors ligne et durée d'accès local) et complète `0003` (identité) et `0006` (appareils).

## Contexte

Dans la vie réelle, le réseau coupe : délestage, forfait épuisé, quartier mal couvert. Aujourd'hui la caisse sait **vendre** hors ligne, mais **personne ne peut se connecter** sans réseau : le caissier qui arrive le matin, ou celui qui prend la relève, reste devant l'écran de connexion alors que les clients attendent. Le jeton dure 8 heures ; au-delà, ou après un rechargement de la page, il faut du réseau pour rouvrir la caisse.

## Décisions

1. **Une connexion réussie en ligne laisse une preuve locale du PIN sur cet appareil**, pour ce compte seulement : le PIN tapé est transformé **sur l'appareil** (PBKDF2-SHA256, sel aléatoire, 600 000 tours, Web Crypto) ; seul ce condensat est gardé, **jamais le PIN**. Le serveur n'envoie rien de plus : il ne partage aucun condensat de PIN.
2. **Quand le serveur est injoignable** (pas de réponse), et seulement dans ce cas, la caisse propose « Se connecter sans réseau » et compare le PIN tapé au condensat local. Si le serveur répond (même « PIN incorrect »), on ne passe jamais au contrôle local : la réponse du serveur fait foi.
3. **Qui** : les profils qui encaissent (caissier, gérant). Le propriétaire n'a pas de PIN (mot de passe, ADR 0005) : il ne se connecte pas hors ligne, mais un gérant peut tenir la caisse.
4. **Combien de temps** : la preuve locale est valable **3 jours** après la dernière connexion en ligne de ce compte sur cet appareil. Passé ce délai, il faut du réseau une fois. Les forfaits et coupures de quelques heures sont couverts ; une boutique « oubliée » ne reste pas ouverte indéfiniment.
5. **Essais limités, même sans réseau** : 5 PIN faux de suite verrouillent la connexion hors ligne de ce compte **15 minutes** ; le compteur est gardé sur l'appareil (recharger la page ne le remet pas à zéro). Après 3 verrouillages de suite sans connexion en ligne réussie, le compte hors ligne est **bloqué jusqu'à une connexion en ligne**.
6. **Ce que permet la session hors ligne** : vendre, ouvrir et clôturer la caisse, comme aujourd'hui, avec le caissier identifié sur chaque opération. **Rien d'autre** : pas de gestion, pas de prix modifié, pas de remise ni d'annulation (elles exigent déjà le serveur, ADR 0018).
7. **Quand le réseau revient, la caisse demande à nouveau le PIN** du caissier (ou d'un gérant) pour obtenir un jeton et envoyer la file : un message clair le dit (« Le réseau est revenu : tapez votre code pour envoyer vos ventes »). Le serveur contrôle alors tout comme avant : compte actif, appareil non révoqué, session de caisse, prix, plafonds. Les ventes restent attribuées au caissier de la session, quel que soit celui qui les envoie.
8. **Effacement** : la preuve locale d'un compte disparaît à la **révocation de l'appareil**, quand le serveur répond que **le compte est désactivé ou que son PIN a changé** (connexion en ligne refusée, session périmée), et quand un gérant utilise « Oublier les comptes de cet appareil » (écran Appareils). « Se déconnecter » ne l'efface pas : c'est l'usage normal d'une relève.
9. **Journal** : chaque connexion sans réseau est notée sur l'appareil et envoyée au serveur avec la file (« Connexion sans réseau de Awa, de 08:02 à 14:40 »), visible du gérant.

## Risques et limites

- **Un PIN de 4 à 6 chiffres sur un appareil volé** : quelqu'un qui sait **extraire** le condensat du navigateur peut essayer les 10 000 à 1 000 000 combinaisons hors de l'appareil, sans limite d'essais. Le sel et les 600 000 tours ralentissent mais ne l'empêchent pas pour 4 chiffres. Ce que cela donne à l'attaquant est limité : vendre sur la caisse (pas de gestion, pas de remise, pas d'annulation), pendant 3 jours au plus, sur un appareil que le gérant peut révoquer dès qu'il le sait. C'est le prix accepté pour continuer à vendre sans réseau. Mesures : PIN de 6 chiffres conseillé, durée courte, révocation immédiate dès qu'un réseau est disponible.
- **Horloge de l'appareil** : le délai de 3 jours et le verrouillage de 15 minutes se mesurent avec l'heure de l'appareil, qu'un utilisateur peut changer. Sans conséquence pour le serveur (il ne croit pas cette heure pour les droits), mais cela affaiblit les limites ci-dessus.
- **Un compte désactivé pendant que l'appareil est hors ligne** peut encore vendre jusqu'à la prochaine connexion en ligne (au plus 3 jours) ; ses ventes sont ensuite contrôlées et envoyées par un gérant.
- Aucun jeton n'est créé sans réseau : le serveur ne délègue jamais son pouvoir de signer.

## Alternatives écartées

- **Jeton valable plus longtemps** (7 jours) : prolonge l'accès de **tous** les droits du compte, y compris la gestion, et ne résout pas le rechargement de page sans jeton.
- **PIN conservé en mémoire pour réutilisation automatique** : contraire à l'ADR 0004 (le PIN n'est jamais conservé).
- **Aucune connexion hors ligne, caisse laissée ouverte** : les relèves et les ouvertures de matin pendant une coupure restent impossibles.

## Réexamen

Après le pilote : durée de 3 jours, nombre d'essais, besoin d'un 2e facteur local pour les gérants.
