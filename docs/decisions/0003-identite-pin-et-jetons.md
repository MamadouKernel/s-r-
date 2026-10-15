# 0003 — Identité : connexion par identifiant et PIN, jetons, gestion de l'équipe

**Statut :** accepté. Plusieurs paramètres dépendent de décisions métier encore ouvertes (OP-6) et sont marqués ci-dessous.

## Décision

- **Connexion** : `POST /api/v1/auth/login` avec `tenantId`, `identifier` et `pin`. L'identifiant (3 à 32 caractères :
  lettres, chiffres, point, tiret, souligné) est unique **dans un commerce** et insensible à la casse. Le PIN comporte
  4 à 6 chiffres. Le commerce est fourni par l'appareil : l'écran « choisir son nom » du cahier des charges suppose
  un appareil rattaché à un commerce, ce qui reste à construire (inscription T012, enrôlement d'appareil).
- **Réponses indiscernables** : un compte inconnu, désactivé, d'un autre commerce, un PIN mal formé ou faux donnent
  la même réponse (401 `invalid_credentials`), et un compte inconnu coûte autant de calcul qu'un compte réel. Seul un
  compte **verrouillé** reçoit une réponse distincte (423 `account_locked`).
- **Verrouillage** : 5 échecs consécutifs verrouillent le compte 15 minutes ; le compteur repart de zéro au verrouillage
  et à chaque connexion réussie ; un essai pendant le verrouillage ne le prolonge pas.
- **Stockage du PIN** : jamais en clair. HMAC-SHA256 avec un **poivre** secret du serveur (`Security:PinPepper`,
  Base64, 16 octets au minimum, hors base de données), puis PBKDF2-HMAC-SHA256 salé, 600 000 itérations, comparaison en
  temps constant. Le format stocké porte le nombre d'itérations, ce qui permet de le relever plus tard.
- **Jeton** : JWT HMAC-SHA256 portant `sub`, `tenant_id`, `role` et `jti`, valable **8 heures** (`Jwt:AccessTokenMinutes`).
- **Équipe** : le propriétaire crée et désactive tous les profils sauf un autre propriétaire ; un gérant crée et
  désactive caissiers et magasiniers ; les autres profils ne gèrent personne. Un utilisateur ne se supprime jamais, il se
  désactive (attribution des ventes passées, Constitution 5). La désactivation est tracée dans le journal d'audit.
- **Isolation** : la table `users` suit les mêmes règles que les tables de caisse (RLS par commerce, suppression
  interdite). À la connexion, le commerce n'est pas encore connu par un jeton : il est posé dans le contexte de la requête
  depuis le corps de la requête, après quoi la base n'expose que les utilisateurs de ce commerce. Pour les requêtes avec
  jeton, un intergiciel pose le commerce du jeton ; sans commerce posé, la base n'expose aucune ligne.
- **Limitation de débit** : 10 tentatives de connexion par minute et par adresse (`RateLimit:LoginPermitsPerMinute`),
  en plus du plafond global.

## Conséquences et limites connues

- **Durée de 8 heures et révocation** : un jeton déjà émis reste valable jusqu'à son expiration même si l'utilisateur est
  désactivé ensuite ; il ne peut simplement plus en obtenir un nouveau. Pas de jeton de rafraîchissement. À revoir avec
  la décision sur l'accès local hors ligne et la durée acceptable (OP-6).
- **Poivre** : sa rotation n'est pas gérée (il faudrait versionner le condensat). Le perdre invalide tous les PIN.
- **Premier propriétaire** : aucun point d'entrée public ne le crée ; il doit être provisionné à l'inscription du commerce
  (T012) ou par une procédure d'exploitation.
- **Propriétaire** : le cahier des charges prévoit mot de passe et code SMS pour lui ; seul le PIN est implémenté ici.
- **Compteur d'échecs** : mises à jour concurrentes possibles (perte d'un incrément), sans conséquence de sécurité réelle
  grâce à la limitation de débit par adresse.
- **Coût** : 600 000 itérations rendent chaque vérification de l'ordre de 0,1 à 0,3 s, ce qui ralentit la suite de tests
  d'intégration (une quarantaine de secondes).

## Réexamen

À la décision sur OP-6 (accès hors ligne), à l'ajout du mot de passe et du SMS du propriétaire, ou si le coût de
vérification pose problème sur le matériel d'hébergement.
