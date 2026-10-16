"""Separate repair control: concrete failing examples instead of invariant names."""
import json
from pathlib import Path

from coder_bench import POLICY, SAMPLING, oracle_hash
from coder_cases import CASES, VERSION
from coder_verify import verify_code
from evaluate import server
from setup import ROOT
from smoke import chat

EXAMPLES = {
    "topological-order": 'Le test nodes=["c","b","a"], edges=[("b","a")] échoue. Résultat attendu : ["b","a","c"]. Chaque choix doit prendre le plus petit nom parmi les nœuds actuellement disponibles, pas uniquement parmi ceux disponibles au départ.',
    "generation-events": 'Le test events=["start:A","chunk:A:one","stop:A","chunk:A:late"] échoue. Résultat attendu : "one". Après Stop, le texte partiel doit rester disponible et les fragments ultérieurs de cette génération ne doivent plus être acceptés.',
    "async-forward": 'Une source de test ignore volontairement le token passé à GetAsyncEnumerator. Le consommateur lit "first", annule le token, puis appelle MoveNextAsync à nouveau. Ta méthode livre alors "late", ce qui viole le contrat. Le deuxième appel doit terminer sans nouvel élément ou lever OperationCanceledException. La propagation du token ne suffit pas pour cette source.',
}


def main():
    output=ROOT / "artifacts/m1/coder-solo-concrete-feedback"
    output.mkdir(parents=True,exist_ok=True)
    original=json.loads((ROOT / "artifacts/m1/coder-solo/results.json").read_text())
    proof=json.loads((ROOT / "artifacts/m1/coder-solo-v2/self-test/oracles.json").read_text())
    assert proof["passed"] and proof["oracle_sha256"]==oracle_hash()
    report={"version":VERSION,"oracle_sha256":oracle_hash(),"seed":42,"sampling":SAMPLING,
            "condition":"one alternative repair from the ORIGINAL first answer, not a third sequential attempt",
            "server":{},"cases":{}}
    path=output / "results.json"
    try:
        with server("coder7b-q6",18080,path,report["server"]):
            for case in CASES:
                if case["id"] not in EXAMPLES:continue
                prompt=("Corrige ta proposition à partir du contre-exemple vérifiable. Respecte toutes les contraintes "
                        "de la demande originale. Renvoie seulement les méthodes corrigées.\nDEMANDE:\n"+
                        case["prompt"]+"\nPROPOSITION INITIALE:\n"+
                        original["runs"]["42"][case["id"]]["first"]["content"]+
                        "\nCONTRE-EXEMPLE:\n"+EXAMPLES[case["id"]])
                answer=chat(18080,prompt,limit=1800,sampling=SAMPLING,system=POLICY)
                check=verify_code(case,answer["content"],output / case["id"])
                if answer["output_truncated"]:check["passed"]=False
                report["cases"][case["id"]]={"counterexample":EXAMPLES[case["id"]],"answer":answer,"check":check}
                path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
                print(case["id"],check["passed"],flush=True)
    finally:
        path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
        print(path,flush=True)


if __name__=="__main__":
    main()
