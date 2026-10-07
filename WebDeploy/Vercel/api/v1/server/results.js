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
  const body = await readJson(req, 16384);
  const matchId = getMatchId({ query: { id: body.matchId } });
  if (!Array.isArray(body.results) || body.results.length < 1 || body.results.length > 32) {
    throw new ApiError(400, 'INVALID_RESULTS', 'A match result must include 1–32 player records.');
  }
  const results = body.results.map((item) => {
    if (!item || typeof item !== 'object' || !UUID.test(String(item.userId || '')) ||
        !Number.isInteger(item.kills) || item.kills < 0 || item.kills > 10000 ||
        !Number.isInteger(item.deaths) || item.deaths < 0 || item.deaths > 10000 ||
        !Number.isInteger(item.placement) || item.placement < 1 || item.placement > 32 ||
        (item.score !== undefined && (!Number.isInteger(item.score) || item.score < -1000000 || item.score > 1000000)) ||
        typeof item.won !== 'boolean') {
      throw new ApiError(400, 'INVALID_PLAYER_RESULT', 'One or more player result fields are invalid.');
    }
    return {
      user_id: item.userId,
      kills: item.kills,
      deaths: item.deaths,
      placement: item.placement,
      won: item.won,
      score: item.score || 0
    };
  });
  if (new Set(results.map((item) => item.user_id)).size !== results.length ||
      results.filter((item) => item.won).length > 1) {
    throw new ApiError(400, 'INVALID_RESULTS', 'Player IDs must be unique and there can be at most one winner.');
  }
  const savedCount = await rpc('record_match_results', { p_match_id: matchId, p_results: results });
  sendJson(res, 200, { matchId, savedPlayers: Number(savedCount), state: 'FINISHED' });
});
