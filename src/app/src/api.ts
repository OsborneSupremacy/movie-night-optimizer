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

async function request<T>(
  method: string,
  path: string,
  options: { token?: string; body?: unknown; signal?: AbortSignal } = {},
): Promise<T> {
  const headers: Record<string, string> = {}
  if (options.token) headers[PARTICIPANT_TOKEN_HEADER] = options.token
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    signal: options.signal,
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
  /** Everyone emailed a link, the organizer included. */
  invitedCount: number
  /** Guests left out because they have asked never to be emailed. */
  optedOutCount: number
}

export const createNight = (body: CreateNightRequest) =>
  request<CreateNightResponse>('POST', '/nights', { body })

export type NightPhase = 'pending' | 'collecting' | 'ranking' | 'done'

/** What helps people rank a movie they don't know. Any part can be empty. Mirrors MovieDetails. */
export interface MovieDetails {
  /** The US rating, like "PG-13". */
  certification: string
  /** 0 when unknown. */
  runtimeMinutes: number
  genres: string[]
  /** US subscription or free services, from JustWatch by way of TMDB. */
  streamingOn: string[]
  trailerUrl: string
}

export interface MovieView {
  movieId: string
  title: string
  /** Empty when TMDB didn't find the movie. */
  year: string
  /** A small TMDB poster, or empty. */
  posterUrl: string
  details: MovieDetails
  /** The movie's page on TMDB, or empty when TMDB didn't find it. */
  tmdbUrl: string
}

export interface ResultView {
  movieId: string
  title: string
  year: string
  posterUrl: string
  details: MovieDetails
  /** The movie's page on TMDB, or empty when TMDB didn't find it. */
  tmdbUrl: string
  points: number
  firstPlaceVotes: number
  isYours: boolean
}

/** What one participant sees of a night. Mirrors NightView in Messages.cs. */
export interface NightView {
  nightId: string
  /** yyyy-MM-dd */
  date: string
  phase: NightPhase
  isOrganizer: boolean
  /** Empty until this participant has suggested a movie. */
  yourSuggestion: string
  yourYear: string
  yourPosterUrl: string
  yourDetails: MovieDetails
  yourTmdbUrl: string
  /** Whether this participant has answered the current round. */
  hasAnswered: boolean
  participantCount: number
  answeredCount: number
  /** While ranking: everyone else's suggestions, never this participant's own. */
  toRank: MovieView[]
  /** When done: every movie, winner first. */
  results: ResultView[]
  /** For the organizer only: who hasn't answered the current round. */
  waitingOn: string[]
  /** For the organizer of a pending night only: who the invitations will go to. */
  guests: string[]
}

const nightPath = (nightId: string) => `/nights/${encodeURIComponent(nightId)}`

export const getNight = (nightId: string, token: string) =>
  request<NightView>('GET', nightPath(nightId), { token })

/** tmdbId is the movie picked from the autocomplete list, or 0 when the title was typed. */
export const submitSuggestion = (nightId: string, token: string, suggestion: string, tmdbId = 0) =>
  request<NightView>('POST', `${nightPath(nightId)}/suggestion`, { token, body: { suggestion, tmdbId } })

/** One movie in the autocomplete list. Mirrors MovieOption. */
export interface MovieOption {
  tmdbId: number
  title: string
  /** Empty when unknown. */
  year: string
  /** A thumbnail, or empty. */
  posterUrl: string
}

/**
 * Movies that could be what somebody is typing. Public and cached for a day at CloudFront, so the
 * query is normalized first: the same letters typed differently share one cached answer.
 */
export const searchMovies = (query: string, signal?: AbortSignal) =>
  request<{ movies: MovieOption[] }>('GET', `/movies/search?q=${encodeURIComponent(normalizeQuery(query))}`, {
    signal,
  }).then((response) => response.movies)

export function normalizeQuery(query: string): string {
  return query.trim().toLowerCase().replace(/\s+/g, ' ')
}

/** Movie ids from NightView.toRank, best first. */
export const submitRanking = (nightId: string, token: string, ranking: string[]) =>
  request<NightView>('POST', `${nightPath(nightId)}/ranking`, { token, body: { ranking } })

/** The "don't email me again" link: lists this inbox for good and takes them out of the night. */
export const optOut = (nightId: string, token: string) =>
  request<void>('POST', `${nightPath(nightId)}/opt-out`, { token })

/** Organizer only: confirms the address was theirs, and sends the guests their invitations. */
export const confirmNight = (nightId: string, token: string) =>
  request<NightView>('POST', `${nightPath(nightId)}/confirm`, { token })

/** Organizer only: closes the current round without waiting for everyone. */
export const advanceNight = (nightId: string, token: string) =>
  request<NightView>('POST', `${nightPath(nightId)}/advance`, { token })
