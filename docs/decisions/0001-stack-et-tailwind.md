# 0001 — Pile technique et Tailwind CSS

**Statut :** accepté (demande du porteur du projet).

## Décision

- Frontend : Angular (PWA, IndexedDB), style avec **Tailwind CSS**.
- Backend : .NET 10 (ASP.NET Core).
- Base de données : PostgreSQL.
- Bootstrap n'est pas utilisé.

## Contexte

Le cahier des charges fixait déjà Angular 22, .NET 10 LTS et PostgreSQL 18 mais ne disait rien du CSS. `DESIGN.md` impose une identité propre (vert feuille, ivoire, cibles de 48 px, pas de tableaux de cartes uniformes) qu'une bibliothèque de composants préstylés rendrait difficile à tenir.

## Conséquences

- Les jetons de `DESIGN.md` (couleurs, typographie, tailles tactiles) sont définis une fois dans le thème Tailwind; les composants Angular les utilisent seuls.
- Aucun composant visuel n'est fourni : boutons, champs, panier et bandeau réseau sont à construire, ce qui coûte du temps mais garde le contrôle sur l'ergonomie de caisse.
- L'accessibilité (focus, contraste, états) est de notre responsabilité et testée écran par écran.
- Versions exactes d'Angular et de Tailwind à confirmer à la création du projet `src/frontend/` (tâche T033).

## Réexamen

À la fin du pilote, ou si la construction des composants ralentit nettement la livraison.
