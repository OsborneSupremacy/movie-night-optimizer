import { Link, Route, Routes } from 'react-router-dom'
import CreateNight from './pages/CreateNight.tsx'
import Night from './pages/Night.tsx'
import StopEmails from './pages/StopEmails.tsx'

export default function App() {
  return (
    <div className="shell">
      <header className="masthead">
        <Link to="/">Movie Night Optimizer</Link>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<CreateNight />} />
          <Route path="/night/:nightId" element={<Night />} />
          <Route path="/night/:nightId/stop" element={<StopEmails />} />
          <Route path="*" element={<p>There's nothing at this address.</p>} />
        </Routes>
      </main>
      <footer className="credits">
        Movie details and posters from{' '}
        <a href="https://www.themoviedb.org/" target="_blank" rel="noreferrer">
          TMDB
        </a>
        . This product uses the TMDB API but is not endorsed or certified by TMDB. Streaming availability from{' '}
        <a href="https://www.justwatch.com/" target="_blank" rel="noreferrer">
          JustWatch
        </a>
        .
      </footer>
    </div>
  )
}
