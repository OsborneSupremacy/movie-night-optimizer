import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  advanceNight,
  ApiError,
  getNight,
  submitRanking,
  submitSuggestion,
  type MovieView,
  type NightView,
} from '../api.ts'

/**
 * The token from a link like /night/{nightId}#t={token}.
 *
 * It travels in the fragment because a fragment is never sent to a server, so it stays out of
 * CloudFront's logs and out of any Referer header.
 */
function readToken(): string {
  return new URLSearchParams(window.location.hash.replace(/^#/, '')).get('t') ?? ''
}

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Something went wrong. Try again in a moment.'
}

/** "Saturday, November 14" from yyyy-MM-dd, read as a calendar date rather than a UTC instant. */
function formatDate(date: string): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  })
}

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

      {night.phase === 'collecting' && <Collecting night={night} token={token} onChange={update} />}
      {night.phase === 'ranking' && <Ranking night={night} token={token} onChange={update} />}
      {night.phase === 'done' && <Results night={night} />}

      {night.phase !== 'done' && (
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
    return (
      <>
        <h1>You suggested {night.yourSuggestion}</h1>
        <p>When everyone has suggested a movie, you'll get an email to rank the others' picks.</p>
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
            <span className="rank-title">{movie.title}</span>
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

  const [winner] = night.results
  const tied = night.results.length > 1 && night.results[1].points === winner.points

  return (
    <>
      <h1>You're watching {winner.title}</h1>
      <p>
        {tied
          ? `It was a tie on points. ${winner.title} had more first-place votes.`
          : `It won with ${winner.points} points.`}
      </p>
      <ol className="results">
        {night.results.map((result) => (
          <li key={result.movieId} className={result.isYours ? 'yours' : undefined}>
            <span className="rank-title">
              {result.title}
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
