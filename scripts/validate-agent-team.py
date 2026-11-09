#!/usr/bin/env python3
"""Basic structural validation for the generated Claude Code team."""
from pathlib import Path
import json, re, sys
root=Path(__file__).resolve().parents[1]
errors=[]
agents=list((root/'.claude/agents').glob('*.md'))
skills=list((root/'.claude/skills').glob('*/SKILL.md'))
if len(agents)!=8: errors.append(f'Expected 8 agents, found {len(agents)}')
if len(skills)<12: errors.append(f'Expected >=12 skills, found {len(skills)}')
for p in skills:
    txt=p.read_text(encoding='utf-8')
    if not txt.startswith('---\n') or '\nname:' not in txt or '\ndescription:' not in txt: errors.append(f'Invalid skill frontmatter: {p}')
for p in agents:
    txt=p.read_text(encoding='utf-8')
    if not txt.startswith('---\n') or 'name:' not in txt or 'tools:' not in txt: errors.append(f'Invalid agent frontmatter: {p}')
if errors:
    print('\n'.join(errors)); sys.exit(1)
print(f'OK: {len(agents)} agents, {len(skills)} skills')
