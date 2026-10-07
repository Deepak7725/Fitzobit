import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ChevronDown, KeyRound, LogOut } from 'lucide-react';
import { api } from '../api';
import { useAuth } from '../auth';
import { Modal, useToast } from './feedback';

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('');
}

export default function UserMenu() {
  const { user, signOut } = useAuth();
  const [open, setOpen] = useState(false);
  const [changing, setChanging] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false);
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', esc);
    };
  }, [open]);

  return (
    <div className="user-menu" ref={ref}>
      <button className="user-chip" onClick={() => setOpen((o) => !o)} aria-haspopup="menu" aria-expanded={open} title={user.userName}>
        <span className="avatar">{initials(user.displayName)}</span>
        <span className="user-text">
          <strong>{user.displayName}</strong>
          <small>{user.role}</small>
        </span>
        <ChevronDown size={16} className="muted" />
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="menu-header">
            <strong>{user.displayName}</strong>
            <small className="muted">
              {user.userName} · {user.role}
            </small>
          </div>
          <button role="menuitem" onClick={() => { setOpen(false); setChanging(true); }}>
            <KeyRound size={15} /> Change password
          </button>
          <button role="menuitem" onClick={() => void signOut()}>
            <LogOut size={15} /> Sign out
          </button>
        </div>
      )}
      {changing && <ChangePasswordDialog onClose={() => setChanging(false)} />}
    </div>
  );
}

function ChangePasswordDialog({ onClose }: { onClose: () => void }) {
  const { refresh } = useAuth();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (next !== confirm) {
      setError('The new passwords do not match.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.changePassword(current, next);
      await refresh();
      toast('Password changed. Other sessions were signed out.');
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The password could not be changed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Change password"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="change-password" disabled={busy || !current || !next}>
            {busy ? 'Saving…' : 'Change password'}
          </button>
        </>
      }
    >
      <form id="change-password" onSubmit={submit}>
        <label className="field">
          Current password
          <input className="input" type="password" autoComplete="current-password" autoFocus value={current} onChange={(e) => setCurrent(e.target.value)} />
        </label>
        <label className="field">
          New password
          <input className="input" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
          <small className="field-hint">At least 10 characters with letters and numbers, not containing your user name.</small>
        </label>
        <label className="field">
          Confirm new password
          <input className="input" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </label>
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
      </form>
    </Modal>
  );
}
