const { endpoint } = require('../_lib/handler');
const { ApiError } = require('../_lib/errors');
const { requireUser } = require('../_lib/auth');
const { allowMethods, readJson, sendJson, routeParam } = require('../_lib/http');
const { enforceRateLimit } = require('../_lib/rate-limit');
const { rest } = require('../_lib/supabase');
const { getMatchId } = require('../_lib/domain');

module.exports = endpoint(async (req, res) => {
  if (!allowMethods(req, res, ['GET', 'POST'])) return;
  const user = await requireUser(req);
  const matchId = getMatchId({ query: { id: routeParam(req, 'matchId') } });
  if (req.method === 'POST') {
    await enforceRateLimit(req, { bucket: 'chat_send', userId: user.id, limit: 8, windowSeconds: 10, ipLimit: 30 });
    const body = await readJson(req, 2048);
    const text = typeof body.message === 'string' ? body.message.trim() : '';
    if (!text || text.length > 500 || /[\u0000-\u0008\u000B\u000C\u000E-\u001F]/.test(text)) {
      throw new ApiError(400, 'INVALID_CHAT_MESSAGE', 'Chat messages must be 1–500 characters.');
    }
    const members = await rest(
      'match_members?select=status&match_id=eq.' + encodeURIComponent(matchId) +
      '&user_id=eq.' + encodeURIComponent(user.id) + '&limit=1'
    );
    if (!Array.isArray(members) || members.length !== 1 || !['ACTIVE', 'DISCONNECTED'].includes(members[0].status)) {
      throw new ApiError(403, 'CHAT_ACCESS_DENIED', 'You are not an active member of this match.');
    }
    const matches = await rest('matches?select=state&id=eq.' + encodeURIComponent(matchId) + '&limit=1');
    if (!Array.isArray(matches) || !matches.length || !['LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE'].includes(matches[0].state)) {
      throw new ApiError(409, 'CHAT_CLOSED', 'Chat is unavailable for this match.');
    }
    const now = encodeURIComponent(new Date().toISOString());
    const common = 'select=id&user_id=eq.' + encodeURIComponent(user.id) + '&lifted_at=is.null&starts_at=lte.' + now +
      '&or=(expires_at.is.null,expires_at.gt.' + now + ')&limit=1';
    const [globalBans, matchBans] = await Promise.all([
      rest('bans?' + common + '&match_id=is.null'),
      rest('bans?' + common + '&match_id=eq.' + encodeURIComponent(matchId))
    ]);
    if ((Array.isArray(globalBans) && globalBans.length) || (Array.isArray(matchBans) && matchBans.length)) {
      throw new ApiError(403, 'USER_BANNED', 'This account cannot chat.');
    }
    const inserted = await rest('chat_messages', {
      method: 'POST',
      body: { match_id: matchId, user_id: user.id, body: text },
      prefer: 'return=representation'
    });
    const row = Array.isArray(inserted) ? inserted[0] : null;
    sendJson(res, 201, { message: row ? { id: row.id, createdAt: row.created_at } : null });
    return;
  }

  await enforceRateLimit(req, { bucket: 'chat_read', userId: user.id, limit: 40, windowSeconds: 60, ipLimit: 80 });
  const members = await rest(
    'match_members?select=status&match_id=eq.' + encodeURIComponent(matchId) +
    '&user_id=eq.' + encodeURIComponent(user.id) + '&limit=1'
  );
  if (!Array.isArray(members) || members.length !== 1) throw new ApiError(403, 'CHAT_ACCESS_DENIED', 'You are not a member of this match.');
  const afterText = typeof req.query.after === 'string' ? req.query.after : '';
  let after = 0;
  if (afterText) {
    after = Number(afterText);
    if (!Number.isSafeInteger(after) || after < 0) throw new ApiError(400, 'INVALID_CURSOR', 'The chat cursor is invalid.');
  }
  const [blockedByMe, blockedMe, mutedByMe] = await Promise.all([
    rest('user_blocks?select=blocked_user_id&user_id=eq.' + encodeURIComponent(user.id)),
    rest('user_blocks?select=user_id&blocked_user_id=eq.' + encodeURIComponent(user.id)),
    rest('user_mutes?select=muted_user_id&user_id=eq.' + encodeURIComponent(user.id))
  ]);
  const hidden = new Set();
  for (const row of blockedByMe || []) hidden.add(row.blocked_user_id);
  for (const row of blockedMe || []) hidden.add(row.user_id);
  for (const row of mutedByMe || []) hidden.add(row.muted_user_id);
  const query = 'chat_messages?select=id,user_id,body,created_at,users(username)&match_id=eq.' +
    encodeURIComponent(matchId) + '&id=gt.' + after + '&order=id.asc&limit=50';
  const rows = await rest(query);
  const messages = Array.isArray(rows) ? rows.filter((row) => !hidden.has(row.user_id)).map((row) => ({
    id: row.id,
    userId: row.user_id,
    username: row.users && row.users.username,
    message: row.body,
    createdAt: row.created_at
  })) : [];
  sendJson(res, 200, { messages, nextCursor: messages.length ? messages[messages.length - 1].id : after });
});
