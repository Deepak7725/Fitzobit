import { createContext, useCallback, useContext, useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { KeyRound, LogIn, ShieldCheck } from 'lucide-react';
import { api, UNAUTHORIZED_EVENT, type AuthState, type SignedInUser } from './api';

interface AuthContextValue {
  user: SignedInUser;
  isAdmin: boolean;
  signOut: () => Promise<void>;
  /** Re-reads the session (and a CSRF token bound to it) after the password changed. */
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>.');
  return value;
}

/**
 * Gate for the whole app: shows first-run setup, the sign-in page, or the app for a signed-in user.
 * Any 401 from the API (expired session, deactivated user) returns to the sign-in page.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);

  const refresh = useCallback(async () => {
    try {
      setState(await api.authState());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The server could not be reached.');
    }
  }, []);

  useEffect(() => {
    void refresh();
    const onUnauthorized = () => {
      setExpired(true);
      void refresh();
    };
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
  }, [refresh]);

  const signOut = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      setExpired(false);
      await refresh();
    }
  }, [refresh]);

  if (!state) {
    return (
      <AuthScreen>
        {error ? (
          <div className="auth-error" role="alert">
            {error}
            <button className="btn btn-secondary btn-sm" onClick={() => void refresh()}>
              Retry
            </button>
          </div>
        ) : (
          <div className="loading">
            <span className="spinner" aria-hidden /> Connecting…
          </div>
        )}
      </AuthScreen>
    );
  }

  if (state.setupRequired)
    return <SetupScreen allowed={state.setupAllowedHere} codeRequired={state.setupCodeRequired} onDone={refresh} />;
  if (!state.authenticated || !state.user)
    return <LoginScreen expired={expired} onDone={async () => { setExpired(false); await refresh(); }} />;

  return (
    <AuthContext.Provider value={{ user: state.user, isAdmin: state.user.role === 'Admin', signOut, refresh }}>
      {children}
    </AuthContext.Provider>
  );
}

function AuthScreen({ children, title, subtitle }: { children: ReactNode; title?: string; subtitle?: string }) {
  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-brand">
          <img src="/favicon.svg" alt="" width={40} height={40} />
          <div>
            <span className="brand-name">Fitzobit</span>
            <span className="brand-tag">Delivery Management</span>
          </div>
        </div>
        {title && <h1>{title}</h1>}
        {subtitle && <p className="muted">{subtitle}</p>}
        {children}
      </div>
    </div>
  );
}

function LoginScreen({ expired, onDone }: { expired: boolean; onDone: () => Promise<void> }) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.login(userName, password);
      await onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed.');
      setPassword('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthScreen title="Sign in" subtitle="Use the account your administrator created for you.">
      {expired && !error && <div className="auth-note">Your session has ended. Please sign in again.</div>}
      <form onSubmit={submit} className="auth-form">
        <label className="field">
          User name
          <input className="input" autoComplete="username" autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} required />
        </label>
        <label className="field">
          Password
          <input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
        <button className="btn btn-primary btn-lg" disabled={busy || !userName || !password}>
          <LogIn size={17} /> {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </AuthScreen>
  );
}

function SetupScreen({ allowed, codeRequired, onDone }: { allowed: boolean; codeRequired: boolean; onDone: () => Promise<void> }) {
  const [setupCode, setSetupCode] = useState('');
  const [userName, setUserName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!allowed)
    return (
      <AuthScreen title="Setup required" subtitle="No administrator account exists yet.">
        <div className="auth-note">
          <ShieldCheck size={16} /> For security, the first administrator can only be created from a browser on the server itself
          (http://localhost), or with a setup code configured on the server (Auth__SetupCode).
        </div>
      </AuthScreen>
    );

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (password !== confirm) {
      setError('The passwords do not match.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.setup(userName, displayName, password, codeRequired ? setupCode : undefined);
      await onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Setup failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthScreen title="Create the administrator" subtitle="First-run setup: this account can manage users and settings. It is shown only once.">
      <form onSubmit={submit} className="auth-form">
        {codeRequired && (
          <label className="field">
            Setup code
            <input className="input" autoComplete="off" autoFocus value={setupCode} onChange={(e) => setSetupCode(e.target.value)} required />
            <small className="field-hint">The Auth__SetupCode value from the server's settings (on Render: your service → Environment).</small>
          </label>
        )}
        <label className="field">
          Your name
          <input className="input" autoComplete="name" autoFocus={!codeRequired} value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        </label>
        <label className="field">
          User name
          <input className="input" autoComplete="username" value={userName} onChange={(e) => setUserName(e.target.value)} required />
          <small className="field-hint">3–50 characters: letters, numbers, “.”, “_” or “-”.</small>
        </label>
        <label className="field">
          Password
          <input className="input" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          <small className="field-hint">At least 10 characters with letters and numbers, not containing the user name.</small>
        </label>
        <label className="field">
          Confirm password
          <input className="input" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required />
        </label>
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
        <button className="btn btn-primary btn-lg" disabled={busy}>
          <KeyRound size={17} /> {busy ? 'Creating…' : 'Create administrator and sign in'}
        </button>
      </form>
    </AuthScreen>
  );
}
