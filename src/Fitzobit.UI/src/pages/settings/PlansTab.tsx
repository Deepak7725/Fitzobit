import { useState } from 'react';
import { ClipboardList, Link2, Trash2 } from 'lucide-react';
import { api, type Plan, type PlanAlias } from '../../api';
import { useApi } from '../../hooks';
import { formatDateTime } from '../../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, useToast } from '../../components/feedback';

/** Plan availability and remembered plan-name mappings (Admin only). */
export default function PlansTab() {
  const toast = useToast();
  const plans = useApi(api.plans);
  const aliases = useApi(api.planAliases);
  const [confirm, setConfirm] = useState<{ title: string; messages: string[]; label: string; danger?: boolean; run: () => Promise<void> } | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      await confirm.run();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The change failed.', 'error');
    } finally {
      setBusy(false);
      setConfirm(null);
    }
  };

  const toggle = (p: Plan) =>
    setConfirm({
      title: `${p.status === 'Active' ? 'Deactivate' : 'Activate'} “${p.name}”?`,
      messages:
        p.status === 'Active'
          ? [
              'Imports and customer edits can no longer choose this plan.',
              p.customerCount > 0 ? `${p.customerCount} existing customer(s) keep it unchanged.` : 'No customers use it.',
            ]
          : ['The plan can be chosen again in imports and customer edits.'],
      label: p.status === 'Active' ? 'Deactivate' : 'Activate',
      danger: p.status === 'Active',
      run: async () => {
        await api.setPlanStatus(p.id, p.status !== 'Active');
        toast(`Plan ${p.status === 'Active' ? 'deactivated' : 'activated'}: ${p.name}`);
        plans.reload();
      },
    });

  const remove = (a: PlanAlias) =>
    setConfirm({
      title: 'Forget this plan mapping?',
      messages: [`“${a.originalText}” will no longer be mapped automatically to ${a.planName}${a.tierName ? ` (${a.tierName})` : ''}.`, 'Customers already imported are not changed.'],
      label: 'Forget mapping',
      danger: true,
      run: async () => {
        await api.deletePlanAlias(a.id);
        toast('Plan mapping removed.');
        aliases.reload();
      },
    });

  return (
    <>
      <Card title={<><ClipboardList size={18} /> Plans</>} subtitle="One plan per catalogue item. Inactive plans stay on existing customers but cannot be newly chosen.">
        {plans.error && <ErrorPanel message={plans.error} onRetry={plans.reload} />}
        {plans.loading && !plans.data && <Loading label="Loading plans…" />}
        {plans.data && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Plan</th>
                  <th>Category</th>
                  <th className="num">Customers</th>
                  <th>Status</th>
                  <th className="actions-cell" aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {plans.data.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <div className="item-name">{p.name}</div>
                      <div className="item-sub">{p.code}</div>
                    </td>
                    <td>{p.category ?? '—'}</td>
                    <td className="num">{p.customerCount.toLocaleString('en-IN')}</td>
                    <td>
                      <Badge tone={p.status === 'Active' ? 'success' : 'neutral'}>{p.status}</Badge>
                    </td>
                    <td className="actions-cell">
                      <button className="btn btn-secondary btn-sm" onClick={() => toggle(p)}>
                        {p.status === 'Active' ? 'Deactivate' : 'Activate'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title={<><Link2 size={18} /> Remembered plan mappings</>} subtitle="Plan names from Excel files that were matched by hand once and are now applied automatically.">
        {aliases.error && <ErrorPanel message={aliases.error} onRetry={aliases.reload} />}
        {aliases.loading && !aliases.data && <Loading label="Loading mappings…" />}
        {aliases.data && aliases.data.length === 0 && (
          <EmptyState icon={<Link2 size={22} />} title="No remembered mappings" message="When you map a plan name by hand during an import, it is remembered here." />
        )}
        {aliases.data && aliases.data.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Text in the Excel file</th>
                  <th>Mapped to</th>
                  <th>Saved</th>
                  <th className="actions-cell" aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {aliases.data.map((a) => (
                  <tr key={a.id}>
                    <td className="strong">“{a.originalText}”</td>
                    <td>
                      {a.planName}
                      {a.tierName && <div className="item-sub">{a.tierName}</div>}
                    </td>
                    <td>
                      {formatDateTime(a.createdAt)}
                      {a.createdBy && <div className="item-sub">by {a.createdBy}</div>}
                    </td>
                    <td className="actions-cell">
                      <button className="icon-btn danger" title="Forget mapping" aria-label={`Forget mapping ${a.originalText}`} onClick={() => remove(a)}>
                        <Trash2 size={15} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {confirm && (
        <ConfirmDialog
          title={confirm.title}
          messages={confirm.messages}
          confirmLabel={confirm.label}
          danger={confirm.danger}
          busy={busy}
          onConfirm={run}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
