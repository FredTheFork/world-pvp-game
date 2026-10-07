const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { requireGameServer } = require('../../_lib/server-auth');
const { allowMethods, readJson, sendJson } = require('../../_lib/http');
const { rpc } = require('../../_lib/supabase');
const { getMatchId } = require('../../_lib/domain');

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  requireGameServer(req);
  const body = await readJson(req, 512 * 1024);
  const matchId = getMatchId({ query: { id: body.matchId } });
  if (!Array.isArray(body.snapshots) || body.snapshots.length > 32) {
    throw new ApiError(400, 'INVALID_SNAPSHOTS', 'A checkpoint must include no more than 32 player snapshots.');
  }
  const seen = new Set();
  const snapshots = body.snapshots.map((item) => {
    if (!item || !UUID.test(String(item.sessionId || '')) || !item.playerState ||
        typeof item.playerState !== 'object' || Array.isArray(item.playerState)) {
      throw new ApiError(400, 'INVALID_SNAPSHOT', 'One or more player snapshots are invalid.');
    }
    const key = item.sessionId.toLowerCase();
    if (seen.has(key)) throw new ApiError(400, 'DUPLICATE_SNAPSHOT', 'A session can only appear once per checkpoint.');
    seen.add(key);
    if (Buffer.byteLength(JSON.stringify(item.playerState), 'utf8') > 16384) {
      throw new ApiError(400, 'SNAPSHOT_TOO_LARGE', 'Each player snapshot must be 16 KB or smaller.');
    }
    return { session_id: item.sessionId, player_state: item.playerState };
  });
  const saved = await rpc('server_save_player_snapshots', { p_match_id: matchId, p_snapshots: snapshots });
  sendJson(res, 200, { matchId, savedSessions: Number(saved) });
});
