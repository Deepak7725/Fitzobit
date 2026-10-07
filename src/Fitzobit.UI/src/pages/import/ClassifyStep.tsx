import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ArrowRight, CheckCircle2, Info } from 'lucide-react';
import { api, type ImportDetail, type PlanMapping, type PlanOption } from '../../api';
import { useApi } from '../../hooks';
import { Badge, Card, ErrorPanel, Loading } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { BarList, MiniStat, StepFooter, stepPath } from './shared';

const methodLabel: Record<PlanMapping['method'], string> = {
  None: '',
  SavedAlias: 'Saved mapping',
  ExactName: 'Exact name',
  Keywords: 'Matched by keywords',
  Manual: 'Chosen by you',
};

export default function ClassifyStep({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  const navigate = useNavigate();
  const mappings = useApi((signal) => api.planMappings(detail.id, signal), [detail.id]);
  const options = useApi(api.planOptions);
  const classification = useApi((signal) => api.classification(detail.id, signal), [detail.id]);
  const editable = detail.status === 'Validated';

  const refreshAll = () => {
    mappings.reload();
    classification.reload();
    onChanged();
  };

  const unresolved = (mappings.data ?? []).filter((m) => m.status !== 'Mapped');
  const c = classification.data;

  return (
    <>
      <Card
        title="Plan mapping"
        subtitle="Each distinct plan text in the file, matched to a Fitzobit plan from the catalogue. Nothing ambiguous is chosen for you."
        actions={
          unresolved.length > 0 ? (
            <Badge tone="danger">{unresolved.length} need a decision</Badge>
          ) : mappings.data ? (
            <Badge tone="success" icon={<CheckCircle2 size={13} />}>
              All plans mapped
            </Badge>
          ) : null
        }
      >
        {(mappings.loading && !mappings.data) || (options.loading && !options.data) ? <Loading /> : null}
        {mappings.error && <ErrorPanel message={mappings.error} onRetry={mappings.reload} />}
        {mappings.data && options.data && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Plan text in file</th>
                  <th className="num">Rows</th>
                  <th>Status</th>
                  <th>Fitzobit plan</th>
                  <th>Variant</th>
                  <th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {mappings.data.map((m) => (
                  <PlanMappingRow key={m.id} batchId={detail.id} mapping={m} options={options.data!} editable={editable} onSaved={refreshAll} />
                ))}
              </tbody>
            </table>
          </div>
        )}
        <p className="footnote">
          <Info size={13} /> Rows whose plan is unknown or ambiguous are errors until mapped. Plans you choose are remembered for future uploads.
        </p>
      </Card>

      {classification.error && <ErrorPanel message={classification.error} onRetry={classification.reload} />}
      {c && (
        <>
          <div className="mini-stat-grid mini-stat-grid-6">
            <MiniStat label="Customers to import" value={c.includedRows} tone="info" />
            <MiniStat label="New customers" value={c.newCustomers} tone="success" />
            <MiniStat label="Existing customers" value={c.existingCustomers} />
            <MiniStat label="Without pincode" value={c.withoutPincode} tone={c.withoutPincode ? 'warning' : undefined} />
            <MiniStat label="Without group" value={c.withoutGroup} tone={c.withoutGroup ? 'warning' : undefined} />
            <MiniStat label="Potential duplicates" value={c.potentialDuplicates} tone={c.potentialDuplicates ? 'warning' : undefined} />
          </div>

          <div className="grid-3">
            <Card title="By pincode" subtitle={`${c.byPincode.length} pincodes`}>
              <BarList items={c.byPincode} total={c.includedRows} empty="No pincodes detected" />
            </Card>
            <Card title="By area / locality" subtitle={`${c.byLocality.length} areas`}>
              <BarList items={c.byLocality} total={c.includedRows} empty="No areas detected" />
            </Card>
            <Card title="By city" subtitle={`${c.byCity.length} cities`}>
              <BarList items={c.byCity} total={c.includedRows} empty="No cities detected" />
            </Card>
            <Card title="By plan">
              <BarList items={c.byPlan} total={c.includedRows} />
            </Card>
            <Card title="By catalogue category">
              <BarList items={c.byCategory} total={c.includedRows} />
            </Card>
            <Card title="By full location" subtitle={`${c.byLocation.length} distinct addresses`}>
              <BarList items={c.byLocation} total={c.includedRows} limit={6} />
            </Card>
          </div>
        </>
      )}

      <StepFooter note="Classification only describes the data. Next, decide which delivery groups you need.">
        <button className="btn btn-secondary" onClick={() => navigate(stepPath(detail.id, 'validate'))}>
          <ArrowLeft size={16} /> Back
        </button>
        <button className="btn btn-primary" onClick={() => navigate(stepPath(detail.id, 'groups'))}>
          Next: Create groups <ArrowRight size={16} />
        </button>
      </StepFooter>
    </>
  );
}

function PlanMappingRow({ batchId, mapping, options, editable, onSaved }: {
  batchId: number;
  mapping: PlanMapping;
  options: PlanOption[];
  editable: boolean;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [planId, setPlanId] = useState<number | ''>(mapping.planId ?? '');
  const [tierId, setTierId] = useState<number | ''>(mapping.pricingTierId ?? '');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setPlanId(mapping.planId ?? '');
    setTierId(mapping.pricingTierId ?? '');
  }, [mapping.planId, mapping.pricingTierId]);

  const plan = options.find((o) => o.id === planId);
  const changed = planId !== (mapping.planId ?? '') || tierId !== (mapping.pricingTierId ?? '');
  const candidates = options.filter((o) => mapping.candidatePlanIds.includes(o.id));
  const others = options.filter((o) => !mapping.candidatePlanIds.includes(o.id));

  const save = async (reset = false) => {
    setBusy(true);
    try {
      await api.updatePlanMapping(batchId, mapping.id, reset ? null : planId === '' ? null : planId, reset || tierId === '' ? null : tierId);
      toast(reset ? `“${mapping.exampleText}” reset to automatic matching` : `“${mapping.exampleText}” mapped for ${mapping.rowCount} row(s)`);
      onSaved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save the mapping', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr className={mapping.status !== 'Mapped' ? 'row-error' : mapping.method === 'Keywords' ? 'row-warn' : ''}>
      <td>
        <div className="strong">{mapping.exampleText}</div>
        {mapping.note && <div className="item-sub">{mapping.note}</div>}
      </td>
      <td className="num">{mapping.rowCount}</td>
      <td>
        {mapping.status === 'Mapped' ? (
          <Badge tone={mapping.method === 'Keywords' ? 'warning' : 'success'}>{methodLabel[mapping.method]}</Badge>
        ) : (
          <Badge tone="danger">{mapping.status === 'Ambiguous' ? 'Ambiguous: choose' : 'Unknown plan'}</Badge>
        )}
      </td>
      <td>
        <select
          className="select"
          value={planId}
          disabled={!editable || busy}
          onChange={(e) => {
            setPlanId(e.target.value === '' ? '' : Number(e.target.value));
            setTierId('');
          }}
          aria-label={`Plan for ${mapping.exampleText}`}
        >
          <option value="">Choose a plan…</option>
          {candidates.length > 0 && (
            <optgroup label="Possible matches">
              {candidates.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </optgroup>
          )}
          <optgroup label={candidates.length > 0 ? 'All plans' : 'Plans'}>
            {others.map((o) => (
              <option key={o.id} value={o.id}>
                {o.name}
              </option>
            ))}
          </optgroup>
        </select>
      </td>
      <td>
        <select
          className="select"
          value={tierId}
          disabled={!editable || busy || !plan || plan.tiers.length === 0}
          onChange={(e) => setTierId(e.target.value === '' ? '' : Number(e.target.value))}
          aria-label={`Variant for ${mapping.exampleText}`}
        >
          <option value="">{plan && plan.tiers.length > 0 ? 'Not specified' : '—'}</option>
          {plan?.tiers.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name} · ₹{t.price.toLocaleString('en-IN')}
            </option>
          ))}
        </select>
      </td>
      <td className="actions-cell">
        {editable && changed && planId !== '' && (
          <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => save()}>
            Save
          </button>
        )}
        {editable && !changed && mapping.method === 'Keywords' && (
          <button className="btn btn-secondary btn-sm" disabled={busy} onClick={() => save()}>
            Confirm
          </button>
        )}
        {editable && !changed && mapping.method === 'Manual' && (
          <button className="link-button" disabled={busy} onClick={() => save(true)}>
            Reset
          </button>
        )}
      </td>
    </tr>
  );
}
