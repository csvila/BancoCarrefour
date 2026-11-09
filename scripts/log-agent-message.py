#!/usr/bin/env python3
"""Append one auditable agent communication event to JSONL."""
import argparse, datetime, json, os, uuid
from pathlib import Path
try:
    import fcntl
except ImportError:
    fcntl = None
parser=argparse.ArgumentParser()
parser.add_argument('--from', dest='sender', required=True)
parser.add_argument('--to', required=True)
parser.add_argument('--type', required=True, choices=['delegation','response','finding','decision','handoff','correction-request','correction-result','status','closure','bootstrap'])
parser.add_argument('--task', required=True)
parser.add_argument('--summary', required=True)
parser.add_argument('--finding-ids', default='')
parser.add_argument('--artifacts', default='')
parser.add_argument('--status', default='recorded')
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
path=root/'docs/governance/agent-communications.jsonl'
record={'schemaVersion':1,'timestamp':datetime.datetime.now(datetime.timezone.utc).isoformat(),'messageId':str(uuid.uuid4()),'from':args.sender,'to':args.to,'type':args.type,'task':args.task,'summary':args.summary,'findingIds':[x for x in args.finding_ids.split(',') if x],'artifacts':[x for x in args.artifacts.split(',') if x],'status':args.status}
line=json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n'
path.parent.mkdir(parents=True,exist_ok=True)
with path.open('a',encoding='utf-8') as f:
    if fcntl: fcntl.flock(f.fileno(),fcntl.LOCK_EX)
    f.write(line); f.flush(); os.fsync(f.fileno())
    if fcntl: fcntl.flock(f.fileno(),fcntl.LOCK_UN)
print(record['messageId'])
