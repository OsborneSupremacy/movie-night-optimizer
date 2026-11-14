import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { normalizeQuery, searchMovies, type MovieOption } from '../api.ts'

/** How long typing has to pause before a search. Long enough that a fast typist makes a few requests per title. */
const DEBOUNCE_MS = 250

/** Fewer letters than this match too much to be worth a request. Mirrors MovieSearchService.MinQueryLength. */
const MIN_QUERY_LENGTH = 2

/**
 * Answers already fetched, kept for the life of the page. A backspace and a retype of the same
 * letters costs nothing, which matters more than memory for a list this small.
 */
const answers = new Map<string, MovieOption[]>()

interface MovieInputProps {
  value: string
  /** The movie picked from the list, or 0 while the title is just typed. */
  pickedId: number
  onType: (value: string) => void
  onPick: (movie: MovieOption) => void
}

/**
 * The suggest box, with TMDB movies offered as you type.
 *
 * Purely a help: picking pins the exact film (the 1984 Dune, not the 2021 one), but typing and
 * submitting work the same as ever when the list is empty, slow, rate limited or failing. Any
 * error just means no suggestions.
 */
export default function MovieInput({ value, pickedId, onType, onPick }: MovieInputProps) {
  const [options, setOptions] = useState<MovieOption[]>([])
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const listId = useId()
  const closeTimer = useRef<number | undefined>(undefined)

  const query = normalizeQuery(value)

  useEffect(() => {
    // Nothing to offer for a pick, which is already the movie, or for a couple of letters.
    if (pickedId > 0 || query.length < MIN_QUERY_LENGTH) {
      setOptions([])
      return
    }

    const known = answers.get(query)
    if (known) {
      setOptions(known)
      setActive(-1)
      return
    }

    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      searchMovies(query, controller.signal)
        .then((movies) => {
          answers.set(query, movies)
          setOptions(movies)
          setActive(-1)
        })
        .catch(() => {
          // Aborted because typing moved on, or the search failed. Either way: no suggestions.
          if (!controller.signal.aborted) setOptions([])
        })
    }, DEBOUNCE_MS)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [query, pickedId])

  const showList = open && options.length > 0

  function pick(movie: MovieOption) {
    onPick(movie)
    setOpen(false)
    setActive(-1)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!showList) return
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActive((index) => (index + 1) % options.length)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActive((index) => (index <= 0 ? options.length - 1 : index - 1))
    } else if (event.key === 'Enter' && active >= 0) {
      // Picks rather than submits; a second Enter submits.
      event.preventDefault()
      pick(options[active])
    } else if (event.key === 'Escape') {
      setOpen(false)
    }
  }

  return (
    <div className="movie-input">
      <input
        required
        maxLength={100}
        autoComplete="off"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={showList}
        aria-controls={listId}
        aria-activedescendant={showList && active >= 0 ? `${listId}-${active}` : undefined}
        value={value}
        onChange={(event) => {
          onType(event.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => {
          // Late enough for a tap on an option to land first.
          closeTimer.current = window.setTimeout(() => setOpen(false), 150)
        }}
        onKeyDown={handleKeyDown}
      />
      {showList && (
        <ul className="movie-options" role="listbox" id={listId}>
          {options.map((movie, index) => (
            <li
              key={movie.tmdbId}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === active}
              className={index === active ? 'active' : undefined}
              onMouseDown={(event) => {
                // Keeps focus in the input, so blur doesn't close the list before the pick.
                event.preventDefault()
                window.clearTimeout(closeTimer.current)
                pick(movie)
              }}
            >
              {movie.posterUrl ? (
                <img src={movie.posterUrl} alt="" className="option-poster" />
              ) : (
                <span className="option-poster poster-missing" aria-hidden="true">
                  🎬
                </span>
              )}
              <span>
                {movie.title}
                {movie.year && <span className="year"> {movie.year}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
