# 0008 — Clients, plafond de crédit et versements

**Statut :** accepté le 24/10/2026 (quatre choix du porteur du projet, tous alignés sur la recommandation).

## Contexte

Le mode de paiement « Crédit » existe depuis le début mais sans fiche client ni suivi de la dette : la vente exige un identifiant de client qui ne correspond à rien.

## Décisions

1. **Fiche client minimale** : nom, téléphone facultatif, plafond ; la dette est calculée par le serveur. Le téléphone sert à retrouver un client et, plus tard, à le joindre (canal de relance à choisir : ni SMS ni WhatsApp).
2. **Plafond par client**, valeur par défaut du commerce. Dépassement : accord d'un gérant (RG-06), comme pour les remises, avec le même mécanisme de validateur.
3. **Versements partiels** en espèces ou Mobile Money, tracés, jamais supérieurs à la dette, rejouables ; **en ligne seulement**.
4. **Hors ligne** : la caisse vend à crédit dans le plafond connu à son dernier chargement. À la synchronisation, le serveur **enregistre toujours** la vente (elle a eu lieu : la rejeter ne rendrait pas la marchandise) et **signale** un dépassement dans le journal.
5. **Dette = valeur stockée mise à jour dans la transaction de la vente**, avec un jeton de concurrence : deux ventes simultanées pour un même client ne se perdent pas (la seconde est rejouée). Invariant testé : dette = Σ crédits des ventes validées − Σ versements.
6. **Annuler une vente à crédit annule la dette correspondante** (le reste de l'annulation dépend de T017).
7. **Un client ne se supprime pas** ; il se désactive, et seulement sans dette.
8. **Un client se crée à la caisse** : le caissier n'a pas à interrompre la vente pour appeler le gérant, mais ne choisit pas le plafond.

## Risques et limites

- Un caissier peut créer des doublons (même nom) : seul le téléphone est unique.
- En ligne, le plafond protège ; hors ligne il ne protège que contre la dette connue : plusieurs caisses peuvent dépasser ensemble. Le signalement dans le journal est la compensation.
- La dette n'a ni échéance ni intérêt.

## Réexamen

À la validation avec le pilote : le plafond par défaut (20 000 FCFA provisoire), l'accès des caissiers aux dettes, le besoin d'un canal de relance.
