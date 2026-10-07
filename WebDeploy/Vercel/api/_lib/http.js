const { ApiError } = require('./errors');

function setSafeHeaders(res) {
  res.setHeader('Cache-Control', 'no-store, max-age=0');
  res.setHeader('Pragma', 'no-cache');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('Vary', 'Origin');
}

function sendJson(res, status, body) {
  setSafeHeaders(res);
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  res.statusCode = status;
  res.end(JSON.stringify(body));
}

function sendError(res, error) {
  const status = Number.isInteger(error && error.status) ? error.status : 503;
  const payload = {
    error: error && error.code ? error.code : 'SERVICE_UNAVAILABLE',
    message: error && error.message ? error.message : 'The backend is temporarily unavailable.'
  };
  if (error && error.details && status < 500) payload.details = error.details;
  if (status === 429 && error && Number.isFinite(error.retryAfterSeconds)) {
    res.setHeader('Retry-After', String(Math.max(1, Math.ceil(error.retryAfterSeconds))));
  }
  sendJson(res, status, payload);
}

function allowMethods(req, res, allowed) {
  if (allowed.includes(req.method)) return true;
  res.setHeader('Allow', allowed.join(', '));
  sendJson(res, 405, { error: 'METHOD_NOT_ALLOWED', message: 'This method is not supported.' });
  return false;
}

function checkOrigin(req) {
  const origin = req.headers && req.headers.origin;
  if (!origin) return true;
  const configured = process.env.PUBLIC_WEB_ORIGIN;
  if (!configured) {
    // Same-origin requests do not need CORS. Cross-origin browser calls are denied unless explicitly configured.
    const host = req.headers && req.headers.host;
    try {
      return new URL(origin).host === host;
    } catch (_) {
      return false;
    }
  }
  return origin === configured;
}

function requireAllowedOrigin(req) {
  if (!checkOrigin(req)) throw new ApiError(403, 'ORIGIN_NOT_ALLOWED', 'This browser origin is not allowed.');
}

async function readJson(req, maximumBytes = 16384) {
  if (req.body !== undefined) {
    if (Buffer.isBuffer(req.body)) return parseBodyBuffer(req.body, maximumBytes);
    if (typeof req.body === 'string') return parseBodyBuffer(Buffer.from(req.body, 'utf8'), maximumBytes);
    if (req.body && typeof req.body === 'object') return req.body;
    if (req.body === null) return {};
  }

  const chunks = [];
  let bytes = 0;
  for await (const chunk of req) {
    bytes += chunk.length;
    if (bytes > maximumBytes) throw new ApiError(413, 'BODY_TOO_LARGE', 'The request body is too large.');
    chunks.push(chunk);
  }
  return parseBodyBuffer(Buffer.concat(chunks), maximumBytes);
}

function parseBodyBuffer(buffer, maximumBytes) {
  if (buffer.length > maximumBytes) throw new ApiError(413, 'BODY_TOO_LARGE', 'The request body is too large.');
  if (buffer.length === 0) return {};
  try {
    const body = JSON.parse(buffer.toString('utf8'));
    if (!body || typeof body !== 'object' || Array.isArray(body)) {
      throw new Error('JSON object required');
    }
    return body;
  } catch (_) {
    throw new ApiError(400, 'INVALID_JSON', 'The request body must be a JSON object.');
  }
}

function bearerToken(req) {
  const header = req.headers && (req.headers.authorization || req.headers.Authorization);
  if (typeof header !== 'string') return '';
  const match = /^Bearer ([A-Za-z0-9._~-]{20,8192})$/i.exec(header.trim());
  return match ? match[1] : '';
}

function clientIp(req) {
  const forwarded = req.headers && (req.headers['x-forwarded-for'] || req.headers['x-vercel-forwarded-for']);
  if (typeof forwarded === 'string' && forwarded.length < 512) {
    const first = forwarded.split(',')[0].trim();
    if (first.length > 0) return first;
  }
  return (req.socket && req.socket.remoteAddress) || 'unknown';
}

function routeParam(req, name) {
  const value = req.query && req.query[name];
  return Array.isArray(value) ? value[0] : value;
}

module.exports = {
  allowMethods,
  bearerToken,
  clientIp,
  readJson,
  requireAllowedOrigin,
  routeParam,
  sendError,
  sendJson,
  setSafeHeaders
};
