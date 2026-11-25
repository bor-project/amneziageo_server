#!/usr/bin/env python3
"""Exercises proxy ownership across package transitions."""
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[1]
LEGACY = 'amneziageo-proxy@awg0.service'
PANEL = 'amneziageo-server'
DAEMON = 'amneziageo-proxy.service'

SYSTEMCTL = r'''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
r=Path(os.environ['PROXY_TEST_ROOT'])
p=r/'services.json'
s=json.loads(p.read_text())
a=[x for x in sys.argv[1:] if not x.startswith('--')]
verb=a.pop(0)
name=next((x for x in a if not x.startswith('-') and not x.startswith('MainPID')), '')
if name=='-p': name=a[-1]
name=name if name!=PANEL+'.service' else PANEL
trace=r/'calls.log'
with trace.open('a') as f:f.write(' '.join(sys.argv[1:])+'\n')
fail=r/'failure.json'
if verb=='stop' and name==PANEL and (r/'stop-update').exists():
 (r/'stop-update').unlink()
 (r/'etc/amneziageo-server/proxy-awg0.yaml').write_text('updated before panel exit\n')
if verb in ['start','restart'] and name==PANEL and (r/'api-cleanup').exists():
 current=r/'opt/amneziageo-server/current'
 if current.is_symlink() and os.readlink(current)=='releases/new':
  for pattern in ['proxy-*.env','proxy-*.yaml']:
   for file in (r/'etc/amneziageo-server').glob(pattern):file.unlink()
if fail.exists():
 rule=json.loads(fail.read_text())
 if verb==rule['verb'] and (not rule.get('name') or rule['name']==name):
  fail.unlink()
  print('injected systemctl failure',file=sys.stderr)
  sys.exit(1)
if verb=='list-units':
 for n,v in s.items():
  if n.startswith('amneziageo-proxy@'):print(n+' loaded '+v['active']+' running fixture')
 sys.exit(0)
if verb=='list-unit-files':
 pattern=next((x for x in a if '*' in x), '')
 if pattern and not any(n.startswith('amneziageo-proxy@') for n in s):sys.exit(1)
 for n,v in s.items():
  if not pattern or n.startswith('amneziageo-proxy@'):print(n+' '+v['enabled'])
 sys.exit(0)
if verb=='daemon-reload':sys.exit(0)
v=s.get(name,{'enabled':'not-found','active':'inactive','pid':0})
if verb=='is-enabled':
 print(v['enabled']);sys.exit(0 if v['enabled'].startswith('enabled') else 1)
if verb=='is-active':
 if '--quiet' not in sys.argv:print(v['active'])
 sys.exit(0 if v['active']=='active' else 3)
if verb=='show':
 print(v['pid']);sys.exit(0)
if verb in ['start','restart']:
 v['active']='active';v['pid']+=1
elif verb=='stop':v['active']='inactive'
elif verb=='enable':v['enabled']='enabled-runtime' if '--runtime' in sys.argv else 'enabled'
elif verb=='disable':
 if not v['enabled'].startswith('masked'):v['enabled']='disabled'
 if '--now' in sys.argv:v['active']='inactive'
elif verb=='mask':v['enabled']='masked-runtime' if '--runtime' in sys.argv else 'masked'
elif verb=='unmask':
 if v['enabled'].startswith('masked'):v['enabled']='disabled'
else:
 print('unsupported systemctl '+verb,file=sys.stderr);sys.exit(2)
s[name]=v;p.write_text(json.dumps(s))
'''.replace('PANEL',repr(PANEL))

class ProxyHandoverTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory(prefix='proxy-handover-')
  self.root=Path(self.temp.name)
  self.pkg=self.root/'package';self.pkg.mkdir()
  self.bin=self.root/'bin';self.bin.mkdir()
  for f in ['install.sh','proxy-install.sh','proxy-handover.sh','database-handover.sh']:
   text=(DEPLOY/f).read_text()
   for base in ['/opt/','/etc/','/var/lib/','/usr/local/','/run/']:
    text=text.replace(base,str(self.root)+base)
   (self.pkg/f).write_text(text)
  (self.pkg/'release').write_text('new\n');(self.pkg/'server').write_text('fixture\n')
  for f in ['amneziageo-server.service','amneziageo-proxy.service']:(self.pkg/f).write_text('[Service]\n')
  publish=self.pkg/'publish';(publish/'wwwroot').mkdir(parents=True)
  (publish/'wwwroot/index.html').write_text('new')
  for f in ['AmneziaGeo.Server.Api','AmneziaGeo.Server.Cli','AmneziaGeo.Server.Proxy.Host']:
   p=publish/f;p.write_text('#!/bin/sh\nif [ "$1" = init ]; then echo \'{}\' > "$2"; fi\n');p.chmod(0o755)
  for f in ['wstunnel','wstunnel-get','wstunnel-quic']:
   p=self.pkg/f;p.write_text('#!/bin/sh\nexit 0\n');p.chmod(0o755)
  for f,text in [('systemctl',SYSTEMCTL),('id','#!/bin/sh\necho 0\n'),('chown','#!/bin/sh\nexit 0\n'),('sleep','#!/bin/sh\nexit 0\n')]:
   p=self.bin/f;p.write_text(text);p.chmod(0o755)
  self.env=dict(os.environ,PATH=str(self.bin)+':'+os.environ['PATH'],PROXY_TEST_ROOT=str(self.root))
  self.panel=self.root/'opt/amneziageo-server';self.panel.mkdir(parents=True)
  old=self.panel/'releases/old';old.mkdir(parents=True)
  (old/'wstunnel').write_text('old native')
  (self.panel/'web/old').mkdir(parents=True)
  (self.panel/'web/old/index.html').write_text('old')
  (self.panel/'current').symlink_to('releases/old');(self.panel/'wwwroot').symlink_to('web/old')
  self.etc=self.root/'etc/amneziageo-server';self.etc.mkdir(parents=True)
  self.original_env='Web__Path=/held\nProxy__Enabled=true\nProxy__Isolated=false\n'
  (self.etc/'server.env').write_text(self.original_env)
  self.fronts={'proxy-awg0.env':('PROXY_ARGS=ws://127.0.0.1:61001 --restrict-config /etc/amneziageo-server/proxy-awg0.yaml\n',0o600),
               'proxy-awg0.yaml':('restrictions: []\n',0o640),
               'proxy-awg1.env':('PROXY_ARGS=ws://127.0.0.1:61002\n',0o600),
               'proxy-awg2.env':('PROXY_ARGS=ws://127.0.0.1:61003\n',0o640),
               'proxy-awg4.env':('PROXY_ARGS=ws://127.0.0.1:61005\n',0o600)}
  for name,(text,mode) in self.fronts.items():
   p=self.etc/name;p.write_text(text);p.chmod(mode)
  (self.root/'etc/systemd/system').mkdir(parents=True)
  (self.root/'run/systemd/system').mkdir(parents=True)
  (self.root/'usr/local/bin').mkdir(parents=True)
  (self.root/'var/lib/amneziageo-server').mkdir(parents=True)
  self.original={PANEL:{'enabled':'enabled','active':'active','pid':1},LEGACY:{'enabled':'enabled','active':'active','pid':2},'amneziageo-proxy@awg1.service':{'enabled':'disabled','active':'active','pid':3},'amneziageo-proxy@awg2.service':{'enabled':'enabled','active':'inactive','pid':4},'amneziageo-proxy@awg3.service':{'enabled':'masked','active':'inactive','pid':5},'amneziageo-proxy@awg4.service':{'enabled':'enabled-runtime','active':'active','pid':6}}
  self.save(self.original)
 def tearDown(self):self.temp.cleanup()
 def save(self,data):(self.root/'services.json').write_text(json.dumps(data))
 def states(self):return json.loads((self.root/'services.json').read_text())
 def inject_failure(self,verb,name=''):(self.root/'failure.json').write_text(json.dumps({'verb':verb,'name':name}))
 def run_install(self,*args,ok=True):
  p=subprocess.run(['bash',str(self.pkg/'install.sh'),*args],env=self.env,capture_output=True,text=True)
  if ok:self.assertEqual(0,p.returncode,p.stdout+p.stderr)
  else:self.assertNotEqual(0,p.returncode,p.stdout+p.stderr)
  return p
 def original_restored(self):
  self.assertEqual('releases/old',os.readlink(self.panel/'current'))
  self.assertEqual(self.original_env,(self.etc/'server.env').read_text())
  self.fronts_restored()
  for name,want in self.original.items():
   actual=self.states()[name]
   self.assertEqual(want['active'],actual['active'],name)
   self.assertEqual(want['enabled'],actual['enabled'],name)
 def fronts_restored(self):
  for name,(text,mode) in self.fronts.items():
   p=self.etc/name
   self.assertEqual(text,p.read_text(),name)
   self.assertEqual(mode,p.stat().st_mode & 0o777,name)
 def upgraded(self):
  self.assertEqual('releases/new',os.readlink(self.panel/'current'))
  self.assertIn('Proxy__Isolated=true',(self.etc/'server.env').read_text())
  self.assertEqual('active',self.states()[DAEMON]['active'])
  for name in self.original:
   if name.startswith('amneziageo-proxy@'):
    self.assertEqual('inactive',self.states()[name]['active'])
    expected='masked' if self.original[name]['enabled']=='masked' else 'disabled'
    self.assertEqual(expected,self.states()[name]['enabled'])
 def test_upgrade_and_reinstall(self):
  self.run_install();self.upgraded()
  states=self.states();states[LEGACY]['active']='active';states[LEGACY]['enabled']='enabled';self.save(states)
  self.run_install();self.upgraded()
 def test_failed_new_daemon_restores_legacy(self):
  self.inject_failure('restart',DAEMON);self.run_install(ok=False);self.original_restored()
  self.assertFalse((self.root/'opt/amneziageo-proxy/current').exists())
 def test_failed_panel_start_restores_legacy(self):
  self.inject_failure('start',PANEL);self.run_install(ok=False);self.original_restored()
 def test_failed_legacy_disable_restores_all_states(self):
  self.inject_failure('disable',LEGACY);self.run_install(ok=False);self.original_restored()
 def test_manual_rollback_and_return(self):
  self.run_install();self.run_install('--rollback');self.original_restored()
  self.run_install('--rollback');self.upgraded()
 def test_failed_rollback_restores_new_release(self):
  self.run_install();self.inject_failure('start',PANEL);self.run_install('--rollback',ok=False);self.upgraded()
 def test_inactive_panel_keeps_active_legacy(self):
  states=self.states();states[PANEL]['active']='inactive';self.save(states)
  self.run_install(ok=False)
  self.assertEqual('active',self.states()[LEGACY]['active'])
  self.assertEqual('releases/old',os.readlink(self.panel/'current'))
 def test_failed_enumeration_does_not_stop_legacy(self):
  for verb in ['list-units','list-unit-files']:
   with self.subTest(verb=verb):
    self.inject_failure(verb);p=self.run_install(ok=False)
    self.assertIn('could not enumerate legacy proxy services',p.stderr)
    self.assertEqual('active',self.states()[LEGACY]['active'])
    self.assertEqual('releases/old',os.readlink(self.panel/'current'))
 def test_install_without_legacy_units(self):
  self.save({PANEL:self.original[PANEL]})
  for file in self.etc.glob('proxy-*'):file.unlink()
  self.run_install('--with-proxy')
  self.assertEqual('active',self.states()[DAEMON]['active'])
  self.assertEqual('releases/new',os.readlink(self.panel/'current'))
 def test_failed_environment_snapshot_keeps_legacy_running(self):
  environment=self.etc/'server.env';environment.chmod(0)
  try:
   p=self.run_install(ok=False)
   self.assertIn('could not snapshot proxy service ownership',p.stderr)
   self.assertEqual(self.original,self.states())
  finally:environment.chmod(0o600)
 def test_failed_daemon_snapshot_keeps_running_release(self):
  self.run_install()
  states=self.states()
  cp=shutil.which('cp')
  wrapper=self.bin/'cp'
  wrapper.write_text('#!/usr/bin/env python3\nimport os,sys\n'
   'if any("proxy-handover/transaction." in x and x.endswith("/amneziageo-proxy.service") for x in sys.argv[1:]):sys.exit(1)\n'
   f'os.execv({cp!r},[{cp!r}]+sys.argv[1:])\n')
  wrapper.chmod(0o755)
  p=self.run_install(ok=False)
  self.assertIn('could not snapshot proxy service ownership',p.stderr)
  self.assertEqual(states,self.states())
 def test_rollback_after_panel_withdraws_files(self):
  (self.root/'api-cleanup').touch()
  self.run_install()
  self.assertEqual([],list(self.etc.glob('proxy-*')))
  installer=self.root/'var/lib/amneziageo-server/proxy-handover/installer/install.sh'
  for _ in range(2):
   p=subprocess.run(['bash',str(installer),'--rollback'],env=self.env,capture_output=True,text=True)
   self.assertEqual(0,p.returncode,p.stdout+p.stderr)
   self.original_restored()
   p=subprocess.run(['bash',str(installer),'--rollback'],env=self.env,capture_output=True,text=True)
   self.assertEqual(0,p.returncode,p.stdout+p.stderr)
   self.upgraded()
   self.assertEqual([],list(self.etc.glob('proxy-*')))
 def test_failed_panel_after_withdrawal_restores_files(self):
  (self.root/'api-cleanup').touch()
  self.inject_failure('start',PANEL)
  self.run_install(ok=False);self.original_restored()
 def test_restore_removes_files_absent_from_snapshot(self):
  self.run_install()
  (self.etc/'proxy-new.env').write_text('new')
  (self.etc/'proxy-new.yaml').write_text('new')
  (self.etc/'unrelated.env').write_text('held')
  self.run_install('--rollback');self.original_restored()
  self.assertFalse((self.etc/'proxy-new.env').exists())
  self.assertFalse((self.etc/'proxy-new.yaml').exists())
  self.assertEqual('held',(self.etc/'unrelated.env').read_text())
 def test_rollback_preserves_configuration_symlinks(self):
  (self.etc/'held').write_text('linked rules')
  (self.etc/'proxy-linked.yaml').symlink_to('held')
  (self.etc/'proxy-missing.env').symlink_to('absent')
  self.run_install()
  (self.etc/'proxy-linked.yaml').unlink();(self.etc/'proxy-missing.env').unlink()
  self.run_install('--rollback')
  self.assertEqual('held',os.readlink(self.etc/'proxy-linked.yaml'))
  self.assertEqual('linked rules',(self.etc/'proxy-linked.yaml').read_text())
  self.assertEqual('absent',os.readlink(self.etc/'proxy-missing.env'))
 def test_older_snapshot_keeps_existing_files(self):
  self.run_install()
  profile=self.root/'var/lib/amneziageo-server/proxy-handover/releases/old'
  shutil.rmtree(profile/'fronts')
  self.run_install('--rollback');self.original_restored()
 def test_older_snapshot_missing_files_keeps_new_release_running(self):
  self.run_install()
  profile=self.root/'var/lib/amneziageo-server/proxy-handover/releases/old'
  shutil.rmtree(profile/'fronts')
  (self.etc/'proxy-awg0.env').unlink()
  p=self.run_install('--rollback',ok=False)
  self.assertIn('configuration backup is missing',p.stderr)
  self.upgraded()
  self.assertEqual('active',self.states()[PANEL]['active'])
 def test_snapshot_records_final_panel_writes(self):
  (self.root/'stop-update').touch()
  self.run_install()
  (self.etc/'proxy-awg0.yaml').unlink()
  self.run_install('--rollback')
  self.assertEqual('updated before panel exit\n',(self.etc/'proxy-awg0.yaml').read_text())
 def test_unrelated_environment_survives_rollback(self):
  self.run_install()
  with (self.etc/'server.env').open('a') as f:f.write('Web__Name=changed\n')
  self.run_install('--rollback')
  self.assertIn('Web__Name=changed',(self.etc/'server.env').read_text())
  self.assertIn('Proxy__Isolated=false',(self.etc/'server.env').read_text())

 def test_persistent_rollback_survives_old_payload(self):
  self.run_install()
  installer=self.root/'var/lib/amneziageo-server/proxy-handover/installer/install.sh'
  for _ in range(2):
   p=subprocess.run(['bash',str(installer),'--rollback'],env=self.env,capture_output=True,text=True)
   self.assertEqual(0,p.returncode,p.stdout+p.stderr)
  self.upgraded()
 def test_same_release_repairs_installer_payload(self):
  self.run_install()
  target=self.panel/'releases/new'
  (target/'install.sh').write_text('old installer')
  (target/'proxy-handover.sh').unlink()
  self.run_install()
  self.assertEqual((self.pkg/'install.sh').read_bytes(),(target/'install.sh').read_bytes())
  self.assertTrue((target/'proxy-handover.sh').exists())
 def test_failed_proxy_stop_does_not_restore_legacy(self):
  self.run_install();self.inject_failure('stop',DAEMON)
  profile=self.root/'var/lib/amneziageo-server/proxy-handover/releases/old'
  command='source "$1"; point() { ln -sfn "$2" "$1.next"; mv -Tf "$1.next" "$1"; }; proxy_restore "$2"'
  p=subprocess.run(['bash','-c',command,'test',str(self.pkg/'proxy-handover.sh'),str(profile)],env=self.env,capture_output=True,text=True)
  self.assertNotEqual(0,p.returncode)
  self.assertEqual('active',self.states()[DAEMON]['active'])
  self.assertEqual('inactive',self.states()[LEGACY]['active'])
  self.assertIn('Proxy__Isolated=true',(self.etc/'server.env').read_text())
 def test_standalone_failure_preserves_installed_daemon(self):
  self.run_install('--proxy-only')
  proxy=self.root/'opt/amneziageo-proxy'
  self.assertEqual('releases/new',os.readlink(proxy/'current'))
  self.inject_failure('restart',DAEMON)
  (self.pkg/'release').write_text('newer\n')
  self.run_install('--proxy-only',ok=False)
  self.assertEqual('releases/new',os.readlink(proxy/'current'))
  self.assertEqual('active',self.states()[DAEMON]['active'])
  self.assertEqual('active',self.states()[LEGACY]['active'])

 def test_failed_panel_start_restores_disabled_autostart(self):
  self.original[PANEL]['enabled']='disabled';self.save(self.original)
  self.inject_failure('start',PANEL);self.run_install(ok=False);self.original_restored()

if __name__=='__main__':unittest.main(verbosity=2)
