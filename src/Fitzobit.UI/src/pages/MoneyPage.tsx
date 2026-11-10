import { useEffect, useMemo, useState } from 'react';
import { BarChart3, IndianRupee, Lock, Settings2, Tag, Truck, UserRound, Utensils, Wallet } from 'lucide-react';
import { api, type MoneyLine, type MoneySettings, type PriceList } from '../api';
import { useAuth } from '../auth';
import { useApi } from '../hooks';
import { formatDay, todayIso } from '../format';
import { Card, ErrorPanel, Loading, PageHeader, StatCard } from '../components/ui';
import { useToast } from '../components/feedback';

/** "₹1,234" (whole rupees) or "₹103.8" with one decimal. */
export function rupees(value: number, decimals = 0): string {
  const sign = value < 0 ? '−' : '';
  return `${sign}₹${Math.abs(value).toLocaleString('en-IN', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })}`;
}

/** How one meal's price splits — the same formula as the server (MoneyService.Split). */
function split(packagePrice: number, deliveries: number, delivery: number, helper: number, percent: number) {
  const perMeal = packagePrice / deliveries;
  const profit = (perMeal * percent) / 100;
  return { perMeal, profit, food: perMeal - delivery - helper - profit };
}

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

/**
 * Money (Manager accounts only; owner and Siva, 2026-11-10): costs per box and target profits, the price list per meal type ×
 * preference × frequency with a per-meal breakdown, and what the packages earn today, this week or this month.
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
  const settings = useApi((signal) => api.moneySettings(signal), []);
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
          <SettingsCard settings={settings.data} onSaved={(s) => { settings.setData(s); setSaved((n) => n + 1); }} />
          <PriceListCard list={prices.data} settings={settings.data} onSaved={(p) => { prices.setData(p); setSaved((n) => n + 1); }} />
          <EarningsCard refreshKey={saved} />
        </>
      )}
    </>
  );
}

function SettingsCard({ settings, onSaved }: { settings: MoneySettings; onSaved: (s: MoneySettings) => void }) {
  const toast = useToast();
  const [delivery, setDelivery] = useState(String(settings.deliveryCostPerBox));
  const [helper, setHelper] = useState(String(settings.helperCostPerBox));
  const [targets, setTargets] = useState<Record<number, string>>(() =>
    Object.fromEntries(settings.targets.map((t) => [t.frequencyId, String(t.percent)])),
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    const numbers = [delivery, helper, ...Object.values(targets)].map(Number);
    if (numbers.some((n, i) => Number.isNaN(n) || n < 0 || [delivery, helper, ...Object.values(targets)][i].trim() === '')) {
      setError('Enter a number of 0 or more in every box.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await api.saveMoneySettings({
        deliveryCostPerBox: Number(delivery),
        helperCostPerBox: Number(helper),
        targets: settings.targets.map((t) => ({ frequencyId: t.frequencyId, percent: Number(targets[t.frequencyId]) })),
      });
      onSaved(result);
      toast('Costs and profit targets saved.');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The settings could not be saved.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      title={<><Settings2 size={18} /> Costs and profit targets</>}
      subtitle="Costs are per box delivered; the target profit is a share of the selling price."
      actions={
        <button className="btn btn-primary btn-sm" onClick={() => void save()} disabled={busy}>
          {busy ? 'Saving…' : 'Save costs'}
        </button>
      }
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
      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
      {settings.updatedAt && (
        <p className="footnote">
          Last changed {formatDay(settings.updatedAt.slice(0, 10))}
          {settings.updatedBy ? ` by ${settings.updatedBy}` : ''}.
        </p>
      )}
    </Card>
  );
}

function PriceListCard({ list, settings, onSaved }: { list: PriceList; settings: MoneySettings; onSaved: (p: PriceList) => void }) {
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
            {sel.meal.name} · {sel.pref.name}: where each meal's money goes
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
              const s = split(price, f.deliveryDays, settings.deliveryCostPerBox, settings.helperCostPerBox, target(f.id));
              const parts = [
                { label: 'Delivery + petrol', value: settings.deliveryCostPerBox, cls: 'mb-delivery' },
                { label: 'Helper', value: settings.helperCostPerBox, cls: 'mb-helper' },
                { label: `Profit ${target(f.id)}%`, value: s.profit, cls: 'mb-profit' },
                { label: 'Left for food', value: Math.max(s.food, 0), cls: 'mb-food' },
              ];
              return (
                <div key={f.id}>
                  <div className="small muted">
                    {f.name}: {rupees(price)} ÷ {f.deliveryDays} = {rupees(s.perMeal, 1)} per meal
                  </div>
                  <div className="money-bar">
                    {parts.map((p) => (
                      <span key={p.label} className={p.cls} style={{ flex: Math.max(p.value, 0.01) }} />
                    ))}
                  </div>
                  {parts.map((p, i) => (
                    <div key={p.label} className="money-bar-row">
                      <span>
                        <span className={`money-swatch ${p.cls}`} />
                        {p.label}
                      </span>
                      <span className={i === 3 ? (s.food < 0 ? 'text-danger strong' : 'text-success') : ''}>{rupees(i === 3 ? s.food : p.value, 1)}</span>
                    </div>
                  ))}
                  {s.food < 0 && <div className="text-danger small">Price too low for this profit target.</div>}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </Card>
  );
}

function EarningsCard({ refreshKey }: { refreshKey: number }) {
  const [period, setPeriod] = useState<Period>('today');
  const [from, to] = periodRange(period);
  const earnings = useApi((signal) => api.earnings(from, to, signal), [from, to, refreshKey]);
  const e = earnings.data;

  return (
    <Card
      title={<><BarChart3 size={18} /> Earnings from packages</>}
      subtitle={e ? `${formatDay(e.from, true)}${e.to !== e.from ? ` – ${formatDay(e.to, true)}` : ''} · ${e.deliveryDays} delivery day(s) · paused days not counted` : undefined}
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
      {e && (
        <>
          <div className="stat-grid">
            <StatCard icon={<Wallet size={20} />} label="Earned" value={rupees(e.totals.earned)} hint={`${e.totals.boxes} box(es)`} tone="green" />
            <StatCard icon={<Truck size={20} />} label="Delivery + petrol" value={rupees(e.totals.deliveryCost)} tone="blue" />
            <StatCard icon={<UserRound size={20} />} label="Helper" value={rupees(e.totals.helperCost)} tone="violet" />
            <StatCard icon={<IndianRupee size={20} />} label="Target profit" value={rupees(e.totals.targetProfit)} tone="teal" />
            <StatCard
              icon={<Utensils size={20} />}
              label="Left for food"
              value={<span className={e.totals.foodBudget < 0 ? 'text-danger' : ''}>{rupees(e.totals.foodBudget)}</span>}
              tone="amber"
            />
          </div>
          {e.unpricedBoxes > 0 && (
            <p className="alert alert-warning">
              {e.unpricedBoxes} box(es) belong to packages without a price and are not counted. Give those packages a price (package Edit → Price).
            </p>
          )}
          <MoneyTable title="By box" lines={e.byBox} />
          <MoneyTable title="By delivery group" lines={e.byGroup} />
        </>
      )}
    </Card>
  );
}

function MoneyTable({ title, lines }: { title: string; lines: MoneyLine[] }) {
  if (lines.length === 0) return <p className="muted small">{title}: no deliveries in this period.</p>;
  return (
    <div className="table-wrap money-table">
      <table className="table">
        <thead>
          <tr>
            <th>{title}</th>
            <th className="num">Boxes</th>
            <th className="num">Earned</th>
            <th className="num">Delivery + helper</th>
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
              <td className="num">{rupees(l.deliveryCost + l.helperCost)}</td>
              <td className="num">{rupees(l.targetProfit)}</td>
              <td className={`num ${l.foodBudget < 0 ? 'text-danger strong' : ''}`}>{rupees(l.foodBudget)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

