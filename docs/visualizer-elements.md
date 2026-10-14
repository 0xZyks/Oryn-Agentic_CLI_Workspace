# Éléments du visualiseur — proposition pour la template M0

Cette proposition sert à préparer une maquette avec des données fictives.
Elle ne définit pas un analyseur ni un schéma définitif et ne modifie pas les
choix du README_ORYN. Le prototype rend désormais un petit sous-ensemble
fictif : point d'entrée, classes, contrat et composant conceptuel.

## Familles de nœuds

Les familles donnent une représentation commune ; les variantes préservent la
nature de l'élément et ses métadonnées propres au langage. Une classe, une struct
et une interface peuvent partager certains composants visuels sans devenir
interchangeables dans le modèle.

| Famille | Éléments et variantes à pouvoir représenter | Rôle |
|---|---|---|
| Dépôt | Dépôt, sous-dépôt | Ensemble versionné ; peut contenir plusieurs projets. |
| Projet | Application, bibliothèque, outil, projet de tests | Unité de développement ; son organisation dépend de l'écosystème. |
| Conteneur physique | Dossier, fichier | Organisation réelle des sources et ressources. |
| Conteneur logique | Package, module, namespace | Organisation du code ; garder ces sous-types distincts. |
| Type | Classe, struct, record, enum, union, type opaque | Déclaration d'un type ; conserver sa forme réelle. |
| Contrat de type | Interface, trait, protocole | Capacités attendues ou contrats, selon les règles du langage. |
| Alias de type | Alias, typedef, type nommé redirigeant vers un autre | Référence à un type ; pas une nouvelle classe par défaut. |
| Élément exécutable | Fonction, méthode, constructeur, destructeur, opérateur, lambda, closure | Unité de comportement ; conserver portée et signature. |
| Donnée déclarée | Champ, propriété, variable, constante, membre d'enum | État ou valeur accessible dans une portée donnée. |
| Entrée de signature | Paramètre, paramètre de type/générique | Détail d'une déclaration ; afficher dans la fiche avant d'en faire un nœud autonome. |
| Événement | Événement, signal, canal/topic déclaré | Point d'émission/réception ; le handler est un élément exécutable distinct. |
| Dépendance externe | Bibliothèque, package externe, service/API externe | Élément fourni hors du projet ; import et utilisation deviennent des relations. |
| Ressource | Configuration, schéma, migration, asset, document, script de build | Artefact du projet qui n'est pas nécessairement une déclaration de code. Un fichier et ce qu'il représente peuvent être reliés. |
| Unité d'exécution/build | Exécutable, cible de build, service/processus, tâche/job | Vue des unités construites ou exécutées ; distinguer définition et instance runtime si un jour nécessaire. |
| Élément conceptuel | Composant d'architecture, sous-système, fonctionnalité, technologie, hypothèse | Intention ou organisation pensée par l'utilisateur, avec ou sans code associé. |

Un test, un point d'entrée, un callback ou un handler peuvent être des **rôles**
d'une fonction/méthode existante. Une suite de tests ou fixture peut être un
conteneur ou un type portant un rôle de test. Éviter de dupliquer le même symbole
pour chacune de ses utilisations. Les types non pris en charge gardent une
catégorie de symbole générique et leur nature d'origine plutôt qu'une conversion
inventée vers un type connu.

Les détails locaux (variables, paramètres, lambdas, génériques) apparaissent
selon la profondeur choisie. Ils ne doivent pas encombrer la vue d'architecture.

## Relations, distinctes des nœuds

Un appel est d'abord une relation entre deux éléments exécutables. Dans une vue
de flot, son **site d'appel** peut aussi être un élément : deux appels à la même
fonction dans un corps restent deux occurrences différentes.

| Relation | Exemple |
|---|---|
| Contient | Dossier → fichier ; namespace → type ; type → méthode. Préciser la nature physique/logique/lexicale de l'appartenance. |
| Référence / utilise | Fonction → type ou symbole utilisé. |
| Appelle | Fonction → fonction/méthode cible. |
| Dépend de / importe | Module → module ou dépendance externe. |
| Hérite / étend | Type → type parent, lorsque le langage le permet. |
| Implémente | Type → contrat. |
| Redéfinit | Méthode → méthode redéfinie. |
| Instancie | Fonction/site de construction → type construit. |
| Lit / écrit | Fonction → champ, propriété ou variable. |
| Émet / s'abonne / traite | Fonction → événement ; handler → événement traité. |
| Transporte des données | Sortie/composant → entrée/composant, avec justification. |
| Transition de contrôle | Bloc/instruction → prochain bloc/instruction, avec condition éventuelle. |
| Réalise une intention | Code → composant ou fonctionnalité conceptuelle. |
| Documente / justifie / discute | Connaissance, décision ou conversation → élément concerné. |
| Remplace / lié à | Proposition ou élément → élément antérieur/associé. |

La flèche de contrôle, l'appel et la dépendance ne représentent pas le même
phénomène. Leur apparence et leur filtrage seront discutés séparément. Dans un
vrai projet, une relation non vérifiable reste partielle, inconnue ou absente ;
la template n'est pas une preuve de capacité d'analyse.

## Éléments du flot interne — vue séparée

À prévoir pour les maquettes de détail : entrée, sortie, bloc d'instructions,
site d'appel, branche/condition, retour, levée/gestion d'erreur et suspension/
reprise asynchrone. Une boucle peut être un regroupement visuel de blocs et de
transitions ; sa représentation exacte n'est pas figée ici.

Les instructions ne sont pas nécessairement toutes des nœuds du graphe global.
La vue CFG et la vue d'appels restent distinctes. Pour de vraies sources, ces
faits doivent être fournis par un analyseur qui annonce cette capacité. La
template peut simuler ces cas mais doit l'indiquer.

## Connaissances rattachées

Le modèle unifié doit aussi pouvoir porter fiches de connaissance, ressources
externes/documentation, conversations, messages, décisions, propositions et
idées archivées. Ils peuvent apparaître dans une fiche, un badge ou une vue
dédiée ; les afficher tous comme des boîtes d'architecture n'est pas requis.
Un document du dépôt et une fiche de connaissance sont des objets distincts.

## Propriétés transversales

- UUID stable ; nom/chemin/signature ne servent pas seuls d'identité.
- Famille, variante réelle, rôle(s), portée/parent et éventuel langage.
- Statut de conception distinct : À valider, Validé, En cours, Implémenté.
- Présence/lien au code séparé : présent, sans code associé, non résolu, etc.
  La présence d'une fonction ne prouve pas son implémentation fonctionnelle.
- Provenance et sources, dates, confiance si pertinente, obsolescence.
- Relations entrantes/sortantes et références vers conversations/connaissances.
- Focus, sélection et dépliage sont des états de navigation, distincts du statut.
- Idea Box est un archivage avec motif/historique, pas un cinquième statut.

Dans la template, l'origine doit être explicitement **fictive**. Les quatre
statuts peuvent illustrer des données de scénario sans prétendre valider une
décision concernant le véritable projet Oryn.

## Première template recommandée

Un petit projet fictif avec un dossier, deux fichiers, un module, une classe,
une struct, un contrat, deux méthodes/fonctions, un champ et une dépendance.
Ajouter un composant conceptuel sans code pour distinguer intention et présence.

Relations de départ : contient, appelle, implémente et dépend de. Inclure les
quatre statuts de conception sur des éléments fictifs. Montrer les membres dans
les cartes de type avant d'ouvrir un graphe très détaillé.

Ordre proposé : composants de nœuds → sélection/navigation → liens simples →
états et légende → petit exemple de flot interne. Styles, dimensions, couleurs,
ports et tracé des flèches restent à choisir avec l'utilisateur.

Cette étape reste une préparation visuelle M0 ; elle ne lance ni persistance M2,
ni analyse M3, ni CFG réel.
