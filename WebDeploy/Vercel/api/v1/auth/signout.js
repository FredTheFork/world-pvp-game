const { endpoint } = require('../../_lib/handler');
const { requireUser } = require('../../_lib/auth');
const { allowMethods, sendJson } = require('../../_lib/http');
const { enforceRateLimit } = require('../../_lib/rate-limit');
const { authSignOut } = require('../../_lib/supabase');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['POST'])) return;
  const user = await requireUser(req);
  await enforceRateLimit(req, { bucket: 'auth_signout', userId: user.id, limit: 10, windowSeconds: 60, ipLimit: 30 });
  await authSignOut(user.accessToken);
  sendJson(res, 200, { signedOut: true });
});
