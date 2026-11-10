import { useEffect, useState } from 'react'
import { AlertCircle, Check, Settings } from 'lucide-react'
import client from '../../api/client'
import { useAuth } from '../../auth/AuthContext'

interface Parameter {
  kulcs: string
  ertek: string
  leiras?: string
  modositasIdeje: string
}

function getErrorMessage(error: unknown, fallback: string): string {
  const axErr = error as { response?: { data?: { message?: string } } }
  return axErr.response?.data?.message || fallback
}

export default function ParametersPage() {
  const { user } = useAuth()
  const canEdit = user?.role === 'Adminisztrator'
  const [parameters, setParameters] = useState<Parameter[]>([])
  const [values, setValues] = useState<Record<string, string>>({})
  const [isLoading, setIsLoading] = useState(true)
  const [savingKey, setSavingKey] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  useEffect(() => {
    let ignore = false
    client.get<Parameter[]>('/parameters')
      .then((response) => {
        if (!ignore) {
          setParameters(response.data)
          setValues(Object.fromEntries(response.data.map((parameter) => [parameter.kulcs, parameter.ertek])))
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!ignore) setError(getErrorMessage(err, 'A rendszerparaméterek betöltése sikertelen.'))
      })
      .finally(() => {
        if (!ignore) setIsLoading(false)
      })
    return () => { ignore = true }
  }, [])

  const saveParameter = async (parameter: Parameter) => {
    const value = values[parameter.kulcs]?.trim() ?? ''
    if (!value) {
      setError('A paraméter értéke nem lehet üres.')
      return
    }
    setSavingKey(parameter.kulcs)
    try {
      const response = await client.put<Parameter>(`/parameters/${encodeURIComponent(parameter.kulcs)}`, { ertek: value })
      setParameters((current) => current.map((item) => item.kulcs === parameter.kulcs ? response.data : item))
      setValues((current) => ({ ...current, [parameter.kulcs]: response.data.ertek }))
      setError('')
      setSuccess('A rendszerparaméter mentése sikeres.')
      setTimeout(() => setSuccess(''), 3000)
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'A rendszerparaméter mentése sikertelen.'))
    } finally {
      setSavingKey(null)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            <Settings size={20} strokeWidth={1.5} color="var(--color-primary)" />
            <span>Rendszerparaméterek</span>
          </h1>
          <p className="page-subtitle">A backend által engedélyezett működési paraméterek kezelése</p>
        </div>
      </div>
      {error && <div className="alert alert-error" role="alert"><AlertCircle size={16} strokeWidth={1.5} /><span>{error}</span></div>}
      {success && <div className="alert alert-success" role="status"><Check size={16} strokeWidth={1.5} /><span>{success}</span></div>}
      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}><span className="spinner spinner-primary" style={{ width: 24, height: 24 }} /></div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead><tr><th>Paraméter</th><th>Leírás</th><th>Érték</th><th style={{ textAlign: 'right' }}>Művelet</th></tr></thead>
            <tbody>
              {parameters.map((parameter) => (
                <tr key={parameter.kulcs}>
                  <td><code>{parameter.kulcs}</code></td>
                  <td>{parameter.leiras || '—'}</td>
                  <td><input className="form-input" aria-label={`${parameter.kulcs} értéke`} value={values[parameter.kulcs] ?? ''} disabled={!canEdit} onChange={(event) => setValues((current) => ({ ...current, [parameter.kulcs]: event.target.value }))} /></td>
                  <td style={{ textAlign: 'right' }}>{canEdit && <button type="button" className="btn btn-primary btn-sm" disabled={savingKey === parameter.kulcs} onClick={() => saveParameter(parameter)}>{savingKey === parameter.kulcs ? 'Mentés…' : 'Mentés'}</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
