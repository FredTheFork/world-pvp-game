const { endpoint } = require('../_lib/handler');
const { ApiError } = require('../_lib/errors');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest } = require('../_lib/supabase');
const { validateAvatarUrl, validateUsername } = require('../_lib/domain');

async function ensureAccountRows(user) {
  const id = encodeURIComponent(user.id);
  let profiles = await rest('users?select=id,username,avatar_url,created_at,updated_at&id=eq.' + id);
  if (!Array.isArray(profiles) || profiles.length === 0) {
    await rest('users?on_conflict=id', {
      method: 'POST', body: { id: user.id }, prefer: 'resolution=merge-duplicates,return=minimal'
    });
    profiles = await rest('users?select=id,username,avatar_url,created_at,updated_at&id=eq.' + id);
  }
  let statistics = await rest('statistics?select=user_id,matches_played,kills,deaths,wins,updated_at&user_id=eq.' + id);
  if (!Array.isArray(statistics) || statistics.length === 0) {
    await rest('statistics?on_conflict=user_id', {
      method: 'POST', body: { user_id: user.id }, prefer: 'resolution=merge-duplicates,return=minimal'
    });
    statistics = await rest('statistics?select=user_id,matches_played,kills,deaths,wins,updated_at&user_id=eq.' + id);
  }
  return {
    profile: Array.isArray(profiles) ? profiles[0] || null : null,
    statistics: Array.isArray(statistics) ? statistics[0] || null : null
  };
}

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET', 'PATCH'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, {
    bucket: req.method === 'PATCH' ? 'profile_write' : 'api',
    userId: user.id,
    limit: req.method === 'PATCH' ? 20 : 90,
    windowSeconds: 60,
    ipLimit: req.method === 'PATCH' ? 40 : 180
  });

  if (req.method === 'PATCH') {
    const body = await readJson(req, 4096);
    const update = {};
    if (Object.prototype.hasOwnProperty.call(body, 'username')) update.username = validateUsername(body.username);
    if (Object.prototype.hasOwnProperty.call(body, 'avatarUrl')) update.avatar_url = validateAvatarUrl(body.avatarUrl);
    if (Object.keys(update).length === 0) {
      throw new ApiError(400, 'EMPTY_PROFILE_UPDATE', 'Provide a username or avatar URL to update.');
    }
    update.updated_at = new Date().toISOString();
    const rows = await rest('users?id=eq.' + encodeURIComponent(user.id), {
      method: 'PATCH', body: update, prefer: 'return=representation'
    });
    if (!Array.isArray(rows) || rows.length !== 1) {
      throw new ApiError(404, 'PROFILE_NOT_FOUND', 'The account profile was not found.');
    }
  }

  const account = await ensureAccountRows(user);
  const recentMatches = await rest(
    'match_results?select=match_id,kills,deaths,placement,won,score,created_at&user_id=eq.' +
    encodeURIComponent(user.id) + '&order=created_at.desc&limit=20'
  );
  sendJson(res, 200, {
    user: { id: user.id, email: user.email },
    profile: account.profile,
    statistics: account.statistics || { matches_played: 0, kills: 0, deaths: 0, wins: 0 },
    recent_matches: Array.isArray(recentMatches) ? recentMatches : []
  });
});
