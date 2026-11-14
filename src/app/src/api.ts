// The API is served from the same origin as the site, under /api.
const API_BASE = '/api'

// Must match ApiGatewayRequestExtensions.ParticipantTokenHeader. A header rather than the query
// string, so the token stays out of access logs.
const PARTICIPANT_TOKEN_HEADER = 'X-Participant-Token'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(method: string, path: string, options: { token?: string; body?: unknown } = {}): Promise<T> {
  const headers: Record<string, string> = {}
  if (options.token) headers[PARTICIPANT_TOKEN_HEADER] = options.token
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })

  const text = await response.text()
  let payload: unknown = undefined
  try {
    payload = text ? JSON.parse(text) : undefined
  } catch {
    // Not JSON: an error page from somewhere in front of the API. Fall through to the status.
  }

  if (!response.ok) {
    const message =
      payload && typeof payload === 'object' && 'message' in payload && typeof payload.message === 'string'
        ? payload.message
        : `The request failed (${response.status}).`
    throw new ApiError(response.status, message)
  }

  return payload as T
}

export interface CreateNightRequest {
  organizerEmail: string
  participantEmails: string[]
  /** yyyy-MM-dd */
  date: string
}

export interface CreateNightResponse {
  nightId: string
}

export const createNight = (body: CreateNightRequest) =>
  request<CreateNightResponse>('POST', '/nights', { body })

// TODO: type this once GetNightService returns something.
export const getNight = (nightId: string, token: string) =>
  request<unknown>('GET', `/nights/${encodeURIComponent(nightId)}`, { token })
