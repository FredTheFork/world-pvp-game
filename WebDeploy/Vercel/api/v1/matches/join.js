const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { requireUser } = require('../../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../../_lib/http');
const { enforceRateLimit } = require('../../_lib/rate-limit');
const { rpc } = require('../../_lib/supabase');
const { createOpaqueToken, sha256Hex } = require('../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, {
    bucket: 'match_join', userId: user.id,
    limit: 10, windowSeconds: 60, ipLimit: 20
  });

  const body = await readJson(req, 2048);
  const joinCode = typeof body.inviteCode === 'string' ? body.inviteCode.trim().toUpperCase() : '';
  if (!/^[A-Z0-9]{16,32}$/.test(joinCode)) {
    throw new ApiError(400, 'INVALID_INVITE_CODE', 'Enter a valid opaque invite code.');
  }
  const connectionTicket = createOpaqueToken(32);
  const reconnectToken = createOpaqueToken(32);
  const result = await rpc('join_match_by_code', {
    p_user_id: user.id,
    p_join_code_hash: sha256Hex(joinCode),
    p_connection_ticket_hash: sha256Hex(connectionTicket),
    p_reconnect_token_hash: sha256Hex(reconnectToken)
  });
  const match = Array.isArray(result) ? result[0] : result;
  if (!match || !match.match_id || !match.session_id) {
    throw new ApiError(503, 'MATCH_JOIN_FAILED', 'The match could not be joined. Try again shortly.');
  }
  sendJson(res, 200, {
    match: {
      id: match.match_id,
      state: match.state,
      centre: {
        latitude: Number(match.centre_latitude),
        longitude: Number(match.centre_longitude),
        altitudeMeters: Number(match.centre_altitude_meters)
      },
      radiusMeters: Number(match.radius_meters),
      maximumPlayers: Number(match.maximum_players)
    },
    session: {
      id: match.session_id,
      connectionTicket,
      connectionTicketExpiresAt: match.ticket_expires_at,
      reconnectToken,
      reconnectTokenExpiresInSeconds: 600
    },
    message: 'Join accepted. A trusted game server must consume the one-time connection ticket before networking begins.'
  });
});
