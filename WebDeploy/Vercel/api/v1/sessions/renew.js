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
  await enforceRateLimit(req, { bucket: 'session_renew', userId: user.id, limit: 12, windowSeconds: 60, ipLimit: 30 });
  const body = await readJson(req, 2048);
  const sessionId = String(body.sessionId || '');
  const oldToken = String(body.reconnectToken || '');
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(sessionId) ||
      oldToken.length < 32 || oldToken.length > 256) {
    throw new ApiError(400, 'INVALID_SESSION_TOKEN', 'The session token is invalid.');
  }
  const nextToken = createOpaqueToken(32);
  const expiry = await rpc('renew_reconnect_ticket', {
    p_user_id: user.id,
    p_session_id: sessionId,
    p_old_reconnect_token_hash: sha256Hex(oldToken),
    p_new_reconnect_token_hash: sha256Hex(nextToken)
  });
  sendJson(res, 200, { sessionId, reconnectToken: nextToken, expiresAt: expiry });
});
