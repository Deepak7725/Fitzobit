import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { BarChart3, IndianRupee, Lock, Plus, Receipt, Settings2, Tag, Trash2, Truck, UserRound, Utensils, Wallet } from 'lucide-react';
import { api, type Earnings, type MoneyLine, type MoneySettings, type PriceList } from '../api';
import { useAuth } from '../auth';
import { useApi } from '../hooks';
import { formatDay, todayIso } from '../format';
import { Card, ErrorPanel, Loading, PageHeader, StatCard } from '../components/ui';
import { ConfirmDialog, useToast } from '../components/feedback';

/** "₹1,234" (whole rupees) or "₹103.8" with one decimal. */
export function rupees(value: number, decimals = 0): string {
  const sign = value < 0 ? '−' : '';
  return `${sign}₹${Math.abs(value).toLocaleString('en-IN', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })}`;
}

/** Bar colours: delivery, helper, then one per added cost line; profit and food have their own. */
const COST_COLOURS = ['#378add', '#7f77dd', '#d85a30', '#d4537e', '#888780', '#639922', '#0f6e56', '#993556'];
const PROFIT_COLOUR = '#1d9e75';
const FOOD_COLOUR = '#ba7517';

const key = (mealTypeId: number, preferenceId: number, frequencyId: number) => `${mealTypeId}-${preferenceId}-${frequencyId}`;

function addDays(day: string, n: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10);
}

type Period = 'today' | 'week' | 'month';

function periodRange(period: Period): [string, string] {
  const today = todayIso();
  if (period === 'today') return [today, today];
  const [y, m] = today.split('-').map(Number);
  if (period === 'month') return [`${today.slice(0, 7)}-01`, new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10)];
  const weekday = new Date(`${today}T00:00:00Z`).getUTCDay(); // 0 = Sunday
  const monday = addDays(today, weekday === 0 ? -6 : 1 - weekday);
  return [monday, addDays(monday, 5)];
}

/** The cost parts of a money line in display order: delivery, helper, then the added lines. */
function costParts(line: MoneyLine, costNames: string[]): { label: string; value: number }[] {
  return [
    { label: 'Delivery + petrol', value: line.deliveryCost },
    { label: 'Helper', value: line.helperCost },
    ...costNames.map((n) => ({ label: n, value: line.otherCosts[n] ?? 0 })),
  ];
}

/**
 * Money (Manager accounts only; owner and Siva, 2026-11-10): costs per box (delivery, helper and lines the Manager adds, e.g. Plate ₹10)
 * and target profits, all kept with a "valid from" history; the price list with a per-meal breakdown and the day total of a box; and
 * what the packages earn today, this week or this month.
 */
export default function MoneyPage() {
  const { isManager } = useAuth();
  if (!isManager)
    return (
      <>
        <PageHeader title="Money" />
        <ErrorPanel message="The Money page is only for Manager accounts. Sign in with a Manager account to see prices, costs and profit." />
      </>
    );
  return <MoneyContent />;
}

function MoneyContent() {
  const settings = useApi((signal) => api.moneySettings(null, signal), []);
  const prices = useApi((signal) => api.priceList(signal), []);
  const [saved, setSaved] = useState(0); // bumps the earnings after a save

  return (
    <>
      <PageHeader
        title="Money"
        subtitle="Prices, costs per box and target profit, and what the packages earn. Each package keeps the price it was sold at."
        actions={
          <span className="badge badge-warning">
            <Lock size={12} /> Manager accounts only
          </span>
        }
      />
      {(settings.error || prices.error) && (
        <ErrorPanel message={settings.error ?? prices.error ?? ''} onRetry={() => (settings.error ? settings.reload() : prices.reload())} />
      )}
      {(!settings.data || !prices.data) && !settings.error && !prices.error && <Loading label="Loading money…" />}
      {settings.data && prices.data && (
        <>
          <SettingsCard
            key={settings.data.effectiveFrom + (settings.data.updatedAt ?? '')}
            settings={settings.data}
            onSaved={() => {
              settings.reload();
              setSaved((n) => n + 1);
            }}
          />
          <PriceListCard list={prices.data} settings={settings.data} refreshKey={saved} onSaved={(p) => { prices.setData(p); setSaved((n) => n + 1); }} />
          <EarningsCard refreshKey={saved} onChanged={() => setSaved((n) => n + 1)} />
        </>
      )}
    </>
  );
}

interface LineDraft {
  id: number;
  name: string;
  amount: string;
}

let nextLineId = 1;

function SettingsCard({ settings, onSaved }: { settings: MoneySettings; onSaved: () => void }) {
  const toast = useToast();
  const [delivery, setDelivery] = useState(String(settings.deliveryCostPerBox));
  const [helper, setHelper] = useState(String(settings.helperCostPerBox));
  const [lines, setLines] = useState<LineDraft[]>(() => settings.lines.map((l) => ({ id: nextLineId++, name: l.name, amount: String(l.amountPerBox) })));
  const [newName, setNewName] = useState('');
  const [newAmount, setNewAmount] = useState('');
  const [addError, setAddError] = useState<string | null>(null);
  const [targets, setTargets] = useState<Record<number, string>>(() =>
    Object.fromEntries(settings.targets.map((t) => [t.frequencyId, String(t.percent)])),
  );
  const [validFrom, setValidFrom] = useState(todayIso());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const addLine = () => {
    const name = newName.trim();
    if (!name) return setAddError('Enter a name first, e.g. Plate.');
    if (newAmount.trim() === '' || !(Number(newAmount) >= 0)) return setAddError('Enter an amount of ₹0 or more per box.');
    if (lines.some((l) => l.name.trim().toLowerCase() === name.toLowerCase())) return setAddError(`"${name}" is already in the list.`);
    setLines((x) => [...x, { id: nextLineId++, name, amount: newAmount.trim() }]);
    setNewName('');
    setNewAmount('');
    setAddError(null);
  };

  const otherTotal = lines.reduce((sum, l) => sum + (Number(l.amount) || 0), 0);
  const perBox = (Number(delivery) || 0) + (Number(helper) || 0) + otherTotal;

  const save = async () => {
    const numbers = [delivery, helper, ...lines.map((l) => l.amount), ...Object.values(targets)];
    if (numbers.some((n) => n.trim() === '' || !(Number(n) >= 0))) return setError('Enter a number of 0 or more in every box.');
    if (lines.some((l) => !l.name.trim())) return setError('Every cost line needs a name.');
    if (!validFrom) return setError('Choose the day these costs start (Valid from).');
    setBusy(true);
    setError(null);
    try {
      const result = await api.saveMoneySettings({
        effectiveFrom: validFrom,
        deliveryCostPerBox: Number(delivery),
        helperCostPerBox: Number(helper),
        lines: lines.map((l) => ({ name: l.name.trim(), amountPerBox: Number(l.amount) })),
        targets: settings.targets.map((t) => ({ frequencyId: t.frequencyId, percent: Number(targets[t.frequencyId]) })),
      });
      toast(`Costs saved, valid from ${formatDay(result.effectiveFrom, true)}. Earlier days keep their costs.`);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The costs could not be saved.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      title={<><Settings2 size={18} /> Costs and profit targets</>}
      subtitle="Every cost is per box delivered, for all boxes. The target profit is a share of the selling price."
    >
      <div className="money-settings">
        <label className="field">
          Delivery person + petrol (₹ per box)
          <input className="input" type="number" min={0} step={1} value={delivery} onChange={(e) => setDelivery(e.target.value)} />
        </label>
        <label className="field">
          Helper (₹ per box)
          <input className="input" type="number" min={0} step={1} value={helper} onChange={(e) => setHelper(e.target.value)} />
        </label>
        {settings.targets.map((t) => (
          <label key={t.frequencyId} className="field">
            {t.frequency} profit target (%)
            <input
              className="input"
              type="number"
              min={0}
              max={95}
              step={1}
              value={targets[t.frequencyId] ?? ''}
              onChange={(e) => setTargets((x) => ({ ...x, [t.frequencyId]: e.target.value }))}
            />
          </label>
        ))}
      </div>

      <p className="form-section-title money-lines-title">Other costs per box</p>
      {lines.length === 0 && <p className="muted small">No other costs yet. Add one below, e.g. Plate ₹10.</p>}
      {lines.map((l) => (
        <div key={l.id} className="money-line-row">
          <input
            className="input"
            value={l.name}
            maxLength={60}
            aria-label="Cost name"
            onChange={(e) => setLines((x) => x.map((y) => (y.id === l.id ? { ...y, name: e.target.value } : y)))}
          />
          <input
            className="input"
            type="number"
            min={0}
            step={1}
            value={l.amount}
            aria-label={`${l.name} ₹ per box`}
            onChange={(e) => setLines((x) => x.map((y) => (y.id === l.id ? { ...y, amount: e.target.value } : y)))}
          />
          <button className="icon-btn danger" aria-label={`Remove ${l.name}`} title="Remove" onClick={() => setLines((x) => x.filter((y) => y.id !== l.id))}>
            <Trash2 size={15} />
          </button>
        </div>
      ))}
      <div className="money-line-row">
        <input
          className="input"
          placeholder="Name, e.g. Plate"
          maxLength={60}
          value={newName}
          aria-label="New cost name"
          onChange={(e) => {
            setNewName(e.target.value);
            setAddError(null);
          }}
          onKeyDown={(e) => e.key === 'Enter' && addLine()}
        />
        <input
          className="input"
          type="number"
          min={0}
          step={1}
          placeholder="₹ per box"
          value={newAmount}
          aria-label="New cost ₹ per box"
          onChange={(e) => {
            setNewAmount(e.target.value);
            setAddError(null);
          }}
          onKeyDown={(e) => e.key === 'Enter' && addLine()}
        />
        <button className="icon-btn" aria-label="Add cost line" title="Add" onClick={addLine}>
          <Plus size={16} />
        </button>
      </div>
      {addError && <div className="field-error">{addError}</div>}

      <div className="money-save-row">
        <span className="small muted">
          Costs per box: <strong>{rupees(perBox, 1)}</strong> (delivery {rupees(Number(delivery) || 0)} + helper {rupees(Number(helper) || 0)}
          {lines.length > 0 ? ` + other ${rupees(otherTotal, 1)}` : ''})
        </span>
        <label className="field-inline">
          Valid from
          <input className="input input-sm" type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
        </label>
        <button className="btn btn-primary btn-sm" onClick={() => void save()} disabled={busy}>
          {busy ? 'Saving…' : 'Save costs'}
        </button>
      </div>
      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
      <p className="footnote">
        Showing the costs valid today (since {formatDay(settings.effectiveFrom)}). Saving applies from the “Valid from” day; earlier days keep
        their costs.
        {settings.history.length > 1 && (
          <>
            {' '}History:{' '}
            {settings.history
              .map((h) => `from ${h.effectiveFrom <= '2000-01-01' ? 'the start' : formatDay(h.effectiveFrom)} ${rupees(h.costsPerBox, 1)}/box`)
              .join(' · ')}
          </>
        )}
      </p>
    </Card>
  );
}

function PriceListCard({ list, settings, refreshKey, onSaved }: {
  list: PriceList;
  settings: MoneySettings;
  refreshKey: number;
  onSaved: (p: PriceList) => void;
}) {
  const toast = useToast();
  const initial = useMemo(
    () => Object.fromEntries(list.prices.map((p) => [key(p.mealTypeId, p.preferenceId, p.frequencyId), String(p.price)])),
    [list],
  );
  const [values, setValues] = useState<Record<string, string>>(initial);
  useEffect(() => setValues(initial), [initial]);
  const rows = list.mealTypes.flatMap((m) => list.preferences.map((p) => ({ meal: m, pref: p })));
  const [selected, setSelected] = useState(() => {
    const first = list.prices[0];
    return first ? `${first.mealTypeId}-${first.preferenceId}` : rows[0] ? `${rows[0].meal.id}-${rows[0].pref.id}` : '';
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const changed = Object.keys({ ...initial, ...values }).filter((k) => (initial[k] ?? '') !== (values[k] ?? '').trim());

  const save = async () => {
    const bad = changed.find((k) => values[k]?.trim() && !(Number(values[k]) > 0));
    if (bad) {
      setError('A price must be more than ₹0; leave the box empty when it is not sold.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await api.savePriceList(
        changed.map((k) => {
          const [mealTypeId, preferenceId, frequencyId] = k.split('-').map(Number);
          return { mealTypeId, preferenceId, frequencyId, price: values[k]?.trim() ? Number(values[k]) : null };
        }),
      );
      onSaved(result);
      toast(`${changed.length} price(s) saved. New packages and renewals use them; packages already sold keep their price.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The prices could not be saved.');
    } finally {
      setBusy(false);
    }
  };

  const target = (frequencyId: number) => settings.targets.find((t) => t.frequencyId === frequencyId)?.percent ?? 0;
  const sel = rows.find((r) => `${r.meal.id}-${r.pref.id}` === selected);
  const costs = [
    { label: 'Delivery + petrol', value: settings.deliveryCostPerBox },
    { label: 'Helper', value: settings.helperCostPerBox },
    ...settings.lines.map((l) => ({ label: l.name, value: l.amountPerBox })),
  ];
  const costsPerBox = costs.reduce((s, c) => s + c.value, 0);

  return (
    <Card
      title={<><Tag size={18} /> Price list</>}
      subtitle="Package price for one box per delivery day. Empty = not sold. Click a row to see where each meal's money goes."
      actions={
        <button className="btn btn-primary btn-sm" onClick={() => void save()} disabled={busy || changed.length === 0}>
          {busy ? 'Saving…' : changed.length > 0 ? `Save ${changed.length} price(s)` : 'Save prices'}
        </button>
      }
    >
      <div className="table-wrap">
        <table className="table money-prices">
          <thead>
            <tr>
              <th>Box</th>
              {list.frequencies.map((f) => (
                <th key={f.id} className="num">
                  {f.name}
                  {f.deliveryDays ? ` (${f.deliveryDays})` : ''}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(({ meal, pref }) => {
              const rowKey = `${meal.id}-${pref.id}`;
              return (
                <tr key={rowKey} className={`row-click ${rowKey === selected ? 'row-selected' : ''}`} onClick={() => setSelected(rowKey)}>
                  <td className="nowrap">
                    {meal.name} · {pref.name}
                  </td>
                  {list.frequencies.map((f) => {
                    const k = key(meal.id, pref.id, f.id);
                    return (
                      <td key={f.id} className="num">
                        <input
                          className={`input input-sm money-input ${changed.includes(k) ? 'money-changed' : ''}`}
                          type="number"
                          min={0}
                          step={1}
                          placeholder="—"
                          aria-label={`${meal.name} ${pref.name} ${f.name} price`}
                          value={values[k] ?? ''}
                          onFocus={() => setSelected(rowKey)}
                          onChange={(e) => setValues((v) => ({ ...v, [k]: e.target.value }))}
                        />
                      </td>
                    );
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
      {sel && (
        <div className="money-breakdown">
          <p className="strong">
            {sel.meal.name} · {sel.pref.name}: where one meal's money goes (one box, one day)
          </p>
          <div className="money-breakdown-grid">
            {list.frequencies.map((f) => {
              const price = Number(values[key(sel.meal.id, sel.pref.id, f.id)]);
              if (!(price > 0) || !f.deliveryDays)
                return (
                  <div key={f.id} className="muted small">
                    {f.name}: not sold
                  </div>
                );
              const perMeal = price / f.deliveryDays;
              const profit = (perMeal * target(f.id)) / 100;
              const food = perMeal - costsPerBox - profit;
              const parts = [
                ...costs.map((c, i) => ({ ...c, colour: COST_COLOURS[i % COST_COLOURS.length] })),
                { label: `Profit ${target(f.id)}%`, value: profit, colour: PROFIT_COLOUR },
                { label: 'Left for food', value: food, colour: FOOD_COLOUR },
              ];
              return (
                <div key={f.id}>
                  <div className="small muted">
                    {f.name}: {rupees(price)} ÷ {f.deliveryDays} = {rupees(perMeal, 1)} per meal
                  </div>
                  <div className="money-bar">
                    {parts.map((p) => (
                      <span key={p.label} style={{ flex: Math.max(p.value, 0.01), background: p.colour }} />
                    ))}
                  </div>
                  {parts.map((p, i) => (
                    <div key={p.label} className="money-bar-row">
                      <span>
                        <span className="money-swatch" style={{ background: p.colour }} />
                        {p.label}
                      </span>
                      <span className={i === parts.length - 1 ? (food < 0 ? 'text-danger strong' : 'text-success') : ''}>{rupees(p.value, 1)}</span>
                    </div>
                  ))}
                  {food < 0 && <div className="text-danger small">Price too low for this profit target.</div>}
                </div>
              );
            })}
          </div>
          <DayTotal boxKey={selected} label={`${sel.meal.name} · ${sel.pref.name}`} refreshKey={refreshKey} />
        </div>
      )}
    </Card>
  );
}

/** All boxes of one meal type · preference delivered on a chosen day, with the money split (owner: "how much for one day"). */
function DayTotal({ boxKey, label, refreshKey }: { boxKey: string; label: string; refreshKey: number }) {
  const [day, setDay] = useState(todayIso());
  const earnings = useApi((signal) => api.earnings(day, day, signal), [day, refreshKey]);
  const e = earnings.data;
  const line = e?.byBox.find((b) => b.key === boxKey);
  return (
    <div className="money-day-total">
      <div className="money-day-head">
        <p className="strong">
          <Receipt size={15} /> {label}: day total
        </p>
        <label className="field-inline">
          Day
          <input className="input input-sm" type="date" value={day} onChange={(ev) => ev.target.value && setDay(ev.target.value)} />
        </label>
      </div>
      {earnings.error && <p className="text-danger small">{earnings.error}</p>}
      {!e && !earnings.error && <Loading label="Adding up…" />}
      {e && e.deliveryDays === 0 && <p className="muted small">{formatDay(day, true)} is a Sunday: no deliveries.</p>}
      {e && e.deliveryDays > 0 && !line && <p className="muted small">No {label} boxes delivered on {formatDay(day, true)} (paused days not counted).</p>}
      {e && line && (
        <div className="money-day-grid">
          <div>
            <span className="muted small">Boxes</span>
            <strong>{line.boxes}</strong>
          </div>
          <div>
            <span className="muted small">Earned</span>
            <strong>{rupees(line.earned)}</strong>
          </div>
          {costParts(line, e.costNames).map((c) => (
            <div key={c.label}>
              <span className="muted small">{c.label}</span>
              <strong>{rupees(c.value)}</strong>
            </div>
          ))}
          <div>
            <span className="muted small">Target profit</span>
            <strong>{rupees(line.targetProfit)}</strong>
          </div>
          <div>
            <span className="muted small">Left for food</span>
            <strong className={line.foodBudget < 0 ? 'text-danger' : 'text-success'}>{rupees(line.foodBudget)}</strong>
          </div>
        </div>
      )}
    </div>
  );
}

function EarningsCard({ refreshKey, onChanged }: { refreshKey: number; onChanged: () => void }) {
  const [period, setPeriod] = useState<Period>('today');
  const [from, to] = periodRange(period);
  const earnings = useApi((signal) => api.earnings(from, to, signal), [from, to, refreshKey]);
  const e = earnings.data;

  return (
    <Card
      title={<><BarChart3 size={18} /> Earnings from packages</>}
      subtitle={e ? `${formatDay(e.from, true)}${e.to !== e.from ? ` – ${formatDay(e.to, true)}` : ''} · ${e.deliveryDays} delivery day(s) · paused days not counted · each day uses the costs valid that day` : undefined}
      actions={
        <div className="tabs tabs-inline" role="tablist">
          {(['today', 'week', 'month'] as Period[]).map((p) => (
            <button key={p} className={period === p ? 'active' : ''} onClick={() => setPeriod(p)}>
              {p === 'today' ? 'Today' : p === 'week' ? 'This week' : 'This month'}
            </button>
          ))}
        </div>
      }
    >
      {earnings.error && <ErrorPanel message={earnings.error} onRetry={earnings.reload} />}
      {!e && !earnings.error && <Loading label="Adding up…" />}
      {e && <EarningsBody e={e} onFilled={() => { earnings.reload(); onChanged(); }} />}
    </Card>
  );
}

function EarningsBody({ e, onFilled }: { e: Earnings; onFilled: () => void }) {
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const [filling, setFilling] = useState(false);
  const fill = async () => {
    setFilling(true);
    try {
      const result = await api.fillMissingPrices();
      toast(
        `${result.filled} package(s) got their list price.` +
          (result.stillMissing > 0 ? ` ${result.stillMissing} still have no price: their box has no price in the price list.` : ''),
      );
      setConfirming(false);
      onFilled();
    } catch (err) {
      toast(err instanceof Error ? err.message : 'The prices could not be filled.', 'error');
    } finally {
      setFilling(false);
    }
  };
  return (
    <>
      <div className="stat-grid">
        <StatCard icon={<Wallet size={20} />} label="Earned" value={rupees(e.totals.earned)} hint={`${e.totals.boxes} box(es)`} tone="green" />
        <StatCard icon={<Truck size={20} />} label="Delivery + petrol" value={rupees(e.totals.deliveryCost)} tone="blue" />
        <StatCard icon={<UserRound size={20} />} label="Helper" value={rupees(e.totals.helperCost)} tone="violet" />
        {e.costNames.map((n) => (
          <StatCard key={n} icon={<Receipt size={20} />} label={n} value={rupees(e.totals.otherCosts[n] ?? 0)} tone="slate" />
        ))}
        <StatCard icon={<IndianRupee size={20} />} label="Target profit" value={rupees(e.totals.targetProfit)} tone="teal" />
        <StatCard
          icon={<Utensils size={20} />}
          label="Left for food"
          value={<span className={e.totals.foodBudget < 0 ? 'text-danger' : ''}>{rupees(e.totals.foodBudget)}</span>}
          tone="amber"
        />
      </div>
      {e.unpricedBoxes > 0 && (
        <div className="alert alert-warning money-unpriced-box">
          <p>
            {e.unpricedBoxes} box(es) belong to packages without a price and are not counted. Usually the box has no price in the price list
            (e.g. Trail, Meal bowl). Give the box a price above for new packages, and these packages a price in their Edit dialog (pencil → Price):
          </p>
          <ul className="plain-list money-unpriced">
            {e.unpricedPackages.map((u) => (
              <li key={`${u.customerId}-${u.package}`}>
                <Link to={`/data?q=${encodeURIComponent(u.customerCode)}`}>
                  {u.customerCode} {u.customerName}
                </Link>{' '}
                · {u.package} · {u.boxes} box(es)
              </li>
            ))}
          </ul>
          <button className="btn btn-secondary btn-sm" onClick={() => setConfirming(true)}>
            Fill missing prices from the price list
          </button>
        </div>
      )}
      {confirming && (
        <ConfirmDialog
          title="Fill missing package prices?"
          messages={[
            'Every package without a price, current and finished, gets its box price from the price list × its quantity.',
            'Packages that already have a price are not changed. Boxes with no price in the price list stay without a price.',
          ]}
          confirmLabel="Fill prices"
          busy={filling}
          onConfirm={() => void fill()}
          onCancel={() => setConfirming(false)}
        />
      )}
      <MoneyTable title="By box" lines={e.byBox} costNames={e.costNames} />
      <MoneyTable title="By delivery group" lines={e.byGroup} costNames={e.costNames} />
    </>
  );
}

function MoneyTable({ title, lines, costNames }: { title: string; lines: MoneyLine[]; costNames: string[] }) {
  if (lines.length === 0) return <p className="muted small">{title}: no deliveries in this period.</p>;
  const headers = ['Delivery + petrol', 'Helper', ...costNames];
  return (
    <div className="table-wrap money-table">
      <table className="table">
        <thead>
          <tr>
            <th>{title}</th>
            <th className="num">Boxes</th>
            <th className="num">Earned</th>
            {headers.map((h) => (
              <th key={h} className="num">
                {h}
              </th>
            ))}
            <th className="num">Target profit</th>
            <th className="num">Left for food</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((l) => (
            <tr key={l.label}>
              <td className="nowrap">{l.label}</td>
              <td className="num">{l.boxes}</td>
              <td className="num">{rupees(l.earned)}</td>
              {costParts(l, costNames).map((c) => (
                <td key={c.label} className="num">
                  {rupees(c.value)}
                </td>
              ))}
              <td className="num">{rupees(l.targetProfit)}</td>
              <td className={`num ${l.foodBudget < 0 ? 'text-danger strong' : ''}`}>{rupees(l.foodBudget)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
