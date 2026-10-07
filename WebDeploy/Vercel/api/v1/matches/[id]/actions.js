const { endpoint } = require('../../../_lib/handler');
const { ApiError } = require('../../../_lib/errors');
const { requireUser } = require('../../../_lib/auth');
const { allowMethods, readJson, sendJson, routeParam } = require('../../../_lib/http');
const { enforceRateLimit } = require('../../../_lib/rate-limit');
const { rpc } = require('../../../_lib/supabase');
const { getMatchId } = require('../../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, {
    bucket: 'host_action', userId: user.id, limit: 20, windowSeconds: 60, ipLimit: 60
  });
  const matchId = getMatchId({ query: { id: routeParam(req, 'id') } });
  const body = await readJson(req, 2048);
  const action = String(body.action || '').toUpperCase();
  const allowed = ['KICK', 'LOCK_LOBBY', 'UNLOCK_LOBBY', 'SET_MAX_PLAYERS', 'END_MATCH'];
  if (!allowed.includes(action)) throw new ApiError(400, 'INVALID_HOST_ACTION', 'That host action is not supported.');

  let targetUserId = null;
  if (action === 'KICK') {
    targetUserId = typeof body.targetUserId === 'string' ? body.targetUserId : '';
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(targetUserId)) {
      throw new ApiError(400, 'INVALID_TARGET', 'Choose a valid player to remove.');
    }
  }
  let maximumPlayers = null;
  if (action === 'SET_MAX_PLAYERS') {
    maximumPlayers = Number(body.maximumPlayers);
    if (!Number.isInteger(maximumPlayers) || maximumPlayers < 2 || maximumPlayers > 32) {
      throw new ApiError(400, 'INVALID_PLAYER_LIMIT', 'Maximum players must be between 2 and 32.');
    }
  }

  const result = await rpc('host_match_action', {
    p_host_user_id: user.id,
    p_match_id: matchId,
    p_action: action,
    p_target_user_id: targetUserId,
    p_new_maximum_players: maximumPlayers
  });
  sendJson(res, 200, { result });
});
