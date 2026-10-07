const { endpoint } = require('../_lib/handler');
const { ApiError } = require('../_lib/errors');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest } = require('../_lib/supabase');

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function requireModerator(userId) {
  const configured = (process.env.WORLD_PVP_MODERATOR_USER_IDS || '')
    .split(',').map((value) => value.trim().toLowerCase()).filter(Boolean);
  if (!configured.includes(userId.toLowerCase())) {
    throw new ApiError(403, 'MODERATOR_REQUIRED', 'This account is not authorized to moderate reports.');
  }
}

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET', 'POST'])) return;
  const user = await requireUser(req);
  requireModerator(user.id);
  await enforceRateLimit(req, { bucket: 'moderation', userId: user.id, limit: 60, windowSeconds: 60, ipLimit: 120 });

  if (req.method === 'GET') {
    const reports = await rest(
      'reports?select=id,reporter_user_id,target_user_id,match_id,category,description,status,created_at' +
      '&status=in.(OPEN,REVIEWING)&order=created_at.asc&limit=100'
    );
    sendJson(res, 200, { reports: Array.isArray(reports) ? reports : [] });
    return;
  }

  const body = await readJson(req, 4096);
  const action = String(body.action || '').toUpperCase();
  if (action === 'BAN_USER') {
    const targetUserId = String(body.targetUserId || '');
    const reason = typeof body.reason === 'string' ? body.reason.trim() : '';
    const durationSeconds = body.durationSeconds === undefined ? 86400 : Number(body.durationSeconds);
    if (!UUID.test(targetUserId) || targetUserId === user.id || reason.length < 3 || reason.length > 500 ||
        !Number.isInteger(durationSeconds) || durationSeconds < 60 || durationSeconds > 31536000) {
      throw new ApiError(400, 'INVALID_BAN', 'Provide a valid target, reason, and duration (1 minute to 365 days).');
    }
    let matchId = null;
    if (body.matchId !== undefined && body.matchId !== null) {
      matchId = String(body.matchId);
      if (!UUID.test(matchId)) throw new ApiError(400, 'INVALID_MATCH_ID', 'The match reference is invalid.');
    }
    const startsAt = new Date();
    const rows = await rest('bans', {
      method: 'POST',
      body: {
        user_id: targetUserId,
        match_id: matchId,
        created_by: user.id,
        reason,
        starts_at: startsAt.toISOString(),
        expires_at: new Date(startsAt.getTime() + durationSeconds * 1000).toISOString()
      },
      prefer: 'return=representation'
    });
    const ban = Array.isArray(rows) ? rows[0] : null;
    sendJson(res, 201, { banId: ban ? ban.id : null, targetUserId, expiresAt: ban ? ban.expires_at : null });
    return;
  }

  if (action === 'LIFT_BAN') {
    const banId = String(body.banId || '');
    if (!UUID.test(banId)) throw new ApiError(400, 'INVALID_BAN_ID', 'The ban reference is invalid.');
    const rows = await rest('bans?id=eq.' + encodeURIComponent(banId) + '&lifted_at=is.null', {
      method: 'PATCH',
      body: { lifted_at: new Date().toISOString(), lifted_by: user.id },
      prefer: 'return=representation'
    });
    if (!Array.isArray(rows) || rows.length !== 1) throw new ApiError(404, 'BAN_NOT_FOUND', 'The active ban was not found.');
    sendJson(res, 200, { banId, lifted: true });
    return;
  }

  if (action === 'RESOLVE_REPORT') {
    const reportId = String(body.reportId || '');
    const status = String(body.status || '').toUpperCase();
    if (!UUID.test(reportId) || !['RESOLVED', 'DISMISSED'].includes(status)) {
      throw new ApiError(400, 'INVALID_REPORT_RESOLUTION', 'Choose a valid report and resolution.');
    }
    const rows = await rest('reports?id=eq.' + encodeURIComponent(reportId) + '&status=in.(OPEN,REVIEWING)', {
      method: 'PATCH',
      body: { status, resolved_at: new Date().toISOString(), resolved_by: user.id },
      prefer: 'return=representation'
    });
    if (!Array.isArray(rows) || rows.length !== 1) throw new ApiError(404, 'REPORT_NOT_FOUND', 'The report was not found.');
    sendJson(res, 200, { reportId, status });
    return;
  }

  throw new ApiError(400, 'INVALID_MODERATION_ACTION', 'That moderation action is not supported.');
});
