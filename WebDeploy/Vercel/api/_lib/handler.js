const { asApiError } = require('./errors');
const { requireAllowedOrigin, sendError } = require('./http');

function endpoint(handler) {
  return async function apiEndpoint(req, res) {
    try {
      requireAllowedOrigin(req);
      await handler(req, res);
    } catch (error) {
      sendError(res, asApiError(error));
    }
  };
}

module.exports = { endpoint };
