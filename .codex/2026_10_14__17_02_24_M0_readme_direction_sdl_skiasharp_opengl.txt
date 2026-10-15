Oryn — Documentation de la direction SDL

Date : 2026-10-14 17:02:24 CEST (+0200) — Europe/Paris.
Palier principal : M0. Cible future : v1.0, hors jalons v0.1.
Commit associé : aucun commit créé.

Demande : préciser dans le README le remplacement de Terminal.Gui par
SDL + SkiaSharp et la transition à SDL + OpenGL pour v1.0.
Résultat : README_ORYN.md aligné (identité, UX, architecture, roadmap,
critères et direction future). README.md annonce la nouvelle stack tout
en distinguant les commandes et capacités du prototype terminal actuel.
ADR 0005 consigne le choix utilisateur et remplace la décision frontend
de l’ADR 0001 ; anciens ADR et preuves historiques conservés.

Fichiers concernés : README_ORYN.md, README.md,
docs/adr/0005-sdl-rendering-direction.md et ce log.
Décisions : SDL fenêtre/événements + SkiaSharp dessin en v0.1 ;
SDL + OpenGL remplace la couche de dessin pour v1.0. État applicatif,
commandes et événements restent indépendants des bibliothèques graphiques.

Observations actuelles : code toujours Terminal.Gui 2.5.0 ; aucune
migration runtime réalisée. Version/bindings SDL, backend SkiaSharp et
packaging natif restent à vérifier ; profil OpenGL différé à v1.0.
AGENTS.md conserve des consignes terminal historiques : la nouvelle
instruction utilisateur et l’ADR explicitent leur remplacement pour le frontend.

Vérifications exécutées : recherche des références terminal/TUI dans les
README, relecture des sections modifiées, assertions de cohérence des
stacks/cible et distinction avec le csproj actuel ; git diff --check.
Validations historiques : non relancées ; aucun test runtime ni build
nécessaire pour cette tâche documentaire. Aucun commit/push.
Prochaine étape : tranche M0 SDL + SkiaSharp avec choix de bindings,
fenêtre, rendu et interactions vérifiés selon la demande utilisateur.
