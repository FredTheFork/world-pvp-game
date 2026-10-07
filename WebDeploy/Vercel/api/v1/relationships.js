const { endpoint } = require('../_lib/handler');
const { ApiError } = require('../_lib/errors');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest } = require('../_lib/supabase');

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, { bucket: 'relationship', userId: user.id, limit: 30, windowSeconds: 60, ipLimit: 60 });
  const body = await readJson(req, 2048);
  const target = body.targetUserId;
  const action = String(body.action || '').toUpperCase();
  const operations = {
    BLOCK: ['user_blocks', 'POST'],
    UNBLOCK: ['user_blocks', 'DELETE'],
    MUTE: ['user_mutes', 'POST'],
    UNMUTE: ['user_mutes', 'DELETE']
  };
  if (!UUID.test(String(target || '')) || target === user.id || !operations[action]) {
    throw new ApiError(400, 'INVALID_RELATIONSHIP', 'Choose a valid player and block/mute action.');
  }
  const [table, method] = operations[action];
  const query = table + '?user_id=eq.' + encodeURIComponent(user.id) +
    '&' + (table === 'user_blocks' ? 'blocked_user_id' : 'muted_user_id') + '=eq.' + encodeURIComponent(target);
  if (method === 'POST') {
    await rest(table + '?on_conflict=user_id,' + (table === 'user_blocks' ? 'blocked_user_id' : 'muted_user_id'), {
      method: 'POST',
      body: table === 'user_blocks'
        ? { user_id: user.id, blocked_user_id: target }
        : { user_id: user.id, muted_user_id: target },
      prefer: 'resolution=ignore-duplicates,return=minimal'
    });
  } else {
    await rest(query, { method: 'DELETE' });
  }
  sendJson(res, 200, { action, targetUserId: target, success: true });
});
