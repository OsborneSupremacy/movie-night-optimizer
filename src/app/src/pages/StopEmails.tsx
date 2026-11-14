import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { ApiError, optOut } from '../api.ts'
import { readToken } from '../token.ts'

type State = { kind: 'idle' } | { kind: 'sending' } | { kind: 'done' } | { kind: 'failed'; message: string }

/**
 * Where the "don't email me again" link in every email lands.
 *
 * Opening it does nothing, like every other link, so a mail scanner can't unsubscribe anybody.
 * Pressing the button lists the inbox for good: no movie night, from anybody, will email it again.
 */
export default function StopEmails() {
  const { nightId = '' } = useParams()
  const [token] = useState(readToken)
  const [state, setState] = useState<State>({ kind: 'idle' })

  async function handleStop() {
    setState({ kind: 'sending' })
    try {
      await optOut(nightId, token)
      setState({ kind: 'done' })
    } catch (error) {
      setState({
        kind: 'failed',
        message: error instanceof ApiError ? error.message : 'Something went wrong. Try again in a moment.',
      })
    }
  }

  if (!token) {
    return (
      <p className="error" role="alert">
        This link is missing its key. Open the link from your email again.
      </p>
    )
  }

  if (state.kind === 'done') {
    return (
      <section>
        <h1>You won't hear from us again</h1>
        <p>
          Movie Night Optimizer won't email this address again, for this movie night or any other. Nobody else's night
          is held up waiting for you.
        </p>
      </section>
    )
  }

  return (
    <section>
      <h1>Stop these emails?</h1>
      <p>
        Somebody added your address to a movie night. If you don't know them, or just don't want these emails, press the
        button and Movie Night Optimizer will never email this address again, from this night or any other.
      </p>
      {state.kind === 'failed' && (
        <p className="error" role="alert">
          {state.message}
        </p>
      )}
      <button
        type="button"
        disabled={state.kind === 'sending'}
        aria-busy={state.kind === 'sending'}
        onClick={() => void handleStop()}
      >
        {state.kind === 'sending' ? 'Stopping…' : 'Stop emailing me'}
      </button>
    </section>
  )
}
