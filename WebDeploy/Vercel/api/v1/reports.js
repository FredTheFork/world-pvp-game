const { endpoint } = require('../_lib/handler');
const { ApiError } = require('../_lib/errors');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest } = require('../_lib/supabase');

function uuid(value) {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, { bucket: 'report', userId: user.id, limit: 5, windowSeconds: 3600, ipLimit: 20, ipWindowSeconds: 3600 });
  const body = await readJson(req, 4096);
  if (!uuid(body.targetUserId) || body.targetUserId === user.id) {
    throw new ApiError(400, 'INVALID_REPORT_TARGET', 'Choose another valid player to report.');
  }
  const category = String(body.category || '').toUpperCase();
  if (!['CHEATING', 'HARASSMENT', 'ABUSE', 'SPAM', 'OTHER'].includes(category)) {
    throw new ApiError(400, 'INVALID_REPORT_CATEGORY', 'Choose a valid report reason.');
  }
  const description = typeof body.description === 'string' ? body.description.trim() : '';
  if (description.length > 1000) throw new ApiError(400, 'REPORT_TOO_LONG', 'Report details must be 1,000 characters or fewer.');
  let matchId = null;
  if (body.matchId !== undefined && body.matchId !== null && body.matchId !== '') {
    if (!uuid(body.matchId)) throw new ApiError(400, 'INVALID_MATCH_ID', 'The match reference is invalid.');
    matchId = body.matchId;
    const membership = await rest(
      'match_members?select=user_id&match_id=eq.' + encodeURIComponent(matchId) +
      '&user_id=in.(' + encodeURIComponent(user.id + ',' + body.targetUserId) + ')'
    );
    if (!Array.isArray(membership) || membership.length < 2) {
      throw new ApiError(404, 'REPORT_CONTEXT_NOT_FOUND', 'Both players must belong to the referenced match.');
    }
  }
  const result = await rest('reports', {
    method: 'POST',
    body: {
      reporter_user_id: user.id,
      target_user_id: body.targetUserId,
      match_id: matchId,
      category,
      description
    },
    prefer: 'return=representation'
  });
  const report = Array.isArray(result) ? result[0] : null;
  sendJson(res, 201, { reportId: report ? report.id : null, status: 'OPEN' });
});
