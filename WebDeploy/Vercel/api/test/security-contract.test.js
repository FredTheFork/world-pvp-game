const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../../..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');

test('Supabase migration stores compact gameplay metadata and locks exposed tables behind the trusted API', () => {
  const sql = read('supabase/migrations/202610060001_phase10_core.sql');
  for (const table of ['users', 'statistics', 'matches', 'match_members', 'match_results', 'sessions', 'reports', 'bans', 'user_blocks', 'user_mutes', 'chat_messages', 'match_events']) {
    assert.match(sql, new RegExp("'" + table + "'"));
  }
  assert.match(sql, /alter table public\.%I enable row level security/);
  assert.match(sql, /revoke all on table public\.%I from anon, authenticated/);
  assert.match(sql, /join_code_hash text not null unique/);
  assert.match(sql, /connection_ticket_hash text unique/);
  assert.match(sql, /reconnect_token_hash text unique/);
  assert.match(sql, /grant execute on function public\.record_match_results\(uuid, jsonb\) to service_role/);
  assert.doesNotMatch(sql, /google[_ ]maps[_ ]tiles|photorealistic[_ ]mesh|map[_ ]tile[_ ]blob/i);
});

test('client runtime uses the Vercel API and contains no Supabase/server privileged key names', () => {
  const client = read('Assets/WorldPvp/Runtime/Backend/PhaseTenAccountClient.cs');
  assert.match(client, /\/api\/v1/);
  assert.match(client, /auth\/refresh/);
  assert.match(client, /auth\/signout/);
  assert.match(client, /PlayerPrefs\.SetString\(RefreshTokenKey/);
  assert.doesNotMatch(client, /SUPABASE_SECRET_KEY|SUPABASE_SERVICE_ROLE_KEY|WORLD_PVP_SERVER_TOKEN|postgresql:\/\//);
});

test('reconnect redemption restores eliminated sessions as spectators and checkpoint endpoints stay server-only', () => {
  const sql = read('supabase/migrations/202610060001_phase10_core.sql');
  const sessions = read('WebDeploy/Vercel/api/v1/server/sessions.js');
  const checkpoint = read('WebDeploy/Vercel/api/v1/server/checkpoint.js');
  assert.match(sql, /create or replace function public\.server_mark_eliminated/);
  assert.match(sql, /s\.status in \('DISCONNECTED', 'ELIMINATED'\)/);
  assert.match(sql, /mm\.status in \('DISCONNECTED', 'ELIMINATED'\)/);
  assert.match(sql, /and status = 'DISCONNECTED';/);
  assert.match(sessions, /action === 'MARK_ELIMINATED'/);
  assert.match(checkpoint, /requireGameServer\(req\)/);
  assert.match(checkpoint, /server_save_player_snapshots/);
});

test('dedicated-server-only endpoints require the server token and user lifecycle APIs do not accept client state transitions', () => {
  const lifecycle = read('WebDeploy/Vercel/api/v1/server/lifecycle.js');
  const serverAuth = read('WebDeploy/Vercel/api/_lib/server-auth.js');
  const createMatch = read('WebDeploy/Vercel/api/v1/matches.js');
  assert.match(lifecycle, /requireGameServer\(req\)/);
  assert.match(serverAuth, /WORLD_PVP_SERVER_TOKEN/);
  assert.match(createMatch, /rpc\('create_match'/);
  assert.doesNotMatch(createMatch, /transition_match|p_to_state/);
});
