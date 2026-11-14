import { Link, Route, Routes } from 'react-router-dom'
import CreateNight from './pages/CreateNight.tsx'
import Night from './pages/Night.tsx'

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
          <Route path="*" element={<p>There's nothing at this address.</p>} />
        </Routes>
      </main>
    </div>
  )
}
