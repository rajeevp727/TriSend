import React from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'

function App() {
  return (
    <main className="shell">
      <header>
        <p className="eyebrow">TRISEND</p>
        <h1>Communications dashboard</h1>
        <p>Send and monitor transactional email through the TriSend API.</p>
      </header>

      <section className="grid">
        <article><strong>0</strong><span>Messages today</span></article>
        <article><strong>0</strong><span>Sent</span></article>
        <article><strong>0</strong><span>Failed</span></article>
        <article><strong>1</strong><span>Active channel</span></article>
      </section>

      <section className="panel">
        <h2>MVP workspace</h2>
        <p>Provider configuration, message history and usage analytics will appear here.</p>
      </section>
    </main>
  )
}

createRoot(document.getElementById('root')).render(
  <React.StrictMode><App /></React.StrictMode>
)
