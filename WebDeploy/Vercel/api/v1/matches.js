const { endpoint } = require('../_lib/handler');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest, rpc } = require('../_lib/supabase');
const { ApiError } = require('../_lib/errors');
const { createInviteCode, sha256Hex, validateMatchDraft } = require('../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET', 'POST'])) return;
  const user = await requireUser(req);
  if (req.method === 'GET') {
    await enforceRateLimit(req, { bucket: 'api', userId: user.id, limit: 90, windowSeconds: 60, ipLimit: 180 });
    const rows = await rest(
      'match_members?select=role,status,joined_at,matches(id,state,centre_latitude,centre_longitude,' +
      'centre_altitude_meters,radius_meters,maximum_players,created_at,finished_at)&user_id=eq.' +
      encodeURIComponent(user.id) + '&order=joined_at.desc&limit=50'
    );
    sendJson(res, 200, { matches: Array.isArray(rows) ? rows : [] });
    return;
  }
  await enforceRateLimit(req, {
    bucket: 'match_create', userId: user.id,
    limit: 3, windowSeconds: 3600, ipLimit: 6, ipWindowSeconds: 3600
  });

  const body = await readJson(req, 4096);
  const draft = validateMatchDraft(body);
  const joinCode = createInviteCode();
  const result = await rpc('create_match', {
    p_host_user_id: user.id,
    p_centre_latitude: draft.latitude,
    p_centre_longitude: draft.longitude,
    p_centre_altitude_meters: draft.altitude,
    p_radius_meters: draft.radiusMeters,
    p_maximum_players: draft.maximumPlayers,
    p_join_code_hash: sha256Hex(joinCode)
  });
  const match = Array.isArray(result) ? result[0] : result;
  if (!match || !match.id) {
    throw new ApiError(503, 'MATCH_CREATE_FAILED', 'The match could not be created. Try again shortly.');
  }
  sendJson(res, 201, {
    match: {
      id: match.id,
      state: match.state,
      created_at: match.created_at,
      radius_meters: match.radius_meters,
      maximum_players: match.maximum_players
    },
    invite_code: joinCode,
    message: 'Match record created. It remains CREATING until a trusted dedicated game server allocates and opens the lobby.'
  });
});
