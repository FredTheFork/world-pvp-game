const { ApiError } = require('./errors');

function projectUrl() {
  const value = process.env.SUPABASE_URL;
  if (!value) throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'Supabase backend configuration is missing.');
  let parsed;
  try { parsed = new URL(value); } catch (_) {
    throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'Supabase backend configuration is invalid.');
  }
  if (parsed.protocol !== 'https:' && parsed.hostname !== 'localhost' && parsed.hostname !== '127.0.0.1') {
    throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'Supabase must use HTTPS outside local development.');
  }
  return parsed.origin;
}

function publicKey() {
  const value = process.env.SUPABASE_PUBLISHABLE_KEY || process.env.SUPABASE_ANON_KEY;
  if (!value) throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'Supabase public authentication configuration is missing.');
  return value;
}

function privateKey() {
  const value = process.env.SUPABASE_SECRET_KEY || process.env.SUPABASE_SERVICE_ROLE_KEY;
  if (!value) throw new ApiError(503, 'BACKEND_NOT_CONFIGURED', 'Supabase server credentials are missing.');
  return value;
}

function adminHeaders(extra) {
  const key = privateKey();
  const headers = Object.assign({ apikey: key }, extra || {});
  // Legacy service_role keys are JWTs and need Authorization. New sb_secret keys are translated
  // by Supabase from apikey and must never be copied into a browser Authorization header.
  if (!key.startsWith('sb_secret_')) headers.Authorization = 'Bearer ' + key;
  return headers;
}

async function request(path, options = {}) {
  const base = projectUrl();
  if (typeof path !== 'string' || !path.startsWith('/')) {
    throw new ApiError(500, 'INTERNAL_ERROR', 'The backend request could not be prepared.');
  }
  const headers = Object.assign({ Accept: 'application/json' }, options.headers || {});
  if (options.admin) {
    Object.assign(headers, adminHeaders(headers));
  }
  const init = {
    method: options.method || 'GET',
    headers,
    signal: AbortSignal.timeout(options.timeoutMs || 8000)
  };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(options.body);
  }

  let response;
  try {
    response = await fetch(base + path, init);
  } catch (_) {
    throw new ApiError(503, 'UPSTREAM_UNAVAILABLE', 'The account database is temporarily unavailable.');
  }

  const text = await response.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch (_) { data = null; }
  }
  if (!response.ok) {
    if (response.status === 429) {
      const limited = new ApiError(429, 'UPSTREAM_RATE_LIMITED', 'The identity or database service is rate limited. Wait and try again.');
      const retry = Number(response.headers.get('retry-after'));
      limited.retryAfterSeconds = Number.isFinite(retry) ? Math.max(1, retry) : 60;
      throw limited;
    }
    const rawCode = String((data && (data.message || data.code || data.error_code || data.error)) || '');
    const safeRpcErrors = {
      MATCH_UNAVAILABLE: [404, 'MATCH_UNAVAILABLE', 'This match is unavailable or its lobby is locked.'],
      MATCH_FULL: [409, 'MATCH_FULL', 'This match is full.'],
      USER_BANNED: [403, 'USER_BANNED', 'This account is not allowed to join this match.'],
      RECONNECT_REQUIRED: [409, 'RECONNECT_REQUIRED', 'Use the active reconnect session for this player.'],
      PLAYER_KICKED: [403, 'PLAYER_KICKED', 'The host removed this account from the match.'],
      HOST_REQUIRED: [403, 'HOST_REQUIRED', 'Only the match host can perform this action.'],
      LOBBY_ONLY: [409, 'LOBBY_ONLY', 'This action is only available while the match is in its lobby.'],
      INVALID_MATCH_TRANSITION: [409, 'INVALID_MATCH_TRANSITION', 'That match state transition is not allowed.'],
      PLAYER_NOT_KICKABLE: [404, 'PLAYER_NOT_KICKABLE', 'That player is not in a kickable state.'],
      MATCH_NOT_FOUND: [404, 'MATCH_NOT_FOUND', 'The match was not found.'],
      TICKET_EXPIRED_OR_USED: [401, 'TICKET_EXPIRED_OR_USED', 'The connection ticket expired or was already used.'],
      RECONNECT_UNAVAILABLE: [401, 'RECONNECT_UNAVAILABLE', 'This player session cannot be reconnected.'],
      MATCH_NOT_RECONNECTABLE: [409, 'MATCH_NOT_RECONNECTABLE', 'The match is no longer accepting reconnects.'],
      PLAYER_LIMIT_BELOW_CURRENT_COUNT: [409, 'PLAYER_LIMIT_BELOW_CURRENT_COUNT', 'The player limit cannot be lower than the current roster.']
    };
    const safeCode = Object.keys(safeRpcErrors).find((candidate) => rawCode.includes(candidate));
    if (safeCode) throw new ApiError(...safeRpcErrors[safeCode]);

    const duplicate = rawCode.toLowerCase().includes('duplicate') || response.status === 409;
    const message = duplicate
      ? 'That value is already in use.'
      : response.status === 401 || response.status === 403
        ? 'Authentication or authorization failed.'
        : response.status === 404
          ? 'The requested backend resource was not found.'
          : 'The account database could not complete the request.';
    const status = response.status === 400 || response.status === 409 || response.status === 422
      ? response.status
      : response.status === 401 ? 401
        : response.status === 403 ? 403
          : response.status === 404 ? 404
            : 503;
    throw new ApiError(status, duplicate ? 'VALUE_ALREADY_EXISTS' : 'BACKEND_REQUEST_FAILED', message);
  }
  return data;
}

function rest(path, options = {}) {
  const headers = Object.assign({}, options.headers || {});
  if (options.prefer) headers.Prefer = options.prefer;
  return request('/rest/v1/' + path.replace(/^\//, ''), {
    admin: true,
    method: options.method || 'GET',
    body: options.body,
    headers,
    timeoutMs: options.timeoutMs
  });
}

function rpc(name, args) {
  if (!/^[a-z_][a-z0-9_]{0,63}$/.test(name)) {
    throw new ApiError(500, 'INTERNAL_ERROR', 'The backend operation could not be prepared.');
  }
  return rest('rpc/' + name, { method: 'POST', body: args });
}

async function verifyAccessToken(accessToken) {
  const key = publicKey();
  const data = await request('/auth/v1/user', {
    headers: { apikey: key, Authorization: 'Bearer ' + accessToken },
    timeoutMs: 8000
  });
  if (!data || typeof data.id !== 'string') {
    throw new ApiError(401, 'INVALID_SESSION', 'Sign in again to continue.');
  }
  return data;
}

async function authRequest(path, body) {
  const key = publicKey();
  return request(path, {
    method: 'POST',
    headers: { apikey: key },
    body,
    timeoutMs: 10000
  });
}

async function authSignOut(accessToken) {
  return request('/auth/v1/logout', {
    method: 'POST',
    headers: { apikey: publicKey(), Authorization: 'Bearer ' + accessToken },
    timeoutMs: 8000
  });
}

module.exports = { authRequest, authSignOut, privateKey, projectUrl, publicKey, request, rest, rpc, verifyAccessToken };
