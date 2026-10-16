"""Versioned, hand-authored oracles. Never include reference/tests in model prompts."""
VERSION = "coder-solo-v2"

PRELUDE = """using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
"""

CASES = [
    {
        "id": "mean-overflow", "category": "implementation",
        "prompt": "Implémente public static double Mean(int[] values). Le tableau non nul peut être vide (ArgumentException attendue). Renvoie la moyenne arithmétique exacte à la précision double, y compris pour des entiers négatifs et int.MaxValue/int.MinValue, sans modifier l'entrée. Corrige les défauts de ce code : int sum=0; foreach(var v in values) sum+=v; return sum/values.Length;",
        "reference": "public static double Mean(int[] values) { if(values.Length==0) throw new ArgumentException(); long sum=0; foreach(var v in values) sum+=v; return (double)sum/values.Length; }",
        "mutant": "public static double Mean(int[] values) { if(values.Length==0) throw new ArgumentException(); int sum=0; foreach(var v in values) sum+=v; return sum/values.Length; }",
        "tests": """
Throws<ArgumentException>(() => Solution.Mean(Array.Empty<int>()), "empty");
var examples = new[] { new[]{5,2}, new[]{-5,2}, new[]{int.MaxValue,int.MaxValue}, new[]{int.MinValue,int.MaxValue}, new[]{0}, new[]{int.MinValue,int.MinValue} };
foreach(var a in examples) { var copy=a.ToArray(); Near(Solution.Mean(a),(double)a.Aggregate(0m,(s,v)=>s+v)/a.Length,"example mean"); Check(a.SequenceEqual(copy),"input unchanged"); }
var random = new Random(715);
for(int k=0;k<100;k++) { var a=Enumerable.Range(0,random.Next(1,60)).Select(_=>random.Next(int.MinValue,int.MaxValue)).ToArray(); Near(Solution.Mean(a),(double)a.Aggregate(0m,(s,v)=>s+v)/a.Length,"random mean"); }
""",
    },
    {
        "id": "merge-intervals", "category": "implementation",
        "prompt": "Implémente public static (int Start, int End)[] Merge((int Start, int End)[] ranges). Entrée non nulle, possiblement vide/non triée. Fusionne les intervalles qui se chevauchent OU se touchent, retourne les intervalles triés par Start, sans modifier le tableau d'entrée. Start > End doit provoquer ArgumentException. Accepte valeurs négatives, doublons, intervalles ponctuels et valeurs int extrêmes. Aucune dépendance externe.",
        "reference": """public static (int Start,int End)[] Merge((int Start,int End)[] ranges) {
if(ranges.Any(r=>r.Start>r.End)) throw new ArgumentException();
var sorted=ranges.OrderBy(r=>r.Start).ThenBy(r=>r.End).ToArray();
var result=new List<(int Start,int End)>();
foreach(var r in sorted) { if(result.Count==0 || r.Start>result[^1].End) result.Add(r); else result[^1]=(result[^1].Start,Math.Max(result[^1].End,r.End)); }
return result.ToArray(); }""",
        "mutant": "public static (int Start,int End)[] Merge((int Start,int End)[] ranges) => ranges.OrderBy(r=>r.Start).ToArray();",
        "tests": """
void MergeCase((int,int)[] input,(int,int)[] expected) { var copy=input.ToArray(); Check(Solution.Merge(input).SequenceEqual(expected),"merged intervals"); Check(input.SequenceEqual(copy),"input unchanged"); }
MergeCase([],[]); MergeCase([(5,8),(1,3),(2,6)],[(1,8)]);
MergeCase([(1,2),(2,3)],[(1,3)]); MergeCase([(1,2),(3,4)],[(1,2),(3,4)]);
MergeCase([(1,10),(2,3),(1,10)],[(1,10)]);
MergeCase([(-4,-2),(-2,0),(5,5)], [(-4,0),(5,5)]);
MergeCase([(int.MaxValue,int.MaxValue),(int.MinValue,int.MinValue)],[(int.MinValue,int.MinValue),(int.MaxValue,int.MaxValue)]);
MergeCase([(0,0),(0,0)],[(0,0)]);
Throws<ArgumentException>(()=>Solution.Merge([(2,1)]),"invalid interval");
""",
    },
    {
        "id": "topological-order", "category": "implementation",
        "prompt": "Implémente public static string[] Order(string[] nodes, (string Before, string After)[] edges). Les noms non nuls sont uniques. Une arête exige Before avant After. Renvoie un ordre topologique, en choisissant à chaque étape le plus petit nom disponible selon StringComparer.Ordinal. Les arêtes répétées sont ignorées. Nœud d'arête inconnu : ArgumentException. Cycle, y compris boucle sur soi : InvalidOperationException. Entrées vides autorisées ; ne modifie pas les entrées. Bibliothèque standard seulement.",
        "reference": """public static string[] Order(string[] nodes,(string Before,string After)[] edges) {
var degree=nodes.ToDictionary(n=>n,n=>0,StringComparer.Ordinal);
var next=nodes.ToDictionary(n=>n,n=>new HashSet<string>(StringComparer.Ordinal),StringComparer.Ordinal);
foreach(var (a,b) in edges) { if(!degree.ContainsKey(a)||!degree.ContainsKey(b)) throw new ArgumentException(); if(next[a].Add(b)) degree[b]++; }
var ready=new SortedSet<string>(degree.Where(p=>p.Value==0).Select(p=>p.Key),StringComparer.Ordinal);
var output=new List<string>(); while(ready.Count>0) { var n=ready.Min!;ready.Remove(n);output.Add(n);foreach(var m in next[n]) if(--degree[m]==0) ready.Add(m); }
if(output.Count!=nodes.Length) throw new InvalidOperationException();return output.ToArray(); }""",
        "mutant": "public static string[] Order(string[] nodes,(string Before,string After)[] edges)=>nodes.OrderBy(n=>n,StringComparer.Ordinal).ToArray();",
        "tests": """
Check(Solution.Order([],[]).Length==0,"empty graph");
Check(Solution.Order(["c","b","a"],[("b","a")]).SequenceEqual(new[]{"b","a","c"}),"available lexical order");
Check(Solution.Order(["a","b"],[("a","b"),("a","b")]).SequenceEqual(new[]{"a","b"}),"duplicate edges");
Throws<ArgumentException>(()=>Solution.Order(["a"],[("a","x")]),"unknown node");
Throws<InvalidOperationException>(()=>Solution.Order(["a","b"],[("a","b"),("b","a")]),"cycle");
Throws<InvalidOperationException>(()=>Solution.Order(["a"],[("a","a")]),"self cycle");
// Independent oracle: exhaustive enumeration of all 24 orders for 4 nodes.
var names=new[]{"a","b","c","d"}; var possible=new List<(string,string)>();
for(int i=0;i<4;i++)for(int j=i+1;j<4;j++)possible.Add((names[i],names[j]));
var permutations=Permutations(names).OrderBy(p=>string.Join(",",p),StringComparer.Ordinal).ToArray();
for(int mask=0;mask<64;mask++) { var edges=possible.Where((e,i)=>(mask&(1<<i))!=0).ToArray();
var expected=permutations.First(p=>edges.All(e=>Array.IndexOf(p,e.Item1)<Array.IndexOf(p,e.Item2)));
var input=new[]{"d","b","a","c"};var copy=input.ToArray();
Check(Solution.Order(input,edges).SequenceEqual(expected),"exhaustive small DAG"); Check(input.SequenceEqual(copy),"nodes unchanged"); }
""",
    },
    {
        "id": "utf8-fragments", "category": "implementation",
        "prompt": "Implémente public static string[] ReadLines(byte[][] chunks). Les tableaux non nuls sont des fragments d'un seul texte UTF-8 ; une séquence UTF-8 ou CRLF peut être coupée entre fragments. Séparateurs : LF et CRLF. Un CR isolé reste un caractère normal. Préserve les lignes vides mais ne crée pas de ligne fantôme après le dernier séparateur. Une dernière ligne sans séparateur est retournée. Entrée vide => tableau vide. UTF-8 invalide ou incomplet => DecoderFallbackException. Ne modifie pas l'entrée. Bibliothèque standard uniquement.",
        "reference": """public static string[] ReadLines(byte[][] chunks) {
var text=new UTF8Encoding(false,true).GetString(chunks.SelectMany(c=>c).ToArray());
if(text.Length==0)return Array.Empty<string>();var lines=text.Replace("\\r\\n","\\n").Split('\\n');
return text.EndsWith('\\n')?lines[..^1]:lines; }""",
        "mutant": "public static string[] ReadLines(byte[][] chunks)=>chunks.SelectMany(c=>Encoding.UTF8.GetString(c).Split('\\n')).ToArray();",
        "tests": """
Check(Solution.ReadLines([]).Length==0,"empty input");
var text="é😀\\r\\nZ\\n\\nlast\\rinside";var bytes=Encoding.UTF8.GetBytes(text);var expected=new[]{"é😀","Z","","last\\rinside"};
for(int split=0;split<=bytes.Length;split++) Check(Solution.ReadLines([bytes[..split],bytes[split..]]).SequenceEqual(expected),"every split including unicode and CRLF");
Check(Solution.ReadLines(bytes.Select(b=>new[]{b}).ToArray()).SequenceEqual(expected),"byte-at-a-time");
Check(Solution.ReadLines([Encoding.UTF8.GetBytes("a\\n")]).SequenceEqual(new[]{"a"}),"no phantom trailing line");
Check(Solution.ReadLines([Encoding.UTF8.GetBytes("\\n")]).SequenceEqual(new[]{""}),"empty line");
Throws<DecoderFallbackException>(()=>Solution.ReadLines([new byte[]{0xff}]),"invalid UTF8");
Throws<DecoderFallbackException>(()=>Solution.ReadLines([new byte[]{0xf0,0x9f}]),"incomplete UTF8");
""",
    },
    {
        "id": "generation-events", "category": "implementation",
        "prompt": "Implémente public static string Reduce(string[] events) pour un chat. Événements : start:ID démarre une nouvelle génération et vide sa réponse ; chunk:ID:TEXT ajoute TEXT uniquement si ID est la génération active et si elle n'a pas été arrêtée ; stop:ID arrête uniquement cette génération si elle est active et conserve sa réponse partielle. Un ancien fragment ou ancien stop ne doit jamais modifier la nouvelle génération. TEXT peut contenir ':'. Les ID sont non vides, uniques pour les start. Ignore les événements malformés. Retourne le texte de la dernière génération active ou chaîne vide si aucune. Fonction pure, pas d'I/O.",
        "reference": """public static string Reduce(string[] events) {
string? active=null;bool stopped=false;var text=new StringBuilder();
foreach(var e in events) {var p=e.Split(':',3);if(p.Length==2&&p[0]=="start"&&p[1].Length>0){active=p[1];stopped=false;text.Clear();}
else if(p.Length==2&&p[0]=="stop"&&p[1]==active)stopped=true;
else if(p.Length==3&&p[0]=="chunk"&&p[1]==active&&!stopped)text.Append(p[2]);}return text.ToString(); }""",
        "mutant": "public static string Reduce(string[] events)=>string.Concat(events.Where(e=>e.StartsWith(\"chunk:\")).Select(e=>e.Split(':',3)[2]));",
        "tests": """
void Trace(string[] events,string expected)=>Check(Solution.Reduce(events)==expected,"event trace");
Trace([],"");Trace(["chunk:A:x"],"");Trace(["start:A","chunk:A:one","stop:A","chunk:A:late"],"one");
Trace(["start:A","chunk:A:old","stop:A","start:B","chunk:A:late","chunk:B:new"],"new");
Trace(["start:A","start:B","stop:A","chunk:B:ok"],"ok");
Trace(["start:A","chunk:A:a:b:c"],"a:b:c");Trace(["start:A","chunk:A:","chunk:A:x"],"x");
Trace(["start:A","chunk:A:x","stop:A","stop:A"],"x");
Trace(["start:A","chunk:A:x","start:B"],"");Trace(["noise","start:","chunk:bad","stop:X"],"");
for(int count=1;count<30;count++){var events=new List<string>{"start:A","chunk:A:old","start:B"};for(int i=0;i<count;i++){events.Add("chunk:A:late");events.Add("stop:A");events.Add("chunk:B:b");}Trace(events.ToArray(),new string('b',count));}
""",
    },
    {
        "id": "async-forward", "category": "implementation",
        "prompt": "Implémente public static async IAsyncEnumerable<string> Forward(IAsyncEnumerable<string> source, [EnumeratorCancellation] CancellationToken token = default). Transmets les éléments dans l'ordre, propage token à l'énumération source et arrête-toi même si elle ignore l'annulation. Si token est déjà annulé au début, n'interroge pas la source. Ne livre aucun fragment supplémentaire après annulation. Préserve les exceptions et la libération de l'énumérateur, même si le consommateur s'arrête tôt. Pas de Task.Run, blocage, tâches orphelines ou dépendance externe.",
        "reference": """public static async IAsyncEnumerable<string> Forward(IAsyncEnumerable<string> source,[EnumeratorCancellation] CancellationToken token=default) {
token.ThrowIfCancellationRequested();await foreach(var item in source.WithCancellation(token)){token.ThrowIfCancellationRequested();yield return item;} }""",
        "mutant": "public static async IAsyncEnumerable<string> Forward(IAsyncEnumerable<string> source,[EnumeratorCancellation] CancellationToken token=default) {await foreach(var item in source){yield return item;}}",
        "tests": """
var normal=new Probe();var items=new List<string>();await foreach(var s in Solution.Forward(normal))items.Add(s);
Check(items.SequenceEqual(new[]{"first","late"}),"normal order");Check(normal.Disposals==1,"normal disposal");
using(var c=new CancellationTokenSource()){c.Cancel();var p=new Probe();bool emitted=false;try{await foreach(var s in Solution.Forward(p,c.Token)){emitted=true;}}catch(OperationCanceledException){}Check(!emitted,"pre-cancel emits nothing");Check(p.Enumerations==0,"pre-cancel does not query source");}
using(var c=new CancellationTokenSource()){var p=new Probe();await using(var e=Solution.Forward(p,c.Token).GetAsyncEnumerator()){Check(await e.MoveNextAsync(),"first element");Check(p.Token==c.Token,"token propagated");c.Cancel();bool more=false;try{more=await e.MoveNextAsync();}catch(OperationCanceledException){}Check(!more,"late item must not be emitted");}Check(p.Disposals==1,"cancel disposal");}
var broken=new Probe{ThrowAfterFirst=true};try{await foreach(var s in Solution.Forward(broken)){}Check(false,"source error must propagate");}catch(InvalidOperationException e){Check(e.Message=="source failure","same source error");}Check(broken.Disposals==1,"error disposal");
var early=new Probe();await foreach(var s in Solution.Forward(early)){break;}Check(early.Disposals==1,"early consumer disposal");
""",
    },
]

DIAGNOSTICS = [
    {"id":"mutable-key", "category":"diagnosis", "expected":{"found":False},
     "prompt": 'C#/.NET : class Key { public int Id; public override bool Equals(object? o)=>o is Key k && k.Id==Id; public override int GetHashCode()=>Id; } var k=new Key{Id=1}; var d=new Dictionary<Key,string>(); d.Add(k,"x"); k.Id=2; bool found=d.ContainsKey(k); Donne seulement JSON {"found":bool}. Explique le code uniquement si demandé, pas ici.'},
    {"id":"finally-return", "category":"diagnosis", "expected":{"result":1},
     "prompt": 'C# : static int F(){ int x=1; try{return x;} finally{x=2;} } Donne la valeur de F(), seulement JSON avec une clé result numérique.'},
    {"id":"when-all", "category":"diagnosis", "expected":{"completed_before_second":False,"exception":"InvalidOperationException"},
     "prompt": 'C# : var a=new TaskCompletionSource<int>(); var b=new TaskCompletionSource<int>(); var both=Task.WhenAll(a.Task,b.Task); a.SetException(new InvalidOperationException("x")); bool flag=both.IsCompleted; b.SetResult(7); puis on await both et on capture le type d\'exception. Donne seulement JSON : completed_before_second (flag), exception (nom court du type capturé).'},
    {"id":"semaphore-finally", "category":"diagnosis", "expected":{"exception":"SemaphoreFullException","count":1},
     "prompt": 'C# : var sem=new SemaphoreSlim(1,1); var cts=new CancellationTokenSource(); cts.Cancel(); try { try { await sem.WaitAsync(cts.Token); } finally { sem.Release(); } } catch(Exception e) { /* observe type e */ } Donne seulement JSON avec exception (nom court du type observé) et count (sem.CurrentCount final).'},
]

ARCHITECTURE = [
    {"id":"bounded-ui-pipeline", "category":"architecture",
     "prompt": 'Conçois la configuration d\'un pipeline local chat. Contraintes : 8 MiB maximum pour fragments en attente ET celui traité ; chaque fragment occupe au plus 2 MiB ; un seul worker traite un fragment ; aucune perte autorisée ; l\'UI ne doit attendre ni inférence ni écriture dans une file pleine ; elle seule modifie son état ; un provider peut ignorer CancellationToken et émettre après le démarrage de la génération suivante. Réponds seulement JSON avec capacity (entier strictement positif, nombre de fragments en file hors worker), overflow ("wait" ou "drop_oldest" ou "unbounded"), producer ("background" ou "ui"), state_writer ("ui" ou "background"), stale_guard ("generation_id" ou "cancellation_only" ou "none"). Plusieurs capacités peuvent être valides.'},
    {"id":"durable-chat-order", "category":"architecture",
     "prompt": 'Un message ne doit déclencher l\'inférence qu\'une fois le prompt utilisateur ET le placeholder de réponse durablement enregistrés dans une seule transaction. En cas d\'échec stockage, préserver le brouillon et ne pas appeler le modèle. Après crash, garder texte partiel, marquer les réponses en cours interrompues, ne pas renvoyer les prompts automatiquement. Donne seulement JSON : order (permutation de begin, save_user, save_pending, commit, infer respectant les contraintes), storage_failure ("keep_draft_no_infer" ou "infer_anyway"), restart_status ("interrupted" ou "complete"), keep_partial (bool), auto_resend (bool).'},
    {"id":"ambiguous-identity", "category":"architecture",
     "prompt": 'Un analyseur retrouve deux méthodes nouvelles pouvant chacune correspondre à une ancienne méthode avec UUID U. Confiances égales, pas de preuve de renommage. U porte un chat et une décision humaine Validé. Le parseur ne constate que la présence des nouvelles méthodes, pas leur bon fonctionnement. Contraintes : aucun chat ne doit être réattribué silencieusement ; aucune décision humaine effacée par rescan ; ne pas déclarer du code fonctionnel sur seule présence. Donne seulement JSON : old_identity ("preserve_unresolved" ou "replace_by_best_guess"), chat ("keep_on_U" ou "move_to_first"), human_status ("preserve" ou "reset"), mark_implemented (bool), manual_link (bool).'},
]
