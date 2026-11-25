#!/usr/bin/env python3
"""Checks release-bound database rollback with isolated SQLite databases."""
import hashlib
import os
import re
import sqlite3
import subprocess
import tempfile
import unittest
from pathlib import Path
import test_proxy_handover as proxy_fixture
PANEL = proxy_fixture.PANEL

DEPLOY = Path(__file__).resolve().parents[1]

def database(path, modern=False, value='old-key'):
    with sqlite3.connect(path) as db:
        db.execute('CREATE TABLE ' + ('RoutingEntries' if modern else 'Rules') + ' (Id INTEGER PRIMARY KEY, Value TEXT)')
        db.execute('CREATE TABLE Canary (Id INTEGER PRIMARY KEY, Value TEXT)')
        db.execute('INSERT INTO Canary VALUES (1,?)', (value,))

def inspect(path):
    with sqlite3.connect(path) as db:
        return set(row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")), db.execute('SELECT Value FROM Canary WHERE Id=1').fetchone()[0]

def migrate(path):
    with sqlite3.connect(path) as db:
        db.execute('DROP TABLE Rules')
        db.execute('CREATE TABLE RoutingEntries (Id INTEGER PRIMARY KEY, Value TEXT)')
        db.execute("UPDATE Canary SET Value='new-change'")

class NativeDatabaseTests(unittest.TestCase):
    def setUp(self):
        self.f = proxy_fixture.ProxyHandoverTests(methodName='runTest')
        self.f.setUp()
        self.addCleanup(self.f.tearDown)
        self.data = self.f.root/'var/lib/amneziageo-server'
        self.db = self.data/'server.db'
        database(self.db)

    def upgrade(self):
        self.f.run_install()
        migrate(self.db)

    def test_matching_schema_and_current_changes_survive_round_trip(self):
        self.upgrade()
        self.f.run_install('--rollback')
        self.assertEqual(({'Rules','Canary'}, 'old-key'), inspect(self.db))
        self.f.run_install('--rollback')
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'), inspect(self.db))

    def test_failed_downgrade_restores_current_schema_and_changes(self):
        self.upgrade()
        self.f.inject_failure('start', PANEL)
        self.f.run_install('--rollback', ok=False)
        self.assertEqual('releases/new', os.readlink(self.f.panel/'current'))
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'), inspect(self.db))
        self.assertEqual('active', self.f.states()[PANEL]['active'])

    def test_missing_matching_backup_refuses_before_service_stop(self):
        self.upgrade()
        key = hashlib.sha256(b'native:releases/old').hexdigest()
        (self.data/'rollback'/key).unlink()
        calls = (self.f.root/'calls.log').read_text()
        self.f.run_install('--rollback', ok=False)
        self.assertEqual(calls, (self.f.root/'calls.log').read_text())
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'), inspect(self.db))

    def test_wrong_build_binding_is_not_used(self):
        self.upgrade()
        key = hashlib.sha256(b'native:releases/old').hexdigest()
        bound = self.data/'rollback'/key
        bound.write_text(bound.read_text().replace('native:releases/old', 'native:releases/other'))
        self.f.run_install('--rollback', ok=False)
        self.assertEqual('releases/new', os.readlink(self.f.panel/'current'))
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'), inspect(self.db))

    def test_committed_wal_is_included_in_the_bound_snapshot(self):
        connection = sqlite3.connect(self.db)
        connection.execute('PRAGMA journal_mode=WAL')
        connection.execute("UPDATE Canary SET Value='committed-in-wal'")
        connection.commit()
        self.f.run_install()
        connection.close()
        migrate(self.db)
        self.f.run_install('--rollback')
        self.assertEqual(({'Rules','Canary'}, 'committed-in-wal'), inspect(self.db))

class DockerDatabaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='docker-db-rollback-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.data = self.root/'data';self.data.mkdir()
        self.db = self.data/'server.db'
        database(self.db)
        self.env = dict(os.environ, DB_TEST_ROOT=str(self.root))
        self.helper = DEPLOY/'database-handover.sh'
        self.run_shell('db_save "$dir" docker:sha256:old')
        migrate(self.db)
        (self.root/'tag').write_text('2.0.0.0')
        (self.root/'running-image').write_text('2.0.0.0')
        (self.root/'sidecar-state').write_text('original-container')
        (self.root/'mock-start.py').write_text("""import os, sqlite3
from pathlib import Path
root=Path(os.environ['DB_TEST_ROOT'])
if (root/'running-image').read_text()=='2.0.0.0':
    db=sqlite3.connect(root/'data/server.db')
    if db.execute("select count(*) from sqlite_master where name='Rules'").fetchone()[0]:
        db.execute('alter table Rules rename to RoutingEntries')
        db.execute("update Canary set Value='new-started'")
        db.commit()
""")
        text = (DEPLOY/'amneziageo-server').read_text()
        names = ['docker_rollback_failed','rollback_panel']
        self.functions = '\n'.join(re.search(r'^'+name+r'\(\) \{.*?^\}', text, re.S|re.M).group(0) for name in names)

    def run_shell(self, body):
        script = 'source "$1"; dir="$DB_TEST_ROOT/data"; '+body
        result = subprocess.run(['bash','-c',script,'test',str(self.helper)],env=self.env,text=True,capture_output=True)
        self.assertEqual(0,result.returncode,result.stdout+result.stderr)
        return result.stdout.strip()

    def rollback(self, success=True):
        prelude = r'''source "$1"
dir="$DB_TEST_ROOT/data"
base="$DB_TEST_ROOT/opt"
need_panel() { return 0; }
host_only() { return 0; }
confirm() { return 0; }
load_database_handover() { return 0; }
kind() { echo docker; }
container_id() { echo panel; }
image_tag() { cat "$DB_TEST_ROOT/tag"; }
write_tag() {
  if [ -f "$DB_TEST_ROOT/fail-tag" ] && [ "$1" = 1.0.0.0 ]; then return 1; fi
  printf '%s' "$1" > "$DB_TEST_ROOT/tag"
}
panel_repository() { echo registry/panel; }
data_dir() { printf '%s/data' "$DB_TEST_ROOT"; }
bad() { echo "$*" >&2; }
good() { echo "$*"; }
wait_panel() { if [ -f "$DB_TEST_ROOT/fail" ] && [ "$(image_tag)" = 1.0.0.0 ]; then rm "$DB_TEST_ROOT/fail"; return 1; fi; }
dcompose() {
  echo "$*" >> "$DB_TEST_ROOT/calls"
  if [ "$1" = up ]; then
    if [[ " $* " == *' --force-recreate '* ]]; then
      cp "$DB_TEST_ROOT/tag" "$DB_TEST_ROOT/running-image"
      if [[ " $* " != *' --no-deps panel '* ]]; then echo recreated > "$DB_TEST_ROOT/sidecar-state"; fi
    fi
    python3 "$DB_TEST_ROOT/mock-start.py"
  fi
}
docker() {
  if [ "$1" = images ]; then printf '1.0.0.0\n2.0.0.0\n'
  elif [ "$1" = image ]; then echo sha256:old
  else echo sha256:new; fi
}
'''
        result = subprocess.run(['bash','-c',prelude+self.functions+'\nrollback_panel','test',str(self.helper)],env=self.env,text=True,capture_output=True)
        if success:self.assertEqual(0,result.returncode,result.stdout+result.stderr)
        else:self.assertNotEqual(0,result.returncode,result.stdout+result.stderr)
        return result

    def test_downgrade_restores_exact_image_snapshot_and_preserves_current(self):
        self.rollback()
        self.assertEqual('1.0.0.0', (self.root/'tag').read_text())
        self.assertEqual('1.0.0.0', (self.root/'running-image').read_text())
        self.assertEqual('original-container',(self.root/'sidecar-state').read_text())
        self.assertEqual(({'Rules','Canary'}, 'old-key'), inspect(self.db))
        self.run_shell('saved=$(db_find "$dir" docker:sha256:new); db_restore "$dir" "$saved"')
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'), inspect(self.db))

    def test_failed_image_switch_restores_current_database_and_tag(self):
        (self.root/'fail').touch()
        self.rollback(success=False)
        self.assertIn('stop panel', (self.root/'calls').read_text())
        self.assertEqual('2.0.0.0',(self.root/'tag').read_text())
        self.assertEqual('2.0.0.0',(self.root/'running-image').read_text())
        self.assertEqual('original-container',(self.root/'sidecar-state').read_text())
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'),inspect(self.db))

    def test_failed_tag_write_restores_current_database_before_restart(self):
        (self.root/'fail-tag').touch()
        self.rollback(success=False)
        self.assertIn('stop panel', (self.root/'calls').read_text())
        self.assertEqual('2.0.0.0',(self.root/'tag').read_text())
        self.assertEqual('2.0.0.0',(self.root/'running-image').read_text())
        self.assertEqual('original-container',(self.root/'sidecar-state').read_text())
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'),inspect(self.db))

    def test_failed_atomic_tag_rename_leaves_the_original_environment(self):
        text = (DEPLOY/'amneziageo-server').read_text()
        function = re.search(r'^write_tag\(\) \{.*?^\}', text, re.S|re.M).group(0)
        env_file=self.root/'.env'
        original='OTHER=preserved\nAMNEZIAGEO_TAG=2.0.0.0\n'
        env_file.write_text(original)
        body='docker_dir="$DB_TEST_ROOT"; mv() { return 1; }; '+function+'\nwrite_tag 1.0.0.0'
        result=subprocess.run(['bash','-c',body],env=self.env,text=True,capture_output=True)
        self.assertNotEqual(0,result.returncode,result.stdout+result.stderr)
        self.assertEqual(original,env_file.read_text())
        self.assertEqual([env_file],list(self.root.glob('.env*')))

    def test_missing_image_binding_keeps_current_image_running(self):
        key=hashlib.sha256(b'docker:sha256:old').hexdigest()
        (self.data/'rollback'/key).unlink()
        self.rollback(success=False)
        self.assertFalse((self.root/'calls').exists())
        self.assertEqual('2.0.0.0',(self.root/'tag').read_text())
        self.assertEqual('2.0.0.0',(self.root/'running-image').read_text())
        self.assertEqual('original-container',(self.root/'sidecar-state').read_text())
        self.assertEqual(({'RoutingEntries','Canary'}, 'new-change'),inspect(self.db))

if __name__ == '__main__':
    unittest.main(verbosity=2)
