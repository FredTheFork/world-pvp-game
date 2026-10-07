const crypto = require('node:crypto');
const { endpoint } = require('../../_lib/handler');
const { ApiError } = require('../../_lib/errors');
const { allowMethods, sendJson } = require('../../_lib/http');
const { rpc } = require('../../_lib/supabase');

function requireCron(req) {
  const expected = process.env.CRON_SECRET;
  const header = req.headers && req.headers.authorization;
  const supplied = typeof header === 'string' && header.startsWith('Bearer ') ? header.slice(7).trim() : '';
  if (!expected || expected.length < 32) {
    throw new ApiError(503, 'CRON_AUTH_NOT_CONFIGURED', 'Scheduled backend reconciliation is not configured.');
  }
  const a = Buffer.from(supplied, 'utf8');
  const b = Buffer.from(expected, 'utf8');
  if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) {
    throw new ApiError(401, 'CRON_AUTH_REQUIRED', 'A trusted scheduler is required.');
  }
}

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET', 'POST'])) return;
  requireCron(req);
  const closed = await rpc('close_stale_matches', {
    p_server_stale_seconds: 90,
    p_creating_stale_seconds: 300,
    p_finished_stale_seconds: 3600
  });
  const rows = Array.isArray(closed) ? closed : [];
  sendJson(res, 200, {
    closedMatches: rows.length,
    matches: rows.map((row) => ({ matchId: row.match_id, previousState: row.previous_state }))
  });
});
