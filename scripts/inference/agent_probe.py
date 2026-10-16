"""Tiny native-tool coding experiment; isolated fixtures, no general shell access."""
import argparse
import http.client
import json
from pathlib import Path
import time

from coder_bench import oracle_hash, POLICY, feedback
from coder_cases import CASES
from coder_verify import verify_code
from evaluate import server
from setup import MANIFEST

TOOLS=[{'type':'function','function':{'name':name,'description':description,
        'parameters':{'type':'object','properties':properties,'required':list(properties),'additionalProperties':False}}}
       for name,description,properties in [
           ('read_file','Read Solution.cs or the read-only ContractExamples.md.',
            {'path':{'type':'string','enum':['Solution.cs','ContractExamples.md']}}),
           ('write_file','Replace Solution.cs with methods only, without class or using directives.',
            {'path':{'type':'string','enum':['Solution.cs']},'content':{'type':'string'}}),
           ('run_tests','Compile the current methods and run independent hidden contract tests.',{})]]

PUBLIC_EXAMPLES={
 'merge-intervals':'Closed real intervals: [(1,2),(2,3)] => [(1,3)]; [(1,2),(3,4)] remain separate. Start > End throws ArgumentException. Do not mutate the input.',
 'utf8-fragments':r'''UTF-8 bytes [C3], [A9 0D], [0A] describe "é\r\n" and must yield ["é"].
"a\n" yields ["a"], "\n" yields [""], "x\ry" yields ["x\ry"].
A single byte FF or an incomplete C3 sequence must throw DecoderFallbackException.
These are illustrative contract examples; the independent verifier checks more cases.''',
 'generation-events':'[start:A, chunk:A:bonjour, stop:A, chunk:A:late] => bonjour.\n[start:A, chunk:A:old, start:B, stop:A, chunk:A:late, chunk:B:new] => new.',
 'async-forward':'An already-cancelled token must not call GetAsyncEnumerator. If the provider ignores cancellation and returns another element, do not yield that late element. Pass the token to the source and always dispose the enumerator. Source exceptions must propagate.'}


def complete(port,messages,seed,sampling,prompt_cache=True,max_tokens=4096):
    data={'model':'oryn-core','messages':messages,'tools':TOOLS,'tool_choice':'auto',
          'parallel_tool_calls':False,'stream':True,'stream_options':{'include_usage':True},
          'max_tokens':max_tokens,**sampling,'min_p':0.0,'seed':seed,'cache_prompt':prompt_cache}
    c=http.client.HTTPConnection('127.0.0.1',port,timeout=90)
    start=time.perf_counter();content='';reasoning='';calls={};finish=None;usage=None;done=False
    try:
        c.request('POST','/v1/chat/completions',json.dumps(data),{'Content-Type':'application/json'})
        response=c.getresponse()
        if response.status!=200:raise RuntimeError(f'HTTP {response.status}: {response.read().decode()[:500]}')
        while line:=response.readline():
            if not line.startswith(b'data: '):continue
            if line[6:].strip()==b'[DONE]':done=True;break
            event=json.loads(line[6:]);usage=event.get('usage') or usage
            if 'error' in event:raise RuntimeError(str(event['error']))
            for choice in event.get('choices',[]):
                finish=choice.get('finish_reason') or finish
                delta=choice.get('delta',{})
                content+=delta.get('content') or ''
                reasoning+=delta.get('reasoning_content') or ''
                for chunk in delta.get('tool_calls',[]):
                    call=calls.setdefault(chunk['index'],{'id':'','type':'function','function':{'name':'','arguments':''}})
                    if chunk.get('id'):call['id']=chunk['id']
                    function=chunk.get('function',{})
                    call['function']['name']+=function.get('name') or ''
                    call['function']['arguments']+=function.get('arguments') or ''
    finally:
        if 'response' in locals():response.close()
        c.close()
    if not done or not finish:raise RuntimeError('Incomplete tool stream')
    message={'role':'assistant','content':content or None}
    if reasoning:message['reasoning_content']=reasoning
    if calls:message['tool_calls']=[calls[i] for i in sorted(calls)]
    # Reasoning is preserved in memory for the next turn, never persisted.
    public={'content':content,'tool_calls':message.get('tool_calls',[]),'reasoning_characters':len(reasoning),
            'elapsed_seconds':time.perf_counter()-start,'usage':usage,'finish_reason':finish}
    return message,public


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--oracles',type=Path,required=True)
    p.add_argument('--seed',type=int,default=42)
    p.add_argument('--model',choices=('gptoss20b-mxfp4','qwen3coder30b-q4','devstral24b-q4'),default='gptoss20b-mxfp4')
    p.add_argument('--cpu-moe',type=int,default=3)
    p.add_argument('--moe-cache-mib',type=int,default=0)
    p.add_argument('--gpu-layers',type=int,default=99)
    p.add_argument('--cpu-ffn',type=int,default=0)
    p.add_argument('--cache-type',choices=('f16','q8_0'),default='f16')
    p.add_argument('--prompt-cache',action=argparse.BooleanOptionalAction,default=True)
    p.add_argument('--context',type=int,default=16384)
    p.add_argument('--reasoning-effort',choices=('low','medium','high'),default='medium')
    p.add_argument('--max-tokens',type=int,default=4096)
    p.add_argument('--cases',nargs='+',choices=('merge-intervals','utf8-fragments','generation-events','async-forward'),
                   default=['merge-intervals','utf8-fragments','generation-events','async-forward'])
    p.add_argument('--port',type=int,default=18080)
    args=p.parse_args()
    proof=json.loads(args.oracles.read_text())
    if not proof['passed'] or proof['oracle_sha256']!=oracle_hash():p.error('Current successful oracle proof required')
    args.output.mkdir(parents=True,exist_ok=True)
    if args.max_tokens<256 or args.max_tokens>16384:p.error('--max-tokens must be between 256 and 16384')
    report={'oracle_sha256':oracle_hash(),'seed':args.seed,'turn_limit':8,'test_call_limit':4,
            'completion_policy':'host stops immediately on verified contract success','prompt_cache':args.prompt_cache,
            'reasoning_effort':args.reasoning_effort,'max_tokens':args.max_tokens,
            'scope':'existing single-file fixtures with native tools; not a general repo agent','selected_cases':args.cases,
            'server':{},'cases':{}}
    path=args.output/'results.json';started=time.perf_counter()
    candidate=MANIFEST['candidates'][args.model]
    thinking=candidate['supports_reasoning']
    try:
        with server(args.model,args.port,path,report['server'],context=args.context,cpu_moe=args.cpu_moe,
                    moe_cache_mib=args.moe_cache_mib,gpu_layers=args.gpu_layers,
                    cpu_ffn=args.cpu_ffn,cache_type=args.cache_type,
                    reasoning='on' if thinking else 'off',reasoning_budget=-1,reasoning_effort=args.reasoning_effort if thinking else 'default'):
            for case in CASES:
                if case['id'] not in args.cases:continue
                print(case['id'],flush=True)
                folder=args.output/case['id'];folder.mkdir(parents=True,exist_ok=True)
                editable=folder/'Solution.cs';editable.write_text(case['mutant'])
                (folder/'ContractExamples.md').write_text(PUBLIC_EXAMPLES[case['id']])
                entry=report['cases'][case['id']]={'steps':[],'initial':'predefined negative-control implementation'}
                contract=case['prompt']
                if case['id']=='merge-intervals':
                    contract+=(' Précision : ce sont des intervalles fermés sur la droite réelle. '
                               '[1,2] et [2,3] fusionnent ; [1,2] et [3,4] restent séparés. '
                               'Le critère de fusion est next.Start <= current.End, sans +1.')
                if case['id']=='generation-events':
                    contract+=(' Précision : la génération arrêtée reste la génération courante consultable. '
                               'Après start:A, chunk:A:bonjour, stop:A, le résultat est bonjour. '
                               'Seul un start suivant remplace cette réponse.')
                entry['contract']=contract
                messages=[{'role':'system','content':POLICY+' Tu travailles sur un fichier local Solution.cs. '
                           'Utilise les outils pour le lire, le corriger et vérifier les tests avant de terminer. '
                           'Seul Solution.cs est modifiable. ContractExamples.md est consultable en lecture seule. '
                           'Maximum quatre appels run_tests.'},
                          {'role':'user','content':'Corrige le fichier Solution.cs pour respecter ce contrat. '
                           'Des exemples publics sont disponibles dans ContractExamples.md.\n'+contract}]
                tests=0
                verified=False
                for turn in range(8):
                    message,public=complete(args.port,messages,args.seed,candidate['sampling'],args.prompt_cache,args.max_tokens)
                    step={'turn':turn+1,'response':public,'tool_results':[]};entry['steps'].append(step)
                    messages.append(message)
                    if public['finish_reason']=='length':entry['truncated']=True;break
                    calls=message.get('tool_calls',[])
                    if not calls:break
                    for call in calls:
                        name=call['function']['name']
                        try:
                            params=json.loads(call['function']['arguments'])
                            if not isinstance(params,dict):raise ValueError('Arguments must be an object')
                            if name=='read_file' and params.get('path') not in ('Solution.cs','ContractExamples.md'):
                                raise ValueError('Only fixture files are readable')
                            if name=='write_file' and params.get('path')!='Solution.cs':
                                raise ValueError('Only Solution.cs is writable')
                            if name=='read_file':result={'content':(folder/params['path']).read_text()}
                            elif name=='write_file':
                                text=params.get('content')
                                if not isinstance(text,str) or len(text)>32000:raise ValueError('Invalid content size')
                                editable.write_text(text);result={'written':True}
                            elif name=='run_tests':
                                if tests>=4:raise ValueError('Test call limit reached')
                                tests+=1
                                checked=verify_code(case,editable.read_text(),folder/f'test-{tests}')
                                result={'passed':checked['passed'],'feedback':feedback(checked) if not checked['passed'] else 'All contract tests passed.'}
                            else:raise ValueError('Unknown tool')
                        except (ValueError,TypeError) as error:result={'error':str(error)}
                        step['tool_results'].append({'name':name,'result':result})
                        messages.append({'role':'tool','tool_call_id':call['id'],'content':json.dumps(result,ensure_ascii=False)})
                        if name=='run_tests' and result.get('passed') is True:
                            entry['stopped_on_verified_success']=True
                            verified=True
                            break
                    path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
                    if verified:break
                entry['test_calls']=tests
                entry['final_check']=checked if verified else verify_code(case,editable.read_text(),folder/'final')
                entry['reused_successful_check']=verified
                print('  passed',entry['final_check']['passed'],'turns',len(entry['steps']),'checks',tests,flush=True)
                path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    except Exception as error:
        report['error']=str(error);raise
    finally:
        report['wall_seconds']=time.perf_counter()-started
        path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')


if __name__=='__main__':main()
