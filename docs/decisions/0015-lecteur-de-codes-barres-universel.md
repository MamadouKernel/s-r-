# 0015 — Lecteur de codes-barres : l'application s'adapte au lecteur

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet. Complète `docs/specs/formats-produits` (code-barres exact trouvé à la caisse).

## Contexte

Le commerçant va acheter un lecteur de codes-barres (USB, Bluetooth ou 2,4 GHz, 1D ou 2D, marque inconnue à ce jour). Presque tous se comportent comme un **clavier** : ils « tapent » le code puis, selon le réglage, Entrée, Tab ou rien. Aujourd'hui la caisse n'ajoute un produit que si le champ de recherche a le focus **et** que le lecteur envoie Entrée. Un lecteur réglé autrement (pas d'Entrée, préfixe, clavier français décalé) paraît « ne rien faire ».

Le réglage d'un lecteur passe par des codes imprimés dans sa notice, propres à chaque marque : on ne peut ni les connaître ni les garantir. Le commerçant n'est pas technicien.

## Décisions

1. **L'application s'adapte, pas le lecteur** : aucun réglage n'est exigé du lecteur. Un lecteur en mode clavier, quel que soit son réglage courant, doit fonctionner.
2. **Un scan se reconnaît à sa vitesse** : une rafale de caractères séparés de moins de 40 ms, d'au moins 6 caractères, est un scan ; une saisie à la main n'en est jamais un. Fin de scan : Entrée, Tab, ou 100 ms de silence après la rafale.
3. **Le scan est capté même si la zone de recherche n'a pas le focus**, à la caisse. Les autres zones de saisie ne sont jamais volées (on ne détourne pas ce qu'on tape dans un champ).
4. **Clavier décalé corrigé par la touche physique** : pendant un scan, les chiffres sont lus à partir de la touche physique (`event.code`) et non du caractère produit, ce qui supprime le décalage d'un lecteur « américain » sur un clavier français (les chiffres sortent sinon en `&é"'(`).
5. **Préfixes et suffixes parasites ignorés** (caractères de contrôle, espaces).
6. **Retour clair à la caisse** : produit ajouté = court bip aigu + message « {produit} ajouté » ; code inconnu = bip grave + message avec le code, rien n'est ajouté. Le son peut être coupé.
7. **Un écran « Lecteur de codes-barres » (Gestion)** pour tester n'importe quel lecteur et dire, en français, quoi corriger. C'est ce qui remplace la connaissance de la notice du lecteur.
8. **Rien côté serveur** : tout se passe sur l'appareil, hors ligne comme en ligne, avec la copie locale du catalogue.
9. **QR codes** : lus comme du texte, comme tout code. Les produits n'ont pas de QR ; un texte qui n'est pas un code produit est « inconnu ».
10. **Hors lot** : caméra du téléphone, impression d'étiquettes, saisie d'un code par QR de configuration.

## Risques et limites

- Un lecteur qui n'est pas en mode clavier (mode « série », « USB COM ») n'est pas lisible par un navigateur : l'écran de test le constate (rien n'arrive) et le dit.
- La correction du clavier suppose un lecteur qui émet des touches « américaines » (le cas général) ; si le lecteur émet des touches françaises, l'écran de test le montre et la correction est désactivée pour ce cas.
- La détection par vitesse est une heuristique : un lecteur très lent (> 40 ms par caractère, rare) n'est pas reconnu ; l'écran de test mesure la vitesse et prévient.
