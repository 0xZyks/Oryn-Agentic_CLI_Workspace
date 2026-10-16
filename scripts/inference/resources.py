"""Sample whole-GPU use and Linux host pressure during local experiments."""
from pathlib import Path
import threading

from smoke import gpu, host_memory


def swap_counters():
    return {k:int(v) for k,v in (line.split() for line in Path('/proc/vmstat').read_text().splitlines())
            if k in ('pswpin','pswpout')}


class ResourceSamples:
    def __init__(self, pid):
        self.pid=pid
        self.stop=threading.Event()
        self.report={'interval_seconds':2,'scope':'after readiness, whole GPU; sampled extrema, not guaranteed peaks',
                     'samples':0,'swap_before':swap_counters()}
        self.thread=threading.Thread(target=self.run,daemon=True)

    def sample(self):
        memory=host_memory(self.pid)
        used=int(gpu().split(',')[2].strip())
        r=self.report
        r['samples']+=1
        r['max_gpu_used_mib']=max(r.get('max_gpu_used_mib',0),used)
        r['max_process_rss_kib']=max(r.get('max_process_rss_kib',0),memory['VmRSS_kib'])
        r['max_process_swap_kib']=max(r.get('max_process_swap_kib',0),memory.get('VmSwap_kib',0))
        r['min_system_available_kib']=min(r.get('min_system_available_kib',memory['system_available_kib']),memory['system_available_kib'])

    def run(self):
        while not self.stop.is_set():
            try:self.sample()
            except (OSError,ValueError,IndexError,KeyError) as error:self.report['sampling_error']=str(error)
            self.stop.wait(2)

    def __enter__(self):
        self.thread.start()
        return self.report

    def __exit__(self,*args):
        self.stop.set()
        self.thread.join(timeout=12)
        self.report['swap_after']=swap_counters()
        self.report['swap_delta_pages']={k:self.report['swap_after'][k]-v for k,v in self.report['swap_before'].items()}
