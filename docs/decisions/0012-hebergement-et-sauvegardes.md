# 0012 — Hébergement et sauvegardes

**Statut :** accepté le 25/10/2026 (choix « Europe + CDN » du porteur du projet) ; les points juridiques restent à vérifier.

## Contexte

L'architecture prévoit PostgreSQL avec sauvegardes mais ne fixe ni l'hébergeur, ni les objectifs de reprise (RPO/RTO, point ouvert OP-9). La caisse fonctionne hors ligne et garde son catalogue sur l'appareil : la latence vers le serveur compte peu pour la vente.

## Décisions

1. **Serveur en Europe (Paris ou Francfort)**, derrière un **CDN** qui sert l'application web (fichiers statiques et PWA). Pas d'hébergement à Abidjan : colocation coûteuse à exploiter, pour un gain de latence que la caisse hors ligne n'exige pas. Les chiffres de latence (100 à 180 ms) viennent de blogs d'hébergeurs et ne sont pas mesurés : on mesurera depuis Abidjan avant de s'engager.
2. **Un serveur pour l'API et une base PostgreSQL**, conteneurisés ; fournisseur à choisir sur devis (Scaleway, OVH, Hetzner) en comparant PostgreSQL géré et auto-hébergé. La décision du fournisseur se prend avec le prix réel ; le reste (images, configuration par variables d'environnement, migrations au démarrage contrôlé) ne dépend pas de lui.
3. **Objectifs : RPO 15 minutes, RTO 4 heures.** On ne perd pas plus de 15 minutes de données et on remet le service en moins de 4 heures.
4. **Sauvegardes** : archivage continu de la base (WAL) + sauvegarde complète chiffrée **chaque jour**, conservée **30 jours**, puis **une par mois pendant 12 mois**. Les copies sont stockées **chez un autre fournisseur** que le serveur.
5. **Restauration testée tous les mois, automatiquement** : une sauvegarde est restaurée dans une base jetable, vérifiée (nombre de lignes, contrôles d'intégrité, isolation), et le résultat est journalisé. Un échec envoie une alerte.
6. **Secrets hors du code** : mots de passe, clés JWT, clé de chiffrement des sauvegardes, accès SMTP dans le gestionnaire de secrets de l'hébergeur ou des variables d'environnement ; jamais dans git (GitGuardian en garde-fou, déjà actif).
7. **Supervision minimale** : un point de santé (`/health`), une alerte si l'API ou la base ne répond plus, un journal d'erreurs centralisé.
8. **Données personnelles** : hébergées hors de Côte d'Ivoire, elles relèvent de la loi ivoirienne sur la protection des données et de l'ARTCI. **À faire valider par un juriste ou l'ARTCI avant la mise en production** (déclaration, autorisation de transfert, mentions dans les conditions d'utilisation).

## Risques et limites

- Un seul serveur est un point de défaillance : la reprise se fait par restauration (RTO 4 h), pas par basculement automatique. Un second serveur se justifiera avec la taille de la clientèle.
- Le CDN ne protège pas l'API : limitation de débit et authentification restent côté serveur (déjà en place).
- Le coût des sauvegardes croît avec les données ; la rétention est un paramètre.
