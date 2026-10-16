Palier principal : M1. Fuseau : Europe/Paris (CEST).

Demande : inscrire HIE dans le README comme proposition de recherche immédiatement après l’intégration multi-model.

Résultat : README_ORYN.md enrichi sous M1 avec une étape explicite « Multi-model integration [PROPOSED] », suivie immédiatement de « Oryn Hybrid Intelligence Engine (HIE) [PROPOSED] [LATER] ». La proposition couvre vision, Problem IR révisable et exemple, moteurs déterministes/neuronaux/agents logiques, séparation router/policy/planner/scheduler, baselines A/B/C, critères de comparaison, métriques, contrats d’exécution, progression et spécialisation apprise facultative/versionnée. Ajout d’un renvoi dans les choix à vérifier expérimentalement.

Décisions documentaires : préserver le multi-model et le chat fonctionnel ; conserver HIE comme recherche sans gain acquis, sans nouvelle obligation v0.1 et sans bloquer M2–M7. Réutiliser le modèle de connaissance commun. Distinguer validité de schéma, tests et satisfaction de l’intention ; ne pas transformer une hypothèse IR en décision utilisateur. Préserver la comparaison B/C avec ressources, outils et budgets comparables.

Fichiers concernés : README_ORYN.md et ce log uniquement dans cette tâche. Changements préexistants conservés. Aucune implémentation, dépendance ou réorganisation de code.

Vérifications exécutées : relecture du diff, contrôle de l’ordre des étapes et des marqueurs [PROPOSED]/[LATER], git diff --check réussi. Aucun build/test/benchmark exécuté : changement documentaire.

Limites : le document utilisateur se terminait au début de sa Phase 2 ; les étapes de recherche suivantes synthétisent la discussion précédente et restent explicitement proposées. Aucun bénéfice HIE mesuré. Les validations historiques des modèles ne deviennent pas des validations HIE.

Prochaine étape : poursuivre la tranche multi-model/observabilité si une implémentation est demandée ; le présent ajout ne l’implémente pas.

Commit associé : aucun. Aucun commit ni push effectué.
