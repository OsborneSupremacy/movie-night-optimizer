import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  advanceNight,
  ApiError,
  confirmNight,
  getNight,
  submitRanking,
  submitSuggestion,
  type MovieDetails,
  type MovieView,
  type NightView,
} from '../api.ts'
import { formatDate } from '../dates.ts'
import { readToken } from '../token.ts'

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Something went wrong. Try again in a moment.'
}

/** Slow enough to stay far under the API's 5 requests a second with a full sofa of eight. */
const POLL_INTERVAL_MS = 15_000

type State = { kind: 'loading' } | { kind: 'loaded'; night: NightView } | { kind: 'failed'; message: string }

/**
 * The one page every emailed link opens. What it shows depends on the night's phase and on
 * whether this participant has already answered.
 *
 * Loading it only reads. Suggesting and ranking happen when a person presses a button, so a mail
 * scanner that follows the link in an email can't answer on anybody's behalf.
 */
export default function Night() {
  const { nightId = '' } = useParams()
  const [token] = useState(readToken)
  const [state, setState] = useState<State>({ kind: 'loading' })

  const load = useCallback(async () => {
    if (!token) {
      setState({ kind: 'failed', message: 'This link is missing its key. Open the link from your email again.' })
      return
    }
    try {
      setState({ kind: 'loaded', night: await getNight(nightId, token) })
    } catch (error) {
      setState({ kind: 'failed', message: messageOf(error) })
    }
  }, [nightId, token])

  useEffect(() => {
    void load()
  }, [load])

  // Waiting on others: check again now and then, so the next round shows up without a reload.
  // Only while this person has answered, so a refresh never lands on a half-typed suggestion or a
  // half-sorted list, and only while the tab is visible. A failed check keeps what is on screen.
  const waiting = state.kind === 'loaded' && state.night.phase !== 'done' && state.night.hasAnswered
  useEffect(() => {
    if (!waiting) return
    const timer = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      getNight(nightId, token)
        .then((night) => setState({ kind: 'loaded', night }))
        .catch(() => {})
    }, POLL_INTERVAL_MS)
    return () => window.clearInterval(timer)
  }, [waiting, nightId, token])

  if (state.kind === 'loading') return <p>Loading…</p>

  if (state.kind === 'failed') {
    return (
      <p className="error" role="alert">
        {state.message}
      </p>
    )
  }

  const { night } = state
  const update = (next: NightView) => setState({ kind: 'loaded', night: next })

  return (
    <section>
      <p className="eyebrow">Movie night · {formatDate(night.date)}</p>

      {night.phase === 'pending' && <Pending night={night} token={token} onChange={update} />}
      {night.phase === 'collecting' && <Collecting night={night} token={token} onChange={update} />}
      {night.phase === 'ranking' && <Ranking night={night} token={token} onChange={update} />}
      {night.phase === 'done' && <Results night={night} />}

      {(night.phase === 'collecting' || night.phase === 'ranking') && (
        <Progress night={night} token={token} onChange={update} onRefresh={() => void load()} />
      )}
    </section>
  )
}

interface PhaseProps {
  night: NightView
  token: string
  onChange: (night: NightView) => void
}

/**
 * A night nobody has been invited to yet. The organizer lands here from the confirmation email,
 * and pressing the button is what proves the address is theirs and sends the invitations.
 */
function Pending({ night, token, onChange }: PhaseProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function handleConfirm() {
    setBusy(true)
    setError('')
    try {
      onChange(await confirmNight(night.nightId, token))
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  if (!night.isOrganizer) {
    return (
      <>
        <h1>Not started yet</h1>
        <p>This movie night is waiting for the person who planned it. You'll get an email when it starts.</p>
      </>
    )
  }

  return (
    <>
      <h1>Send the invitations?</h1>
      <p>
        Nobody has been emailed yet. Press the button to confirm this movie night is yours, and{' '}
        {night.guests.length === 1 ? 'this person gets' : `these ${night.guests.length} people get`} a link to suggest
        a movie:
      </p>
      <ul className="guest-list">
        {night.guests.map((guest) => (
          <li key={guest}>{guest}</li>
        ))}
      </ul>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="button" disabled={busy} onClick={() => void handleConfirm()}>
        {busy ? 'Sending…' : 'Send the invitations'}
      </button>
    </>
  )
}

function Collecting({ night, token, onChange }: PhaseProps) {
  const [editing, setEditing] = useState(!night.yourSuggestion)
  const [suggestion, setSuggestion] = useState(night.yourSuggestion)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const next = await submitSuggestion(night.nightId, token, suggestion.trim())
      setEditing(false)
      onChange(next)
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  if (!editing) {
    const found = night.yourYear !== '' || night.yourPosterUrl !== ''
    return (
      <>
        <h1>You suggested {night.yourSuggestion}</h1>
        <div className="your-pick">
          <Poster url={night.yourPosterUrl} title={night.yourSuggestion} size="large" />
          <div>
            {night.yourYear && <p className="year">{night.yourYear}</p>}
            <Facts details={night.yourDetails} tmdbUrl={night.yourTmdbUrl} />
            <p>When everyone has suggested a movie, you'll get an email to rank the others' picks.</p>
            {found && <p className="hint">Not the one you meant? Change it, and add the year.</p>}
          </div>
        </div>
        <button type="button" className="secondary" onClick={() => setEditing(true)}>
          Change my movie
        </button>
      </>
    )
  }

  return (
    <>
      <h1>What should we watch?</h1>
      <p>Suggest one movie. Nobody sees the others' picks until everyone has made theirs.</p>
      <form onSubmit={handleSubmit}>
        <label>
          Your movie
          <input
            required
            maxLength={100}
            autoComplete="off"
            value={suggestion}
            onChange={(event) => setSuggestion(event.target.value)}
          />
        </label>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={busy || !suggestion.trim()}>
          {busy ? 'Saving…' : 'Suggest it'}
        </button>
      </form>
    </>
  )
}

function Ranking({ night, token, onChange }: PhaseProps) {
  const [order, setOrder] = useState<MovieView[]>(night.toRank)
  const [dragging, setDragging] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  function move(from: number, to: number) {
    if (to < 0 || to >= order.length || from === to) return
    const next = [...order]
    const [movie] = next.splice(from, 1)
    next.splice(to, 0, movie)
    setOrder(next)
  }

  async function handleSubmit() {
    setBusy(true)
    setError('')
    try {
      onChange(await submitRanking(night.nightId, token, order.map((movie) => movie.movieId)))
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  if (night.hasAnswered) {
    return (
      <>
        <h1>Your ranking is in</h1>
        <p>When everyone has ranked the movies, you'll get an email with the winner.</p>
      </>
    )
  }

  return (
    <>
      <h1>Rank the movies</h1>
      <p>
        Your pick, {night.yourSuggestion || 'if you made one'}, isn't on the list. Put the rest in order, favorite
        first. Drag them, or use the arrows.
      </p>

      <ol className="rank-list">
        {order.map((movie, index) => (
          <li
            key={movie.movieId}
            draggable
            className={dragging === index ? 'dragging' : undefined}
            onDragStart={() => setDragging(index)}
            onDragEnd={() => setDragging(null)}
            onDragOver={(event) => {
              event.preventDefault()
              if (dragging !== null && dragging !== index) {
                move(dragging, index)
                setDragging(index)
              }
            }}
          >
            <span className="rank-number">{index + 1}</span>
            <Poster url={movie.posterUrl} title={movie.title} />
            <span className="rank-title">
              {movie.title}
              {movie.year && <span className="year"> {movie.year}</span>}
              <Facts details={movie.details} tmdbUrl={movie.tmdbUrl} />
            </span>
            <span className="rank-buttons">
              <button
                type="button"
                className="icon"
                aria-label={`Move ${movie.title} up`}
                disabled={index === 0}
                onClick={() => move(index, index - 1)}
              >
                ↑
              </button>
              <button
                type="button"
                className="icon"
                aria-label={`Move ${movie.title} down`}
                disabled={index === order.length - 1}
                onClick={() => move(index, index + 1)}
              >
                ↓
              </button>
            </span>
          </li>
        ))}
      </ol>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="button" disabled={busy} onClick={() => void handleSubmit()}>
        {busy ? 'Saving…' : 'Submit my ranking'}
      </button>
    </>
  )
}

function Results({ night }: { night: NightView }) {
  if (night.results.length === 0) {
    return (
      <>
        <h1>No movie this time</h1>
        <p>Nobody suggested a movie. Maybe a board game?</p>
      </>
    )
  }

  const [winner, runnerUp] = night.results
  const winnerTitle = winner.year ? `${winner.title} (${winner.year})` : winner.title
  const tiedOnPoints = runnerUp !== undefined && runnerUp.points === winner.points
  // BordaScorer breaks a full tie by movie id, and movie ids are random, so it's a coin flip.
  const deadHeat = tiedOnPoints && runnerUp.firstPlaceVotes === winner.firstPlaceVotes

  return (
    <>
      <h1>You're watching {winnerTitle}</h1>
      {winner.posterUrl && <Poster url={winner.posterUrl} title={winner.title} size="large" />}
      <Facts details={winner.details} tmdbUrl={winner.tmdbUrl} />
      <p>
        {deadHeat
          ? `It was a dead heat, so ${winner.title} won a coin flip.`
          : tiedOnPoints
            ? `It was a tie on points. ${winner.title} had more first-place votes.`
            : `It won with ${winner.points} points.`}
      </p>
      <ol className="results">
        {night.results.map((result) => (
          <li key={result.movieId} className={result.isYours ? 'yours' : undefined}>
            <Poster url={result.posterUrl} title={result.title} />
            <span className="rank-title">
              {result.title}
              {result.year && <span className="year"> {result.year}</span>}
              {result.isYours && <span className="tag">your pick</span>}
            </span>
            <span className="points">{result.points === 1 ? '1 point' : `${result.points} points`}</span>
          </li>
        ))}
      </ol>
    </>
  )
}

function Progress({ night, token, onChange, onRefresh }: PhaseProps & { onRefresh: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const waiting = night.participantCount - night.answeredCount
  const verb = night.phase === 'collecting' ? 'suggested' : 'ranked'
  const nextStep = night.phase === 'collecting' ? 'ranking' : 'the results'

  async function handleAdvance() {
    if (!window.confirm(`Close this round now and go to ${nextStep} without the others?`)) return
    setBusy(true)
    setError('')
    try {
      onChange(await advanceNight(night.nightId, token))
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <aside className="progress">
      <p>
        {night.answeredCount} of {night.participantCount} have {verb}.
        {waiting > 0 && ` Waiting on ${waiting === 1 ? 'one more person' : `${waiting} more people`}.`}{' '}
        <button type="button" className="link" onClick={onRefresh}>
          Refresh
        </button>
      </p>

      {night.isOrganizer && (
        <>
          {night.waitingOn.length > 0 && <p className="waiting-on">Still waiting on {night.waitingOn.join(', ')}.</p>}
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="button" className="secondary" disabled={busy} onClick={() => void handleAdvance()}>
            {busy ? 'Closing…' : `Close this round and go to ${nextStep}`}
          </button>
        </>
      )}
    </aside>
  )
}

/**
 * A TMDB poster, or a plain placeholder of the same size when there isn't one, so a list where
 * TMDB found some movies and not others still lines up.
 */
function Poster({ url, title, size = 'small' }: { url: string; title: string; size?: 'small' | 'large' }) {
  const className = `poster poster-${size}`
  return url ? (
    <img className={className} src={url} alt={`Poster for ${title}`} loading="lazy" />
  ) : (
    <span className={`${className} poster-missing`} aria-hidden="true">
      🎬
    </span>
  )
}

/** "1h 44m", "45m", or empty for unknown. */
function runtime(minutes: number): string {
  if (minutes <= 0) return ''
  if (minutes < 60) return `${minutes}m`
  return minutes % 60 === 0 ? `${minutes / 60}h` : `${Math.floor(minutes / 60)}h ${minutes % 60}m`
}

/**
 * A movie's rating, runtime and genres, where it streams, and links to its trailer and TMDB page,
 * a line each, leaving out whatever TMDB didn't know. Nothing at all when it knew nothing.
 */
function Facts({ details, tmdbUrl }: { details: MovieDetails; tmdbUrl: string }) {
  const facts = [
    details.certification ? `Rated ${details.certification}` : '',
    runtime(details.runtimeMinutes),
    details.genres.join(', '),
  ].filter((part) => part !== '')

  const streaming = details.streamingOn.join(', ')

  const links = [
    { label: 'Trailer', url: details.trailerUrl },
    { label: 'TMDB', url: tmdbUrl },
  ].filter((link) => link.url !== '')

  if (facts.length === 0 && !streaming && links.length === 0) return null

  return (
    <span className="facts">
      {facts.length > 0 && <span className="facts-line">{facts.join(' · ')}</span>}
      {streaming && <span className="facts-line">On {streaming}</span>}
      {links.length > 0 && (
        <span className="facts-line">
          {links.map((link, index) => (
            <span key={link.label}>
              {index > 0 && ' · '}
              <a
                href={link.url}
                target="_blank"
                rel="noreferrer"
                draggable={false}
                onClick={(event) => event.stopPropagation()}
              >
                {link.label} ↗
              </a>
            </span>
          ))}
        </span>
      )}
    </span>
  )
}
