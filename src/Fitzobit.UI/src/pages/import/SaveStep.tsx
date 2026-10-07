import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, Save, ShieldCheck, XCircle } from 'lucide-react';
import { api, type ImportDetail } from '../../api';
import { useApi } from '../../hooks';
import { formatDateTime } from '../../format';
import { Card, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, useToast } from '../../components/feedback';
import { MiniStat, StepFooter, stepPath } from './shared';
import { ImportScopeSentence } from './ReviewStep';

export default function SaveStep({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  if (detail.status === 'Completed') return <SavedResult detail={detail} />;
  return <ConfirmSave detail={detail} onChanged={onChanged} />;
}

function ConfirmSave({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const review = useApi((signal) => api.review(detail.id, signal), [detail.id]);
  const [confirming, setConfirming] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const r = review.data;

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const result = await api.commit(detail.id);
      toast(`Import saved: ${result.customersCreated} customers created, ${result.assignmentsChanged} assignments`);
      setConfirming(false);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Saving failed');
      setConfirming(false);
      review.reload();
    } finally {
      setSaving(false);
    }
  };

  const newGroups = r?.groups.filter((g) => g.isNew).length ?? 0;
  const messages = r
    ? [
        `${r.summary.newCustomers.toLocaleString('en-IN')} new customer(s) will be created.`,
        ...(newGroups > 0 ? [`${newGroups} new delivery group(s) will be created.`] : []),
        `${(r.summary.toImport - r.unassigned - r.summary.keepingCurrentGroup).toLocaleString('en-IN')} customer(s) will be placed in a delivery group, with assignment history.`,
        ...(r.groupChangesForExistingCustomers > 0 ? [`${r.groupChangesForExistingCustomers} existing customer(s) will move to a different group.`] : []),
        ...(r.summary.keepingCurrentGroup > 0 ? [`${r.summary.keepingCurrentGroup} existing customer(s) keep their current group.`] : []),
        ...(r.unassigned > 0 ? [`${r.unassigned} customer(s) will be saved without a group.`] : []),
        `${(r.summary.errorRows + r.summary.excludedByManager).toLocaleString('en-IN')} row(s) will be skipped.`,
      ]
    : [];

  return (
    <>
      {review.loading && !r && <Loading />}
      {review.error && <ErrorPanel message={review.error} onRetry={review.reload} />}
      {error && (
        <div className="error-panel error-block">
          <XCircle size={18} />
          <div>
            <strong>Nothing was saved.</strong> {error}
          </div>
        </div>
      )}
      {r && (
        <Card className="save-card">
          <div className="save-hero">
            <ShieldCheck size={36} />
            <div>
              <h2>Ready to save</h2>
              <ImportScopeSentence review={r} />
              <p className="muted small">
                Everything is saved in a single transaction. If anything fails, nothing is saved and this draft stays as it is.
              </p>
            </div>
          </div>
          <ul className="confirm-list">
            {messages.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
          {r.blockers.map((b) => (
            <p key={b} className="text-danger">
              {b}
            </p>
          ))}
        </Card>
      )}

      <StepFooter>
        <button className="btn btn-secondary" onClick={() => navigate(stepPath(detail.id, 'review'))}>
          <ArrowLeft size={16} /> Back to review
        </button>
        <button className="btn btn-primary btn-lg" disabled={!r?.canSave || saving} onClick={() => setConfirming(true)}>
          <Save size={18} /> Confirm &amp; Save
        </button>
      </StepFooter>

      {confirming && (
        <ConfirmDialog
          title="Save this import?"
          messages={messages}
          confirmLabel="Confirm & Save"
          busy={saving}
          onCancel={() => setConfirming(false)}
          onConfirm={save}
        />
      )}
    </>
  );
}

function SavedResult({ detail }: { detail: ImportDetail }) {
  return (
    <Card className="save-card">
      <div className="save-hero success">
        <CheckCircle2 size={40} />
        <div>
          <h2>Import saved</h2>
          <p>
            {detail.fileName} was saved {formatDateTime(detail.completedAt)} in {(detail.saveDurationMs / 1000).toFixed(1)} s.
          </p>
        </div>
      </div>
      <div className="mini-stat-grid mini-stat-grid-5">
        <MiniStat label="Records imported" value={detail.importedCount} tone="info" />
        <MiniStat label="Customers created" value={detail.customersCreated} tone="success" />
        <MiniStat label="Existing matched" value={detail.existingCustomersMatched} />
        <MiniStat label="Groups created" value={detail.groupsCreated} />
        <MiniStat label="Assignments made" value={detail.assignmentsChanged} />
      </div>
      <div className="button-row">
        <Link className="btn btn-primary" to="/groups">
          View delivery groups
        </Link>
        <Link className="btn btn-secondary" to="/imports">
          Import history
        </Link>
        <Link className="btn btn-secondary" to="/upload">
          Upload another file
        </Link>
      </div>
    </Card>
  );
}
