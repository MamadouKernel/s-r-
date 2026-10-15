# Spécification : Caisse MVP hors ligne

**Statut :** brouillon initial dérivé du cahier des charges; à valider avec le commerce pilote.

## Objectif

Permettre à un caissier autorisé d'ouvrir sa session, composer et encaisser une vente en espèces ou Mobile Money, même pendant une coupure réseau, sans perte ni doublon après synchronisation.

## Acteurs

- Caissier : ouvre une session, vend, encaisse et clôture sa session.
- Gérant : autorise les remises et annulations selon les règles configurées.
- Propriétaire : consulte les ventes synchronisées et les écarts.

## Exigences

### REQ-01 Ouverture de caisse
Le caissier authentifié ouvre une caisse en déclarant le fond de caisse. La vente est refusée si aucune session valide n'est ouverte.

### REQ-02 Recherche et panier
Le caissier peut scanner un code-barres ou rechercher un produit par nom, ajouter des quantités et retirer une ligne avant validation. Les prix utilisés sont ceux disponibles localement au moment de la vente.

### REQ-03 Encaissement espèces
Le caissier confirme le montant reçu; le système calcule la monnaie à rendre et conserve le détail de la vente.

### REQ-04 Encaissement Mobile Money
Le caissier choisit Orange Money, MTN MoMo, Moov Money ou Wave et saisit une référence. Une référence déjà utilisée dans les données accessibles à la caisse est refusée. Le contrôle global après resynchronisation doit également empêcher les doublons.

### REQ-05 Vente hors ligne
La vente confirmée est durablement enregistrée sur l'appareil avant confirmation visuelle au caissier. L'interface indique clairement l'état local/en attente de synchronisation.

### REQ-06 Synchronisation idempotente
Au retour du réseau, les ventes sont envoyées avec leur UUID d'origine. Répéter un envoi ne crée pas de deuxième vente. L'ordre de la file est conservé par caisse; les erreurs visibles ne sont pas abandonnées silencieusement.

### REQ-07 Attribution et audit
Chaque vente conserve le tenant, le point de vente, la caisse, la session et l'utilisateur. Une vente validée n'est jamais supprimée.

### REQ-08 Clôture
Le caissier clôture sa session avec le comptage demandé. Le système compare le montant attendu au montant déclaré, conserve l'écart et le rend visible au gérant/propriétaire après synchronisation.

## Scénarios d'acceptation

### SC-01 Vente simple en ligne
Étant donné une caisse ouverte et trois produits disponibles, quand le caissier encaisse en espèces, alors la vente reçoit un identifiant, le total et la monnaie sont corrects, le stock local diminue et le serveur reçoit une seule vente.

### SC-02 Vente pendant une coupure réseau
Étant donné une caisse et un catalogue déjà synchronisés, quand Internet est coupé et qu'une vente est confirmée, alors la vente reste consultable après redémarrage brutal et son état indique qu'elle attend la synchronisation.

### SC-03 Reprise après retour du réseau
Étant donné plusieurs ventes en attente, quand la connexion revient et que des envois sont répétés, alors chaque vente apparaît une seule fois côté serveur et les accusés de réception mettent à jour la file locale.

### SC-04 Rôle caissier
Étant donné un caissier sans autorisation gérant, quand il tente une remise nécessitant validation ou l'annulation d'une vente validée, alors l'opération est refusée et tracée.

### SC-05 Référence Mobile Money réutilisée
Étant donné une référence déjà enregistrée, quand une nouvelle vente tente de l'utiliser, alors la caisse ou la synchronisation serveur signale le doublon sans modifier silencieusement la vente.

### SC-06 Isolation des commerces
Étant donné un utilisateur d'un tenant, quand il demande une vente d'un autre tenant, alors aucun détail n'est retourné et aucune modification n'est possible.

## Questions métier ouvertes

- Les paiements Mobile Money saisis hors ligne représentent-ils une confirmation client ou une simple déclaration du caissier ?
- Comment traiter une vente hors ligne si le stock est devenu négatif sur une autre caisse ?
- Quel niveau de vérification du PIN est requis sans réseau et quelle durée d'accès local est acceptable ?
- Les taxes, mentions du ticket et règles de facturation sont à confirmer pour le pilote.
- Les exigences de matériel d'impression et le choix d'agent local restent à valider sur place.
