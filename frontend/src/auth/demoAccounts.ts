// Bemutatóhoz használt demófiókok. A backend csak DemoUsers:Enabled=true esetén hozza létre őket,
// ezért a gyorskitöltő gombok is csak fejlesztéskor vagy VITE_DEMO_ACCOUNTS=true buildnél látszanak.
export const DEMO_ACCOUNTS = [
  { label: 'Oktató', username: 'demo-oktato', pass: 'DemoOktato2026!' },
  { label: 'Portás', username: 'demo-portas', pass: 'DemoPortas2026!' },
  { label: 'Adminisztrátor', username: 'demo-admin', pass: 'DemoAdmin2026!' },
  { label: 'Igazgató', username: 'demo-igazgato', pass: 'DemoIgazgato2026!' },
]

export const SHOW_DEMO_ACCOUNTS =
  import.meta.env.DEV || import.meta.env.VITE_DEMO_ACCOUNTS === 'true'
