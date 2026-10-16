"""Bounded local candidate fit/speed probes, including cancellation."""
import argparse
import json
from pathlib import Path
import time

from evaluate import server
from setup import MANIFEST
from smoke import chat, request, wait_idle


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--model',choices=('gptoss20b-mxfp4','qwen3coder30b-q4','devstral24b-q4'),default='gptoss20b-mxfp4')
    p.add_argument('--cpu-moe',type=int,default=0)
    p.add_argument('--moe-cache-mib',type=int,default=0)
    p.add_argument('--gpu-layers',type=int,default=99)
    p.add_argument('--cpu-ffn',type=int,default=0)
    p.add_argument('--cache-type',choices=('f16','q8_0'),default='f16')
    p.add_argument('--context',type=int,default=4096)
    p.add_argument('--long-input',action='store_true')
    p.add_argument('--throughput-repeats',type=int,choices=range(0,4),default=0)
    p.add_argument('--port',type=int,default=18080)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    r={'server':{},'tests':{},'context':args.context,'cpu_moe':args.cpu_moe}
    candidate=MANIFEST['candidates'][args.model]
    thinking=candidate['supports_reasoning']
    sampling={**candidate['sampling'],'min_p':0.0}
    start=time.perf_counter()
    try:
        with server(args.model,args.port,args.output,r['server'],context=args.context,
                    gpu_layers=args.gpu_layers,
                    cpu_ffn=args.cpu_ffn,cache_type=args.cache_type,
                    cpu_moe=args.cpu_moe,moe_cache_mib=args.moe_cache_mib,
                    reasoning='on' if thinking else 'off',reasoning_budget=-1,reasoning_effort='medium' if thinking else 'default'):
            r['tests']['short']=chat(args.port,'Calcule 17*19. Termine par RESULTAT=nombre.',
                                   thinking=thinking,limit=1024,sampling=sampling,require_progressive=False)
            r['tests']['short']['correct']='RESULTAT=323' in r['tests']['short']['content'].replace(' ','')
            if thinking and r['tests']['short']['reasoning_chunks']==0:
                raise RuntimeError('Native reasoning was not observed')
            r['tests']['code']=chat(args.port,'Écris une méthode C# public static int Max(int a,int b) et explique brièvement son fonctionnement.',
                                  thinking=thinking,limit=1024,sampling=sampling)
            r['tests']['cancel']=chat(args.port,'Écris une explication détaillée des structures de données de graphes, avec de nombreux exemples C#.',
                                    thinking=thinking,limit=2048,sampling=sampling,cancel_after=12)
            r['tests']['idle_after_cancel_seconds']=wait_idle(args.port)
            r['tests']['after_cancel']=chat(args.port,'Écris exactement cette phrase : Le service local fonctionne correctement.',
                                           thinking=thinking,limit=512,sampling=sampling)
            status,body=request(args.port,'POST','/v1/chat/completions',{'messages':'invalid'})
            r['tests']['invalid_request']={'status':status,'body':body[:500]}
            if status!=400:raise RuntimeError('Malformed request should return 400')
            if args.throughput_repeats:
                r['tests']['throughput']=[chat(args.port,
                    'Écris un guide pédagogique très détaillé de plus de 1000 mots sur les graphes orientés et leurs algorithmes, avec du code C# et des exemples.',
                    thinking=thinking,limit=512,sampling=sampling)
                    for _ in range(args.throughput_repeats)]
            if args.long_input:
                from smoke import long_input_check
                # server records its PID for reproducible resource observations.
                pid=r['server']['pid']
                r['tests']['long_input']=long_input_check(args.port,args.context,pid,thinking=thinking,
                                                         limit=1024 if thinking else 96,
                                                         timeout=180 if args.model=='devstral24b-q4' else 60)
    except Exception as error:
        r['error']=str(error)
        raise
    finally:
        r['wall_seconds']=time.perf_counter()-start
        args.output.write_text(json.dumps(r,ensure_ascii=False,indent=2)+'\n')
        print(args.output,flush=True)


if __name__=='__main__':main()
