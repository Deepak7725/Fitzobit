import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ArrowRight, Info, Pencil, Plus, Trash2 } from 'lucide-react';
import { api, type ImportDetail, type ImportGroupSummary } from '../../api';
import { useApi } from '../../hooks';
import { Badge, Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, Modal, useToast } from '../../components/feedback';
import { BarList, StepFooter, stepPath } from './shared';

export default function GroupsStep({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const groups = useApi((signal) => api.importGroups(detail.id, signal), [detail.id]);
  const classification = useApi((signal) => api.classification(detail.id, signal), [detail.id]);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<ImportGroupSummary | null>(null);
  const [deleting, setDeleting] = useState<ImportGroupSummary | null>(null);
  const editable = detail.status === 'Validated';

  const add = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    try {
      const created = await api.createDraftGroup(detail.id, name, description || null);
      toast(`Group “${created.name}” added`);
      setName('');
      setDescription('');
      groups.reload();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Could not add the group', 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!deleting) return;
    setBusy(true);
    try {
      const { unassigned } = await api.deleteDraftGroup(detail.id, Number(deleting.key.slice(2)));
      toast(`Group “${deleting.name}” removed${unassigned ? `; ${unassigned} customer(s) are now unassigned` : ''}`);
      setDeleting(null);
      groups.reload();
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Could not remove the group', 'error');
    } finally {
      setBusy(false);
    }
  };

  const drafts = (groups.data ?? []).filter((g) => g.isNew);
  const existing = (groups.data ?? []).filter((g) => !g.isNew);

  return (
    <>
      <div className="alert alert-info">
        <Info size={18} />
        <div>
          <strong>You decide the groups.</strong> A delivery group is not the same as a location: one group can cover several areas, pincodes
          and plans. For example, ten different addresses could become two groups or ten. Create the groups you need here, or use existing
          ones, then assign customers in the next step.
        </div>
      </div>

      <div className="grid-main-side">
        <div className="stack">
          {editable && (
            <Card title="Add a delivery group">
              <form className="inline-form" onSubmit={add}>
                <label className="field">
                  <span>Group name</span>
                  <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Coimbatore North" maxLength={150} required />
                </label>
                <label className="field field-grow">
                  <span>Description (optional)</span>
                  <input className="input" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Areas, route or timing notes" maxLength={1000} />
                </label>
                <button className="btn btn-primary" disabled={busy || !name.trim()}>
                  <Plus size={16} /> Add group
                </button>
              </form>
            </Card>
          )}

          <Card title={`New groups in this import (${drafts.length})`} subtitle="Created as real delivery groups when you save">
            {groups.loading && !groups.data && <Loading />}
            {groups.error && <ErrorPanel message={groups.error} onRetry={groups.reload} />}
            {groups.data && drafts.length === 0 && (
              <EmptyState title="No new groups yet" message="Add a group above, or assign customers to the existing groups listed below." />
            )}
            {drafts.length > 0 && <GroupTable groups={drafts} editable={editable} onEdit={setEditing} onDelete={setDeleting} />}
          </Card>

          <Card title={`Existing delivery groups (${existing.length})`} subtitle="Active groups already saved. Manage them on the Delivery Groups page">
            {groups.data && existing.length === 0 && <p className="muted">No saved groups yet.</p>}
            {existing.length > 0 && <GroupTable groups={existing} editable={false} />}
          </Card>
        </div>

        <div className="stack">
          <Card title="Pincodes in this file" subtitle="For reference while naming groups">
            {classification.data ? <BarList items={classification.data.byPincode} total={classification.data.includedRows} limit={12} empty="No pincodes detected" /> : <Loading />}
          </Card>
          <Card title="Areas in this file">
            {classification.data ? <BarList items={classification.data.byLocality} total={classification.data.includedRows} limit={12} empty="No areas detected" /> : <Loading />}
          </Card>
        </div>
      </div>

      <StepFooter note={drafts.length + existing.length === 0 ? 'Add at least one group to be able to assign customers.' : undefined}>
        <button className="btn btn-secondary" onClick={() => navigate(stepPath(detail.id, 'classify'))}>
          <ArrowLeft size={16} /> Back
        </button>
        <button className="btn btn-primary" onClick={() => navigate(stepPath(detail.id, 'assign'))}>
          Next: Assign customers <ArrowRight size={16} />
        </button>
      </StepFooter>

      {editing && (
        <EditDraftDialog
          batchId={detail.id}
          group={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            groups.reload();
          }}
        />
      )}
      {deleting && (
        <ConfirmDialog
          title={`Remove “${deleting.name}”?`}
          messages={[
            deleting.customerCount > 0
              ? `${deleting.customerCount} customer(s) assigned to it in this import will become unassigned.`
              : 'No customers are assigned to it yet.',
          ]}
          confirmLabel="Remove group"
          danger
          busy={busy}
          onCancel={() => setDeleting(null)}
          onConfirm={remove}
        />
      )}
    </>
  );
}

function GroupTable({ groups, editable, onEdit, onDelete }: {
  groups: ImportGroupSummary[];
  editable: boolean;
  onEdit?: (g: ImportGroupSummary) => void;
  onDelete?: (g: ImportGroupSummary) => void;
}) {
  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Group</th>
            <th className="num">From this file</th>
            <th>Pincodes</th>
            <th className="num">Locations</th>
            <th>Plans</th>
            {editable && <th aria-label="Actions" />}
          </tr>
        </thead>
        <tbody>
          {groups.map((g) => (
            <tr key={g.key}>
              <td>
                <div className="item-name">
                  {g.name} {g.isNew ? <Badge tone="info">New</Badge> : <Badge tone="neutral">Existing</Badge>}
                </div>
                {g.description && <div className="item-sub">{g.description}</div>}
                {!g.isNew && <div className="item-sub">{g.existingMembersOutsideImport} customer(s) already in this group</div>}
              </td>
              <td className="num strong">{g.customerCount}</td>
              <td>
                <Chips values={g.pincodes} more={g.pincodeCount - g.pincodes.length} />
              </td>
              <td className="num">{g.locationCount}</td>
              <td>
                <Chips values={g.plans} more={g.planCount - g.plans.length} />
              </td>
              {editable && (
                <td className="actions-cell">
                  <button className="icon-btn" onClick={() => onEdit?.(g)} aria-label={`Rename ${g.name}`}>
                    <Pencil size={15} />
                  </button>
                  <button className="icon-btn danger" onClick={() => onDelete?.(g)} aria-label={`Remove ${g.name}`}>
                    <Trash2 size={15} />
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Chips({ values, more = 0 }: { values: string[]; more?: number }) {
  if (values.length === 0) return <span className="muted">—</span>;
  return (
    <div className="tag-cloud">
      {values.map((v) => (
        <span key={v} className="tag">
          {v}
        </span>
      ))}
      {more > 0 && <span className="tag tag-more">+{more}</span>}
    </div>
  );
}

function EditDraftDialog({ batchId, group, onClose, onSaved }: { batchId: number; group: ImportGroupSummary; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [name, setName] = useState(group.name);
  const [description, setDescription] = useState(group.description ?? '');
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setBusy(true);
    try {
      await api.updateDraftGroup(batchId, Number(group.key.slice(2)), name, description || null);
      toast('Group updated');
      onSaved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not update the group', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Edit group"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={busy || !name.trim()}>
            Save
          </button>
        </>
      }
    >
      <label className="field">
        <span>Group name</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} maxLength={150} autoFocus />
      </label>
      <label className="field">
        <span>Description</span>
        <input className="input" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
      </label>
    </Modal>
  );
}
