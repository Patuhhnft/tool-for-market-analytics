import { useEffect, useState } from 'react'
import { NavLink, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { AdminPanel } from './components/AdminPanel'
import { Board } from './components/Board'
import { Diagnostics } from './components/Diagnostics'
import { Explore } from './components/Explore'
import { OperatorBadge, OperatorGate } from './components/OperatorGate'
import { clearActiveOperator, getActiveOperator, setActiveOperator, type ActiveOperator } from './lib/operator'
import './App.css'
import './styles/controls.css'

// Cada seção tem um endereço próprio. "Explorar oportunidades" precisa disso de verdade: os
// filtros vivem na query string, e sem rota não haveria onde guardá-los.
const sections = [
  { path: '/oportunidades', label: 'Oportunidades' },
  { path: '/explorar', label: 'Explorar' },
  { path: '/administracao', label: 'Administração' },
  { path: '/diagnostico', label: 'Diagnóstico' }
]

const tabStorage = 'leitor.tab'

// Os endereços antigos eram âncoras (#oportunidades). Links já salvos continuam funcionando.
const legacyHashes: Record<string, string> = {
  '#oportunidades': '/oportunidades',
  '#administracao': '/administracao',
  '#diagnostico': '/diagnostico'
}

function App() {
  // Quem está trabalhando. Enquanto for null, a tela de seleção toma o lugar do painel: o
  // backend recusa escrita não assinada, e sem isto o usuário só descobriria isso batendo
  // num erro 400 ao salvar.
  const [operator, setOperator] = useState<ActiveOperator | null>(getActiveOperator)

  const choose = (chosen: ActiveOperator) => {
    setActiveOperator(chosen)
    setOperator(chosen)
  }

  const switchOperator = () => {
    clearActiveOperator()
    setOperator(null)
  }

  return (
    <OperatorGate operator={operator} onChosen={choose}>
    <main className="app-shell">
      <header className="topbar">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">LM</span>
          <div>
            <p className="eyebrow">Leitor do Mercado Livre</p>
            <p className="brand-subtitle">Preço, demanda e margem para decidir o que importar</p>
          </div>
        </div>
        {operator && <OperatorBadge operator={operator} onSwitch={switchOperator} />}
        <nav className="tabs" aria-label="Seções">
          {sections.map((section) => (
            <NavLink key={section.path} to={section.path} className={({ isActive }) => `tab ${isActive ? 'is-active' : ''}`}>
              {section.label}
            </NavLink>
          ))}
        </nav>
      </header>

      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/oportunidades" element={<Remember path="/oportunidades"><Board /></Remember>} />
        <Route path="/explorar" element={<Remember path="/explorar"><Explore /></Remember>} />
        <Route path="/administracao" element={<Remember path="/administracao"><AdminPanel /></Remember>} />
        <Route path="/diagnostico" element={<Remember path="/diagnostico"><Diagnostics /></Remember>} />
        <Route path="*" element={<Navigate to="/oportunidades" replace />} />
      </Routes>

      <footer className="app-footer">
        <span>Leitor do Mercado Livre</span>
        <span>Visita não é venda · o índice é prioridade, não lucro</span>
      </footer>
    </main>
    </OperatorGate>
  )
}

/** A raiz manda para a âncora antiga, se houver, ou para a última seção aberta. */
function Landing() {
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => {
    const legacy = legacyHashes[location.hash]
    if (legacy) {
      navigate(legacy, { replace: true })
      return
    }

    navigate(lastSection(), { replace: true })
  }, [location.hash, navigate])

  return null
}

/** Lembra a seção aberta. Sem armazenamento, só não lembra — a tela não muda em nada. */
function Remember({ path, children }: { path: string; children: React.ReactNode }) {
  useEffect(() => {
    try {
      localStorage.setItem(tabStorage, path)
    } catch {
      // Conveniência; nada depende disso.
    }
  }, [path])

  return <>{children}</>
}

function lastSection(): string {
  try {
    const saved = localStorage.getItem(tabStorage)
    return sections.some((section) => section.path === saved) ? saved! : '/oportunidades'
  } catch {
    return '/oportunidades'
  }
}

export default App
