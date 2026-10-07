const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { requireUser } = require('../../_lib/auth');
const { allowMethods, sendJson, routeParam } = require('../../_lib/http');
const { enforceRateLimit } = require('../../_lib/rate-limit');
const { rest } = require('../../_lib/supabase');
const { getMatchId } = require('../../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, { bucket: 'api', userId: user.id, limit: 90, windowSeconds: 60, ipLimit: 180 });
  const matchId = getMatchId({ query: { id: routeParam(req, 'id') } });
  const memberRows = await rest(
    'match_members?select=role,status&match_id=eq.' + encodeURIComponent(matchId) +
    '&user_id=eq.' + encodeURIComponent(user.id) + '&limit=1'
  );
  if (!Array.isArray(memberRows) || memberRows.length === 0) {
    throw new ApiError(403, 'MATCH_ACCESS_DENIED', 'This account is not a member of that match.');
  }
  const matches = await rest(
    'matches?select=id,host_user_id,state,centre_latitude,centre_longitude,centre_altitude_meters,' +
    'radius_meters,maximum_players,is_locked,created_at,updated_at,state_version,end_requested_at&id=eq.' +
    encodeURIComponent(matchId) + '&limit=1'
  );
  if (!Array.isArray(matches) || matches.length !== 1) {
    throw new ApiError(404, 'MATCH_NOT_FOUND', 'The match was not found.');
  }
  const rows = await rest(
    'match_members?select=user_id,role,status,joined_at,users(username,avatar_url)&match_id=eq.' +
    encodeURIComponent(matchId) + '&order=joined_at.asc'
  );
  const match = matches[0];
  sendJson(res, 200, {
    match: {
      id: match.id,
      state: match.state,
      hostUserId: match.host_user_id,
      centre: {
        latitude: match.centre_latitude,
        longitude: match.centre_longitude,
        altitudeMeters: match.centre_altitude_meters
      },
      radiusMeters: match.radius_meters,
      maximumPlayers: match.maximum_players,
      isLocked: match.is_locked,
      stateVersion: match.state_version,
      createdAt: match.created_at,
      updatedAt: match.updated_at,
      endRequested: Boolean(match.end_requested_at)
    },
    membership: memberRows[0],
    players: Array.isArray(rows) ? rows.map((row) => ({
      userId: row.user_id,
      role: row.role,
      status: row.status,
      joinedAt: row.joined_at,
      username: row.users && row.users.username,
      avatarUrl: row.users && row.users.avatar_url
    })) : []
  });
});
