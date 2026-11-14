import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ApiError, getNight } from '../api.ts'

/**
 * The token from a link like /night/{nightId}#t={token}.
 *
 * It travels in the fragment because a fragment is never sent to a server, so it stays out of
 * CloudFront's logs and out of any Referer header.
 */
function readToken(): string {
  return new URLSearchParams(window.location.hash.replace(/^#/, '')).get('t') ?? ''
}

type State =
  | { kind: 'loading' }
  | { kind: 'loaded'; night: unknown }
  | { kind: 'failed'; message: string }

/**
 * The one page every emailed link opens. What it shows depends on the night's phase and on
 * whether this participant has already answered.
 *
 * Loading it only reads. Suggesting and ranking happen when a person presses a button, so a mail
 * scanner that follows the link in an email can't answer on anybody's behalf.
 *
 * TODO, once GetNightService returns the participant's view:
 * - Collecting: one text box for a movie, or "waiting on N others" once they've suggested.
 * - Ranking: the other suggestions in a list to drag into order, never their own.
 * - Done: the results, winner first.
 * - Organizer: a button to close the current round early.
 */
export default function Night() {
  const { nightId = '' } = useParams()
  const [state, setState] = useState<State>({ kind: 'loading' })

  useEffect(() => {
    const token = readToken()

    if (!token) {
      setState({ kind: 'failed', message: 'This link is missing its key. Open the link from your email again.' })
      return
    }

    let cancelled = false

    getNight(nightId, token)
      .then((night) => {
        if (!cancelled) setState({ kind: 'loaded', night })
      })
      .catch((error: unknown) => {
        if (cancelled) return
        setState({
          kind: 'failed',
          message: error instanceof ApiError ? error.message : 'Something went wrong. Try again in a moment.',
        })
      })

    return () => {
      cancelled = true
    }
  }, [nightId])

  if (state.kind === 'loading') return <p>Loading…</p>

  if (state.kind === 'failed') {
    return (
      <p className="error" role="alert">
        {state.message}
      </p>
    )
  }

  return (
    <section>
      <h1>Movie night</h1>
      <pre>{JSON.stringify(state.night, null, 2)}</pre>
    </section>
  )
}
