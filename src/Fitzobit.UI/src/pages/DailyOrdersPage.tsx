import { useState } from 'react';
import { ChevronLeft, ChevronRight, ClipboardList, ExternalLink, FileSpreadsheet, FileText } from 'lucide-react';
import { api, type DailyOrders, type ExportFormat, type GroupOrders, type MealTotal } from '../api';
import { useApi } from '../hooks';
import { formatDay, formatNumber, formatTime } from '../format';
import { Card, EmptyState, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { useToast } from '../components/feedback';
import { GroupPill } from '../components/groups';

/** "yyyy-mm-dd" plus n days (calendar arithmetic in UTC, so no time zone can shift the day). */
function addDays(day: string, n: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10);
}

/** Totals grouped by meal type: [["Medium", [{Veg 12}, {With Egg 5}]], …] in the server's order. */
function byMealType(totals: MealTotal[]): [string, MealTotal[]][] {
  const groups = new Map<string, MealTotal[]>();
  for (const t of totals) groups.set(t.mealType, [...(groups.get(t.mealType) ?? []), t]);
  return [...groups.entries()];
}

/**
 * Daily Orders: the boxes to cook and pack for one day — kitchen totals per meal type × preference, the same per delivery group,
 * and per group the customers in delivery-time order with all their packages on one line (one bag). Exports to Excel and PDF.
 */
export default function DailyOrdersPage() {
  const toast = useToast();
  const [date, setDate] = useState<string | null>(null); // null = the server's default (next delivery day)
  const orders = useApi((signal) => api.dailyOrders(date, signal), [date]);
  const [exporting, setExporting] = useState<ExportFormat | null>(null);
  const o = orders.data;
  const shown = date ?? o?.date ?? '';

  const step = (n: number) => {
    if (!shown) return;
    let next = addDays(shown, n);
    if (new Date(`${next}T00:00:00Z`).getUTCDay() === 0) next = addDays(next, n); // skip Sundays
    setDate(next);
  };

  const exportFile = async (format: ExportFormat) => {
    if (!o) return;
    setExporting(format);
    try {
      const result = await api.exportDailyOrders(o.date, format);
      toast(`Downloaded ${result.fileName}.`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The export failed.', 'error');
    } finally {
      setExporting(null);
    }
  };

  return (
    <>
      <PageHeader
        title="Daily Orders"
        subtitle="Boxes to cook and pack for one day: kitchen totals, and per delivery group the customers with everything they get."
        actions={
          <>
            <button className="btn btn-secondary" onClick={() => void exportFile('Excel')} disabled={!o || exporting !== null}>
              <FileSpreadsheet size={16} /> {exporting === 'Excel' ? 'Exporting…' : 'Excel'}
            </button>
            <button className="btn btn-secondary" onClick={() => void exportFile('Pdf')} disabled={!o || exporting !== null}>
              <FileText size={16} /> {exporting === 'Pdf' ? 'Exporting…' : 'PDF'}
            </button>
          </>
        }
      />

      <div className="orders-date-bar">
        <button className="icon-btn" onClick={() => step(-1)} disabled={!shown} aria-label="Previous delivery day">
          <ChevronLeft size={18} />
        </button>
        <input className="input" type="date" value={shown} onChange={(e) => e.target.value && setDate(e.target.value)} aria-label="Delivery day" />
        <button className="icon-btn" onClick={() => step(1)} disabled={!shown} aria-label="Next delivery day">
          <ChevronRight size={18} />
        </button>
        <strong>{shown ? formatDay(shown, true) : ''}</strong>
        {date !== null && (
          <button className="link-button" onClick={() => setDate(null)}>
            Next delivery day
          </button>
        )}
      </div>
      <p className="footnote">Active customers only; paused days and finished packages are left out. Tomorrow's list is final after 12 pm today.</p>

      {orders.loading && !o && <Loading />}
      {orders.error && <ErrorPanel message={orders.error} onRetry={orders.reload} />}
      {o && <OrdersView orders={o} />}
    </>
  );
}

function OrdersView({ orders: o }: { orders: DailyOrders }) {
  if (!o.isDeliveryDay)
    return (
      <Card>
        <EmptyState icon={<ClipboardList size={22} />} title="No deliveries on Sundays" message="Choose another day." />
      </Card>
    );
  if (o.totalBoxes === 0)
    return (
      <Card>
        <EmptyState icon={<ClipboardList size={22} />} title="No deliveries on this day" message="No active customer has a package delivering then." />
      </Card>
    );

  return (
    <div className="stack">
      <Card
        title={<><ClipboardList size={18} /> Kitchen totals</>}
        subtitle={`${formatNumber(o.totalBoxes)} boxes for ${formatNumber(o.totalCustomers)} customers`}
      >
        <MealTotals totals={o.totals} />
      </Card>

      <Card title="By delivery group" subtitle="Boxes of each meal per group, for packing the bags.">
        <div className="table-wrap">
          <table className="table data-table orders-matrix">
            <thead>
              <tr>
                <th>Meal</th>
                {o.groups.map((g) => (
                  <th key={g.label} className="num">
                    {g.label}
                  </th>
                ))}
                <th className="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {o.totals.map((t) => (
                <tr key={`${t.mealType}|${t.preference}`}>
                  <td>
                    {t.mealType} <span className="muted">· {t.preference}</span>
                  </td>
                  {o.groups.map((g) => (
                    <td key={g.label} className="num">
                      {g.totals.find((x) => x.mealType === t.mealType && x.preference === t.preference)?.boxes || ''}
                    </td>
                  ))}
                  <td className="num strong">{t.boxes}</td>
                </tr>
              ))}
              <tr className="orders-total-row">
                <td>Total</td>
                {o.groups.map((g) => (
                  <td key={g.label} className="num">
                    {g.boxes}
                  </td>
                ))}
                <td className="num">{o.totalBoxes}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </Card>

      {o.groups.map((g) => (
        <GroupSection key={g.label} group={g} />
      ))}
    </div>
  );
}

function MealTotals({ totals }: { totals: MealTotal[] }) {
  return (
    <div className="meal-totals">
      {byMealType(totals).map(([meal, items]) => (
        <div key={meal} className="meal-total">
          <div className="meal-total-head">
            <span>{meal}</span>
            <strong>{items.reduce((sum, t) => sum + t.boxes, 0)}</strong>
          </div>
          {items.map((t) => (
            <div key={t.preference} className="meal-total-line">
              <span className="muted">{t.preference}</span>
              <span>{t.boxes}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

function GroupSection({ group: g }: { group: GroupOrders }) {
  return (
    <Card
      title={g.groupNumber != null ? <GroupPill groupNumber={g.groupNumber} /> : 'No group'}
      subtitle={`${g.customers.length} customer${g.customers.length === 1 ? '' : 's'} · ${g.boxes} box${g.boxes === 1 ? '' : 'es'} · ${g.totals
        .map((t) => `${t.mealType} ${t.preference} ${t.boxes}`)
        .join(' · ')}`}
    >
      <div className="table-wrap">
        <table className="table data-table">
          <thead>
            <tr>
              <th>Time</th>
              <th>Customer</th>
              <th>Orders</th>
              <th className="num">Boxes</th>
              <th>Phone</th>
              <th>Address</th>
              <th>Notes</th>
            </tr>
          </thead>
          <tbody>
            {g.customers.map((c) => (
              <tr key={c.id}>
                <td className="nowrap">{c.deliveryTime ? formatTime(c.deliveryTime) : '—'}</td>
                <td>
                  <span className="strong nowrap">{c.customerId}</span>
                  <div className="item-sub">{c.customerName}</div>
                </td>
                <td>
                  {c.items.map((i, k) => (
                    <div key={k} className="nowrap">
                      {i.preference} {i.mealType} ×{i.quantity}
                    </div>
                  ))}
                </td>
                <td className="num strong">{c.boxes}</td>
                <td className="nowrap">
                  {c.phoneNumber}
                  {c.alternatePhoneNumber && <div className="item-sub">{c.alternatePhoneNumber}</div>}
                </td>
                <td className="cell-wide">
                  {c.address ?? '—'}
                  {c.pincode && <span className="muted"> — {c.pincode}</span>}
                  {c.googleMapUrl && (
                    <div>
                      <a href={c.googleMapUrl} target="_blank" rel="noopener noreferrer" className="external-link small">
                        Open map <ExternalLink size={12} />
                      </a>
                    </div>
                  )}
                </td>
                <td className="cell-notes">{c.notes.length > 0 ? c.notes.join('; ') : <span className="muted">—</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
