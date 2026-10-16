# Bilan des modèles locaux — M1

Ce bilan rassemble les essais réalisés sur une machine Linux avec Ryzen 5 5500,
16 Gio de RAM et RTX 3060 12 Gio, via llama.cpp. Les mesures décrivent cette
configuration et ces prompts ; elles ne prédisent pas exactement les résultats
sur une autre machine ou sur le dépôt Oryn complet.

## Lire les chiffres de vitesse

Un **token** est un petit morceau de texte choisi par le tokenizer du modèle. Ce
n’est pas toujours un mot : un mot rare, un nom de méthode ou un mot accentué
peut être découpé en plusieurs morceaux. Le débit en tokens/s mesure la vitesse
à laquelle le modèle produit la réponse, après avoir lu la demande.

La conversion utile est simplement :

> durée de génération ≈ nombre de tokens produits ÷ tokens par seconde

Ainsi, à 6 tokens/s, une réponse de 100 tokens prend environ 17 secondes et
300 tokens environ 50 secondes. À 30 tokens/s, les mêmes réponses prennent
environ 3 et 10 secondes. Ce sont des calculs sur des morceaux de texte, pas une
garantie de longueur en mots.

Il faut aussi séparer deux délais. Le **premier token** arrive après que le
serveur a lu la demande et le contexte ; un long fichier peut retarder ce moment.
Ensuite, les tokens de réponse arrivent au débit mesuré. Par exemple, Devstral
produit sa réponse de test simple à environ 6 tokens/s, et met 31,6 secondes
avant de répondre après lecture d’un texte de 6122 tokens. Ce dernier délai
comprend surtout le traitement de l’entrée.

Le débit ne mesure pas la qualité. Un modèle peut écrire rapidement une réponse
fausse. Et le réglage de contexte est une capacité réservée au serveur : une
question plus difficile ne fait pas, à elle seule, grandir les poids du modèle
en RAM. Une demande plus longue consomme davantage de contexte et demande plus
de calcul à la lecture ; la longueur de la réponse ajoute du temps de génération.

## Résultats comparables de code

Le banc principal comporte 13 tâches en français : six implémentations C# avec
compilation et tests indépendants, quatre diagnostics vérifiés et trois
problèmes d’architecture à contraintes vérifiables. Certains scores excluent
deux formulations ambiguës, merge et événements ; cette vue sur 11 tâches est
plus prudente. Les répétitions sont des graines appliquées aux mêmes questions,
pas de nouvelles questions indépendantes.

| Rang pratique | Modèle | Résultat solo vérifié | Vitesse et ressources observées | Lecture pour Oryn |
|---:|---|---|---|---|
| 1 | GPT-OSS-20B MXFP4 | Après une réparation : 9/11 sur chacune des trois graines ; 4–5/6 implémentations selon la graine. | Médiane 60 tokens/s en comptant le raisonnement ; première réponse visible médiane 15,9 s. À 8192 de contexte, GPU échantillonné jusqu’à 11,7 Gio. | Meilleur équilibre de qualité mesuré. À essayer comme modèle principal avec outils et tests ; la marge GPU est serrée. |
| 2 | Qwen3-Coder-30B-A3B Q4_K_M | Après réparation : 5–6/11 ; 2–3/6 implémentations. | Médiane 36 tokens/s ; sur une entrée de 5576 tokens, premier contenu après 27,8 s. Maximum GPU échantillonné environ 10,6 Gio et RSS environ 9,6 Gio. | Spécialisé code, mais n’a pas dépassé GPT-OSS. Peut fonctionner en hybride, au prix d’un contexte d’entrée lent. |
| 3 | Qwen2.5-Coder-7B Q6_K | Après réparation : 3–4/11 ; 2/6 implémentations à chaque graine. | Médiane 43,8 tokens/s ; environ 6,7 Gio GPU échantillonnés. | Plus léger et rapide. Sur six questions C# directes, 4–5/6 ; reste trop irrégulier pour écrire et valider seul. |
| 4 | Devstral Small 2 24B Q4_K_M | Une graine : 5/11 après réparation, mais 0/6 implémentations solo. | Environ 5,8–6,1 tokens/s ; réponse simple de code proche de 50 s ; environ 10,5–11,3 Gio GPU échantillonnés. | Bonne récupération avec outils sur quelques cas, mais beaucoup trop lent ici pour être le modèle de code courant. |

Les comparaisons ne sont pas des expériences où seul le nom du modèle change :
les quantifications, le raisonnement, les profils de sampling et le nombre de
graines diffèrent. En particulier, Devstral n’a qu’une graine de banc principal.
Les résultats indiquent les configurations à approfondir, pas un classement
universel des modèles.

## Modèles testés pour le chat général

| Modèle | Mesures locales | Conclusion limitée |
|---|---|---|
| Qwen3-8B Q4_K_M | Environ 54 tokens/s ; 6,4 Gio GPU à 8192 de contexte. 2/6 aux questions C# synthétiques répétées. | Très bonne vitesse et modèle utile pour vérifier le transport. Les réponses mesurées ne justifient pas de lui déléguer seul l’analyse du code. |
| Qwen3-14B Q4_K_M | Environ 30,6 tokens/s ; 10,15 Gio GPU à 8192. Réussit le calcul simple et une recherche dans 5580 tokens ; première réponse visible sur l’exercice avec raisonnement à 8,5 s. | Tient sur la carte dans l’essai, mais aucun banc de code complet ne permet de le classer face à GPT-OSS. |

Les smoke tests de 8B et 14B valident streaming, annulation et quelques réponses
connues. Ce ne sont pas des scores de qualité équivalents au banc de 13 tâches.
Les deux ont aussi échoué à un exemple vérifiable sur les méthodes par défaut
d’interfaces C#.

**Gemma 3 12B** n’a pas été téléchargé ni testé : le dépôt officiel a refusé
l’accès (HTTP 401). **DeepSeek-R1-Distill-Qwen-7B** figurait dans la liste de
candidats mais n’a pas fait l’objet de ces mesures. Ils ne sont donc pas classés.

## Ce que change une boucle d’agent

Un modèle peut recevoir des outils pour lire un fichier, modifier une méthode,
lancer la compilation et recevoir le résultat des tests. C’est important : le
modèle n’a plus à deviner s’il a compilé ou si les cas limites fonctionnent.
Il peut corriger une erreur à partir d’un diagnostic réel. L’agent doit aussi
borner les fichiers accessibles, le nombre d’essais et le temps, puis montrer
le changement à l’utilisateur.

Sur quatre fixtures ciblées avec outils natifs, exemples supplémentaires et
contrats clarifiés :

| Modèle | Réussites | Temps total du lot | Limite observée |
|---|---:|---:|---|
| GPT-OSS-20B, effort moyen | 3/4 | 183 s | N’a pas atteint l’outil sur UTF-8 : plafond de 4096 tokens consommé par le raisonnement. À effort bas, il atteint les tests mais échoue encore sur les octets invalides/incomplets. |
| Devstral Small 2 | 3/4 | 1041 s (17 min) | Répare UTF-8 et asynchronisme ; échoue sur la gestion des événements. |
| Qwen3-Coder-30B | 2/4 | 155 s | Réussit merge et événements ; échoue sur UTF-8 et asynchronisme. |

Ce lot n’est pas directement comparable au banc solo : l’agent part d’un code
volontairement cassé, voit des exemples publics et reçoit des contrats précisés.
Il vérifie tout de même une idée essentielle pour Oryn : **le système de tests
et d’outils peut transformer une réponse plausible en changement vérifiable**.
Les résultats justifient de prototyper l’orchestration autour de GPT-OSS, mais
ne prouvent pas que l’agent sait résoudre un ticket réel dans un dépôt complet.

Un essai séparé combinant Qwen3-8B comme préparateur de prompt puis Coder-7B
n’améliore pas la précision : le Coder direct fait 4–5/6 aux questions C#,
contre 3/6 pour la chaîne. Ajouter un modèle n’améliore donc pas
automatiquement le résultat ; le rôle de préparation doit être mesuré lui aussi.

## Classement et recommandation

Pour avancer vers Oryn sur cette machine, le classement de travail est :

1. **GPT-OSS-20B avec une boucle d’outils bornée** : meilleur score solo, bon
   résultat sur les fixtures agent et temps d’agent nettement inférieur à
   Devstral. Garder un contrôle strict de la VRAM et du délai de raisonnement.
2. **Qwen3-Coder-30B avec les mêmes outils** : candidat spécialisé secondaire ;
   moins précis que GPT-OSS sur le banc principal et plus lent à traiter un long
   contexte.
3. **Qwen2.5-Coder-7B** : option plus légère pour des tâches C# étroites, avec
   tests obligatoires. Ne pas lui confier les décisions d’architecture.
4. **Devstral Small 2** : preuve intéressante que l’itération avec tests peut
   corriger les réponses solo, mais sa vitesse rend l’usage quotidien difficile.

Qwen3-8B et 14B restent des références de chat général et de vitesse ; leur
qualité de code n’a pas été mesurée assez largement pour les placer au-dessus
des candidats Coder. Gemma et DeepSeek restent hors classement faute d’essai.

Le prochain gain important ne vient probablement pas d’un modèle encore plus
grand. Il vient d’un chemin où Oryn fournit le bon extrait de code et les
contraintes, laisse le modèle proposer une petite modification, lance Roslyn/
les tests, renvoie les erreurs utiles, puis montre un diff révisable. Commencer
avec un seul modèle principal chargé à la fois ; n’ajouter un modèle préparateur
ou spécialiste que si une évaluation montre un gain net.

## Rapports détaillés

- [PoC d’inférence, Qwen3-8B et 14B](m1-inference-poc.md)
- [Qwen2.5-Coder-7B et essai à deux modèles](m1-candidate-models.md)
- [Banc Coder avec tests exécutables](m1-coder-solo-evaluation.md)
- [GPT-OSS-20B](m1-gptoss-evaluation.md)
- [Qwen3-Coder-30B et Devstral](m1-large-coder-evaluation.md)
