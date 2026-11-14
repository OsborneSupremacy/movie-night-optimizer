import { useState, type FormEvent } from 'react'
import { ApiError, createNight } from '../api.ts'
import { formatDate } from '../dates.ts'

/** Today in the browser's own time zone, as yyyy-MM-dd. toISOString would give the UTC date. */
function today(): string {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

/** One address per line, or separated by commas or spaces. */
function parseEmails(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((email) => email.trim())
    .filter((email) => email.length > 0)
}

type Status =
  | { kind: 'idle' }
  | { kind: 'sending' }
  | { kind: 'sent'; invited: number; optedOut: number }
  | { kind: 'failed'; message: string }

export default function CreateNight() {
  const [organizerEmail, setOrganizerEmail] = useState('')
  const [participants, setParticipants] = useState('')
  const [date, setDate] = useState(today)
  const [status, setStatus] = useState<Status>({ kind: 'idle' })

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setStatus({ kind: 'sending' })
    try {
      const created = await createNight({
        organizerEmail: organizerEmail.trim(),
        participantEmails: parseEmails(participants),
        date,
      })
      setStatus({ kind: 'sent', invited: created.invitedCount, optedOut: created.optedOutCount })
    } catch (error) {
      setStatus({
        kind: 'failed',
        message: error instanceof ApiError ? error.message : 'Something went wrong. Try again in a moment.',
      })
    }
  }

  if (status.kind === 'sent') {
    return (
      <section>
        <p className="eyebrow">Movie night · {formatDate(date)}</p>
        <h1>Check your inbox</h1>
        <p>
          We've emailed {organizerEmail.trim()} to make sure the address is yours. Open the link in that email to send
          the invitations to the other {status.invited - 1 === 1 ? 'person' : `${status.invited - 1} people`}. Nobody
          else has been emailed yet.
        </p>
        {status.optedOut > 0 && (
          <p>
            {status.optedOut === 1 ? 'One address you added has' : `${status.optedOut} addresses you added have`} asked
            not to get email from Movie Night Optimizer, so {status.optedOut === 1 ? 'it was' : 'they were'} left out.
          </p>
        )}
      </section>
    )
  }

  return (
    <section>
      <h1>Plan a movie night</h1>
      <p>Everyone suggests one movie, then ranks the others. The best-ranked movie wins.</p>

      <form onSubmit={handleSubmit}>
        <label>
          Your email
          <input
            type="email"
            required
            autoComplete="email"
            value={organizerEmail}
            onChange={(event) => setOrganizerEmail(event.target.value)}
          />
        </label>

        <label>
          Who else is watching?
          <textarea
            required
            rows={5}
            placeholder="One email address per line"
            value={participants}
            onChange={(event) => setParticipants(event.target.value)}
          />
        </label>

        <label>
          When
          <input type="date" required value={date} onChange={(event) => setDate(event.target.value)} />
        </label>

        {status.kind === 'failed' && (
          <p className="error" role="alert">
            {status.message}
          </p>
        )}

        <button type="submit" disabled={status.kind === 'sending'} aria-busy={status.kind === 'sending'}>
          {status.kind === 'sending' ? 'Sending…' : 'Send invitations'}
        </button>
      </form>
    </section>
  )
}
