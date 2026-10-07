import { useState, type FormEvent } from 'react';
import { KeyRound, Pencil, Plus, UserRound } from 'lucide-react';
import { api, type AppUser } from '../../api';
import { useApi } from '../../hooks';
import { useAuth } from '../../auth';
import { formatDateTime } from '../../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { Modal, useToast } from '../../components/feedback';

const ROLE_HELP: Record<string, string> = {
  Admin: 'Everything, including users, plans, catalogue re-sync and the audit log.',
  Manager: 'Upload, customers, delivery groups, export and catalogue (view).',
};

/** User administration (Admin only; the API enforces this too). */
export default function UsersTab() {
  const { user: me } = useAuth();
  const users = useApi(api.users);
  const [editing, setEditing] = useState<AppUser | 'new' | null>(null);
  const [resetting, setResetting] = useState<AppUser | null>(null);

  return (
    <Card
      title={<><UserRound size={18} /> Users</>}
      subtitle="People who can sign in. Passwords are stored only as salted hashes."
      actions={
        <button className="btn btn-primary btn-sm" onClick={() => setEditing('new')}>
          <Plus size={15} /> Add user
        </button>
      }
    >
      {users.error && <ErrorPanel message={users.error} onRetry={users.reload} />}
      {users.loading && !users.data && <Loading label="Loading users…" />}
      {users.data && users.data.length === 0 && <EmptyState title="No users" message="Add a user to let them sign in." />}
      {users.data && users.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>User</th>
                <th>Role</th>
                <th>Status</th>
                <th>Last sign-in</th>
                <th>Created</th>
                <th className="actions-cell" aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {users.data.map((u) => (
                <tr key={u.id}>
                  <td>
                    <div className="item-name">
                      {u.displayName}
                      {u.id === me.id && <span className="muted small"> (you)</span>}
                    </div>
                    <div className="item-sub">{u.userName}</div>
                  </td>
                  <td>
                    <Badge tone={u.role === 'Admin' ? 'info' : 'neutral'}>{u.role}</Badge>
                  </td>
                  <td>
                    {u.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>}{' '}
                    {u.isLockedOut && <Badge tone="warning">Locked out</Badge>}
                  </td>
                  <td>{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : <span className="muted">Never</span>}</td>
                  <td>{formatDateTime(u.createdAt)}</td>
                  <td className="actions-cell">
                    <button className="icon-btn" title="Edit" aria-label={`Edit ${u.userName}`} onClick={() => setEditing(u)}>
                      <Pencil size={15} />
                    </button>
                    <button className="icon-btn" title="Reset password" aria-label={`Reset password for ${u.userName}`} onClick={() => setResetting(u)}>
                      <KeyRound size={15} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing && (
        <UserDialog
          user={editing === 'new' ? null : editing}
          isSelf={editing !== 'new' && editing.id === me.id}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            users.reload();
          }}
        />
      )}
      {resetting && <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} onSaved={() => { setResetting(null); users.reload(); }} />}
    </Card>
  );
}

function UserDialog({ user, isSelf, onClose, onSaved }: { user: AppUser | null; isSelf: boolean; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [userName, setUserName] = useState(user?.userName ?? '');
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [role, setRole] = useState(user?.role ?? 'Manager');
  const [isActive, setIsActive] = useState(user?.isActive ?? true);
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (user) await api.updateUser(user.id, displayName, role, isActive);
      else await api.createUser(userName, displayName, role, password);
      toast(user ? `${user.userName} updated.` : `User ${userName.trim().toLowerCase()} created.`);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The user could not be saved.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={user ? `Edit ${user.userName}` : 'Add user'}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="user-form" disabled={busy}>
            {busy ? 'Saving…' : user ? 'Save' : 'Create user'}
          </button>
        </>
      }
    >
      <form id="user-form" onSubmit={submit}>
        {!user && (
          <label className="field">
            User name
            <input className="input" autoFocus autoComplete="off" value={userName} onChange={(e) => setUserName(e.target.value)} required />
            <small className="field-hint">Used to sign in. 3–50 characters: letters, numbers, “.”, “_” or “-”. Cannot be changed later.</small>
          </label>
        )}
        <label className="field">
          Display name
          <input className="input" autoFocus={!!user} value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        </label>
        <label className="field">
          Role
          <select className="select select-full" value={role} onChange={(e) => setRole(e.target.value)} disabled={isSelf}>
            <option>Manager</option>
            <option>Admin</option>
          </select>
          <small className="field-hint">{isSelf ? 'You cannot change your own role.' : ROLE_HELP[role]}</small>
        </label>
        {user && (
          <label className="toggle toggle-field">
            <input type="checkbox" checked={isActive} disabled={isSelf} onChange={(e) => setIsActive(e.target.checked)} />
            Active (can sign in){isSelf ? ' — you cannot deactivate yourself' : ''}
          </label>
        )}
        {!user && (
          <label className="field">
            Initial password
            <input className="input" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
            <small className="field-hint">At least 10 characters with letters and numbers. Share it securely; the user can change it after signing in.</small>
          </label>
        )}
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
      </form>
    </Modal>
  );
}

function ResetPasswordDialog({ user, onClose, onSaved }: { user: AppUser; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.resetPassword(user.id, password);
      toast(`Password reset for ${user.userName}. Their other sessions were signed out.`);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The password could not be reset.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Reset password for ${user.userName}`}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="reset-form" disabled={busy || !password}>
            {busy ? 'Saving…' : 'Reset password'}
          </button>
        </>
      }
    >
      <form id="reset-form" onSubmit={submit}>
        <label className="field">
          New password
          <input className="input" type="password" autoComplete="new-password" autoFocus value={password} onChange={(e) => setPassword(e.target.value)} />
          <small className="field-hint">This also unlocks the account and signs the user out everywhere.</small>
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
