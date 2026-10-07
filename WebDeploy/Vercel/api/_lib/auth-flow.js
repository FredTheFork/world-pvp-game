const { ApiError } = require('./errors');
const { allowMethods, readJson, sendJson } = require('./http');
const { authRequest } = require('./supabase');
const { enforceRateLimit } = require('./rate-limit');
const { normalizeEmail, validatePassword, validateUsername } = require('./domain');

function publicSession(data) {
  return {
    access_token: data.access_token || '',
    refresh_token: data.refresh_token || '',
    expires_in: Number(data.expires_in || 0),
    token_type: data.token_type || 'bearer',
    user: data.user && typeof data.user.id === 'string'
      ? { id: data.user.id, email: data.user.email || '' }
      : null
  };
}

async function authFlow(req, res, kind) {
  if (!allowMethods(req, res, ['POST'])) return;
  const body = await readJson(req, 8192);
  let email;
  let authBody;
  let bucket;
  let rateOptions;

  if (kind === 'signup' || kind === 'signin') {
    email = normalizeEmail(body.email);
    if (!email) throw new ApiError(400, 'INVALID_EMAIL', 'Enter a valid email address.');
    const password = validatePassword(body.password);
    rateOptions = {
      bucket: 'auth_' + kind,
      limit: kind === 'signup' ? 4 : 8,
      windowSeconds: 900,
      ipLimit: kind === 'signup' ? 4 : 8,
      identity: email,
      identityLimit: kind === 'signup' ? 4 : 8,
      identityWindowSeconds: 900
    };
    if (kind === 'signup') {
      const username = validateUsername(body.username);
      authBody = { email, password, data: { username } };
    } else {
      authBody = { email, password };
    }
  } else if (kind === 'refresh') {
    if (typeof body.refresh_token !== 'string' || body.refresh_token.length < 16 || body.refresh_token.length > 4096) {
      throw new ApiError(400, 'INVALID_REFRESH_TOKEN', 'The refresh session is invalid. Sign in again.');
    }
    authBody = { refresh_token: body.refresh_token };
    rateOptions = { bucket: 'auth_refresh', limit: 30, windowSeconds: 60, ipLimit: 60 };
  } else {
    throw new ApiError(404, 'NOT_FOUND', 'The requested endpoint was not found.');
  }

  await enforceRateLimit(req, rateOptions);
  const path = kind === 'signup'
    ? '/auth/v1/signup'
    : kind === 'signin'
      ? '/auth/v1/token?grant_type=password'
      : '/auth/v1/token?grant_type=refresh_token';
  const data = await authRequest(path, authBody);

  if (kind === 'signup' && !data.access_token) {
    sendJson(res, 202, {
      needs_email_confirmation: true,
      message: 'Check your email to confirm the account, then sign in.'
    });
    return;
  }
  if (!data.access_token || !data.refresh_token || !data.user || !data.user.id) {
    throw new ApiError(502, 'AUTH_RESPONSE_INVALID', 'The identity provider returned an incomplete sign-in response.');
  }
  sendJson(res, 200, publicSession(data));
}

module.exports = { authFlow };
