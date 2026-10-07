class ApiError extends Error {
  constructor(status, code, message, details) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.details = details || null;
  }
}

function asApiError(error) {
  if (error instanceof ApiError) return error;
  return new ApiError(503, 'SERVICE_UNAVAILABLE', 'The backend is temporarily unavailable. Try again shortly.');
}

module.exports = { ApiError, asApiError };
