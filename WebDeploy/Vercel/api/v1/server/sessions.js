const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { requireGameServer } = require('../../_lib/server-auth');
const { allowMethods, readJson, sendJson } = require('../../_lib/http');
const { rpc } = require('../../_lib/supabase');
const { createOpaqueToken, sha256Hex } = require('../../_lib/domain');

function rowOf(data) { return Array.isArray(data) ? data[0] || null : data; }

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  requireGameServer(req);
  const body = await readJson(req, 20480);
  const action = String(body.action || '').toUpperCase();

  if (action === 'CONSUME_JOIN_TICKET') {
    if (typeof body.ticket !== 'string' || body.ticket.length < 32 || body.ticket.length > 256) {
      throw new ApiError(400, 'INVALID_TICKET', 'The connection ticket is invalid.');
    }
    const row = rowOf(await rpc('consume_connection_ticket', { p_ticket_hash: sha256Hex(body.ticket) }));
    if (!row || !row.session_id || !row.match_id || !row.user_id) {
      throw new ApiError(401, 'TICKET_EXPIRED_OR_USED', 'The connection ticket expired or was already used.');
    }
    sendJson(res, 200, { sessionId: row.session_id, matchId: row.match_id, userId: row.user_id });
    return;
  }

  if (action === 'REDEEM_RECONNECT_TICKET') {
    if (typeof body.reconnectToken !== 'string' || body.reconnectToken.length < 32 || body.reconnectToken.length > 256) {
      throw new ApiError(400, 'INVALID_TICKET', 'The reconnect ticket is invalid.');
    }
    const nextToken = createOpaqueToken(32);
    const row = rowOf(await rpc('redeem_reconnect_ticket', {
      p_reconnect_token_hash: sha256Hex(body.reconnectToken),
      p_new_reconnect_token_hash: sha256Hex(nextToken)
    }));
    if (!row || !row.session_id || !row.match_id || !row.user_id) {
      throw new ApiError(401, 'RECONNECT_UNAVAILABLE', 'This player session cannot be reconnected.');
    }
    sendJson(res, 200, {
      sessionId: row.session_id,
      matchId: row.match_id,
      userId: row.user_id,
      rotatedReconnectToken: nextToken,
      playerState: row.last_server_state || null
    });
    return;
  }

  if (action === 'MARK_ELIMINATED' || action === 'MARK_DISCONNECTED') {
    const sessionId = String(body.sessionId || '');
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(sessionId)) {
      throw new ApiError(400, 'INVALID_SESSION_ID', 'The player session ID is invalid.');
    }
    const snapshot = body.playerState === undefined ? null : body.playerState;
    if (snapshot !== null && (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot) ||
        Buffer.byteLength(JSON.stringify(snapshot), 'utf8') > 16384)) {
      throw new ApiError(400, 'INVALID_PLAYER_STATE', 'The reconnect snapshot is invalid or too large.');
    }
    const procedure = action === 'MARK_ELIMINATED' ? 'server_mark_eliminated' : 'server_mark_disconnected';
    const result = await rpc(procedure, { p_session_id: sessionId, p_snapshot: snapshot });
    const updated = result === true || (result && result[procedure] === true);
    if (!updated) throw new ApiError(404, 'SESSION_NOT_FOUND', 'The player session was not found.');
    // Reconnect credentials are hashed before storage and are never logged or returned here.
    sendJson(res, 200, action === 'MARK_ELIMINATED'
      ? { sessionId, eliminated: true }
      : { sessionId, disconnected: true });
    return;
  }

  throw new ApiError(400, 'INVALID_SESSION_ACTION', 'Choose a supported dedicated-server session action.');
});
