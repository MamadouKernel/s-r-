# Plan technique : Caisse MVP hors ligne

## Portée

Implémenter le premier parcours vertical : session caisse, lecture du catalogue, panier, paiement espèces/Mobile Money déclaré, persistance locale, synchronisation idempotente, clôture et consultation distante minimale.

## Backend

1. Domain : Sale, SaleLine, Payment, CashRegisterSession et invariants de transition; valeurs monétaires entières en FCFA.
2. Application : OpenRegisterSession, RecordSale, SyncSales, CloseRegisterSession; ports repository, clock, current tenant/user.
3. Infrastructure : EF Core/Npgsql, contraintes d'unicité pour UUID de vente et référence Mobile Money par périmètre métier validé, transactions atomiques vente/paiement/stock/audit.
4. API : endpoints versionnés, authentification/autorisation, validation de contrats et erreurs de synchronisation structurées.
5. PostgreSQL : tenant_id partout, RLS, contraintes et index testés avec comptes réels de test.

## PWA

1. Parcours tactile optimisé : ouverture, vente rapide, panier, paiement, reçu et clôture.
2. IndexedDB pour snapshot catalogue et journal durable des commandes.
3. Identifiants UUID côté client, outbox ordonnée par caisse, reprise réseau avec délai progressif et retour d'état lisible.
4. Service worker limité aux ressources de l'application; données privées jamais mises en cache HTTP partagé.
5. Gestion des appareils et effacement local à cadrer avant usage réel sur appareil partagé.

## Observabilité et exploitation

- Corréler chaque commande par tenant/caisse/opération sans journaliser PIN, données de paiement sensibles ou secrets.
- Mesurer le retard de synchronisation, les rejets et les conflits.
- Sauvegarde/restauration et procédure de remplacement de caisse décrites avant pilote.

## Dépendances et risques

- Confirmation du format des tickets et du matériel d'impression.
- Arbitrage de numérotation sans trou avec ventes hors ligne.
- Règles de stock multi-caisses et de prix modifié hors ligne.
- Validation métier des paiements déclaratifs hors ligne.
- Validation sécurité des politiques RLS et du modèle de session offline.

## Séquence de livraison

1. Modèle de domaine et invariants de caisse.
2. API transactionnelle et isolation tenant.
3. UI de caisse connectée.
4. Persistance offline et synchronisation rejouable.
5. Clôture et tableau de bord distant minimal.
6. Validation sur matériel du pilote et ajustements UX.
