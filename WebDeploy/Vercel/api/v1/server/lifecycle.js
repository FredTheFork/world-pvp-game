const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { allowMethods, readJson, sendJson } = require('../../_lib/http');
const { requireGameServer } = require('../../_lib/server-auth');
const { rpc } = require('../../_lib/supabase');
const { canTransition, getMatchId } = require('../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  requireGameServer(req);
  const body = await readJson(req, 2048);
  const matchId = getMatchId({ query: { id: body.matchId } });
  const toState = String(body.toState || '').toUpperCase();
  if (!['CREATING', 'LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE', 'FINISHED', 'CLOSED'].includes(toState)) {
    throw new ApiError(400, 'INVALID_MATCH_STATE', 'The requested match state is not recognized.');
  }
  const currentState = String(body.expectedFrom || '').toUpperCase();
  if (currentState && !canTransition(currentState, toState)) {
    throw new ApiError(409, 'INVALID_MATCH_TRANSITION', 'That match state transition is not allowed.');
  }
  const match = await rpc('transition_match', { p_match_id: matchId, p_to_state: toState });
  sendJson(res, 200, { matchId: match.id, state: match.state, stateVersion: match.state_version });
});
