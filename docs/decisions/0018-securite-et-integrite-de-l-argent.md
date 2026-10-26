# 0018 — Sécurité et intégrité de l'argent : correctifs issus de l'audit du 25/10/2026

**Statut :** accepté (le porteur a demandé de corriger en priorité les failles de l'audit). Complète `0005`, `0008`, `0011` et `0014`.

## Contexte

Un audit (relecture du code, test de pénétration par lecture, analyse du terrain) a trouvé, sans fuite entre commerces, des failles de **fraude interne** et d'**intégrité de l'argent**. Cette décision règle celles que le code confirme.

## Décisions

1. **Prix inférieur au catalogue = signalé, jamais bloqué hors d'un cas.** Une vente ne se refuse pas parce qu'un prix a changé entre-temps (le prix du catalogue a pu bouger après la vente hors ligne). Mais un prix de ligne **inférieur** au prix du catalogue est une perte possible : (a) vente **envoyée en ligne** par un profil qui n'est ni propriétaire ni gérant : **refusée** (« Seul un gérant peut vendre à un prix inférieur au catalogue ») ; (b) vente **de la file hors ligne** : acceptée (elle a eu lieu) et **inscrite au journal** (« Vente à prix réduit : Jus d'orange 1L à 100 FCFA au lieu de 600 FCFA ») pour que le propriétaire la voie. Un prix supérieur n'est pas une perte et n'est pas signalé.
2. **Une approbation de gérant se prouve par son PIN.** Le champ « identifiant du gérant » ne prouve rien (un caissier peut le connaître). L'approbation d'une remise ou d'un dépassement de crédit exige l'identifiant **et le PIN** du gérant, vérifié par le serveur avec le même verrouillage que la connexion (5 essais, 15 minutes). Le propriétaire n'a pas de PIN : il approuve en vendant lui-même sur la caisse. Un gérant qui vend lui-même n'a rien à prouver de plus.
3. **La lecture seule de l'abonnement ne dépend pas de la casse de l'adresse.** `POST /API/v1/products` est refusé comme `/api/v1/products`.
4. **Changer son mot de passe met fin aux sessions ouvertes** du propriétaire (même mécanisme que le changement de PIN, ADR 0014) : un jeton volé ne survit pas à la réinitialisation.
5. **La session de caisse est protégée contre l'écriture simultanée** (jeton de version `xmin`, comme les produits et les clients) : une vente et la clôture au même instant ne produisent plus de faux écart ; la seconde requête est rejouée (409).
6. **Une vente rejetée ne laisse aucune trace d'argent** : le crédit d'une vente à crédit s'ajoute à la dette du client **après** que la vente a été acceptée par la session, pas avant ; une vente refusée ne peut donc plus faire monter une dette dans le commit d'une vente suivante du même lot.
7. **Une référence Mobile Money est unique entre ventes et versements** (CRD-05) : une vente refuse une référence déjà utilisée par un versement.

## Hors lot (suivis à part)

Lecture seule qui laisse passer toute la file hors ligne ; lot de synchronisation bloqué par une erreur d'unicité ; validation simultanée de deux paiements d'abonnement ; rapport de pente de crédit ; en-têtes de sécurité et proxy de confiance ; import Excel en flux.
