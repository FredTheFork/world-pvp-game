const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { requireGameServer } = require('../../_lib/server-auth');
const { allowMethods, routeParam, sendJson } = require('../../_lib/http');
const { rest } = require('../../_lib/supabase');
const { getMatchId } = require('../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET'])) return;
  requireGameServer(req);
  const matchId = getMatchId({ query: { id: routeParam(req, 'matchId') } });
  const matches = await rest(
    'matches?select=id,state,is_locked,maximum_players,end_requested_at,state_version&id=eq.' +
    encodeURIComponent(matchId) + '&limit=1'
  );
  if (!Array.isArray(matches) || matches.length !== 1) throw new ApiError(404, 'MATCH_NOT_FOUND', 'The match was not found.');
  const kicked = await rest(
    'match_members?select=user_id&match_id=eq.' + encodeURIComponent(matchId) + '&status=eq.KICKED'
  );
  sendJson(res, 200, {
    matchId,
    state: matches[0].state,
    isLocked: matches[0].is_locked,
    maximumPlayers: matches[0].maximum_players,
    stateVersion: matches[0].state_version,
    endRequested: Boolean(matches[0].end_requested_at),
    kickedUserIds: Array.isArray(kicked) ? kicked.map((row) => row.user_id) : []
  });
});
