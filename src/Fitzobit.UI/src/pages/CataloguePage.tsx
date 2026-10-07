import { Fragment, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { AlertTriangle, BookOpen, ChevronDown, ChevronRight, ClipboardList, Layers, Search, Tags } from 'lucide-react';
import { api, type CatalogueItem } from '../api';
import { useApi } from '../hooks';
import { describePeriod, formatDateTime, formatMoney } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader, StatCard, WarningBadge } from '../components/ui';

type Tab = 'items' | 'plans';

export default function CataloguePage() {
  const catalogue = useApi(api.catalogue);
  const plans = useApi(api.plans);
  const [params, setParams] = useSearchParams();
  const [tab, setTab] = useState<Tab>('items');
  const [query, setQuery] = useState('');
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const category = params.get('category') ?? 'All';
  const warningsOnly = params.get('warnings') === '1';

  const setFilter = (key: string, value: string | null) => {
    const next = new URLSearchParams(params);
    if (value === null) next.delete(key);
    else next.set(key, value);
    setParams(next, { replace: true });
  };

  const items = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (catalogue.data?.items ?? []).filter(
      (i) =>
        (category === 'All' || (i.category ?? 'Uncategorised') === category) &&
        (!warningsOnly || i.warnings.length > 0) &&
        (!q || [i.name, i.description, i.category, i.term].some((v) => v?.toLowerCase().includes(q))),
    );
  }, [catalogue.data, category, warningsOnly, query]);

  const toggle = (id: number) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const data = catalogue.data;

  return (
    <>
      <PageHeader
        title="Catalogue / Plans"
        subtitle="Fitzobit's WhatsApp Business catalogue, stored exactly as published. Inconsistencies are flagged, never corrected."
      />

      {catalogue.loading && !data && <Loading />}
      {catalogue.error && <ErrorPanel message={catalogue.error} onRetry={catalogue.reload} />}

      {data && (
        <>
          <div className="stat-grid stat-grid-4">
            <StatCard tone="blue" icon={<BookOpen size={20} />} label="Catalogue Items" value={data.totalItems} />
            <StatCard tone="teal" icon={<Layers size={20} />} label="Pricing Tiers" value={data.totalPricingTiers} />
            <StatCard tone="violet" icon={<Tags size={20} />} label="Categories" value={data.categories.length} />
            <StatCard
              tone="amber"
              icon={<AlertTriangle size={20} />}
              label="Items Needing Review"
              value={data.itemsWithWarnings}
              hint="Source price inconsistencies"
            />
          </div>

          <div className="tabs" role="tablist">
            <button role="tab" aria-selected={tab === 'items'} className={tab === 'items' ? 'active' : ''} onClick={() => setTab('items')}>
              <BookOpen size={16} /> Catalogue items <span className="tab-count">{data.totalItems}</span>
            </button>
            <button role="tab" aria-selected={tab === 'plans'} className={tab === 'plans' ? 'active' : ''} onClick={() => setTab('plans')}>
              <ClipboardList size={16} /> Plans <span className="tab-count">{plans.data?.length ?? '…'}</span>
            </button>
          </div>

          {tab === 'items' && (
            <Card>
              <div className="toolbar">
                <div className="search">
                  <Search size={16} />
                  <input
                    type="search"
                    placeholder="Search items, descriptions…"
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    aria-label="Search catalogue"
                  />
                </div>
                <div className="chips" role="group" aria-label="Filter by category">
                  {['All', ...data.categories.map((c) => c.name)].map((c) => (
                    <button key={c} className={`chip ${category === c ? 'active' : ''}`} onClick={() => setFilter('category', c === 'All' ? null : c)}>
                      {c}
                    </button>
                  ))}
                </div>
                <label className="toggle">
                  <input type="checkbox" checked={warningsOnly} onChange={(e) => setFilter('warnings', e.target.checked ? '1' : null)} />
                  Warnings only
                </label>
              </div>

              {items.length === 0 ? (
                <EmptyState title="No matching items" message="Try a different search or clear the filters." />
              ) : (
                <div className="table-wrap">
                  <table className="table catalogue-table">
                    <thead>
                      <tr>
                        <th aria-label="Expand" />
                        <th>Catalogue item</th>
                        <th>Category</th>
                        <th className="num">Display price</th>
                        <th>Currency</th>
                        <th>Billing period</th>
                        <th className="num">Meals</th>
                        <th>Pricing tiers</th>
                        <th>Availability</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {items.map((item) => (
                        <Fragment key={item.id}>
                          <tr className={`row-click ${item.warnings.length ? 'row-warning' : ''}`} onClick={() => toggle(item.id)}>
                            <td>
                              <button className="icon-btn" aria-label={expanded.has(item.id) ? 'Collapse' : 'Expand'} aria-expanded={expanded.has(item.id)}>
                                {expanded.has(item.id) ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                              </button>
                            </td>
                            <td>
                              <div className="item-name">{item.name}</div>
                              <div className="item-sub">{firstLine(item.description)}</div>
                            </td>
                            <td>{item.category ?? <span className="muted">—</span>}</td>
                            <td className="num strong">{formatMoney(item.displayPrice, item.currency)}</td>
                            <td>{item.currency}</td>
                            <td>{describePeriod(item.billingPeriod, item.durationDays)}</td>
                            <td className="num">{item.mealCount ?? <span className="muted">—</span>}</td>
                            <td>
                              {item.pricingTiers.length === 0 ? (
                                <span className="muted">None listed</span>
                              ) : (
                                <span className="tier-summary">
                                  {item.pricingTiers.length} tier{item.pricingTiers.length === 1 ? '' : 's'}
                                  <small>
                                    {formatMoney(Math.min(...item.pricingTiers.map((t) => t.price)), item.currency)} –{' '}
                                    {formatMoney(Math.max(...item.pricingTiers.map((t) => t.price)), item.currency)}
                                  </small>
                                </span>
                              )}
                            </td>
                            <td>
                              <Badge tone={item.availability?.startsWith('Available') ? 'success' : 'neutral'}>
                                {item.availability?.startsWith('Available') ? 'Available' : item.availability ?? 'Unknown'}
                              </Badge>
                            </td>
                            <td>
                              <WarningBadge count={item.warnings.length} />
                            </td>
                          </tr>
                          {expanded.has(item.id) && (
                            <tr className="detail-row">
                              <td />
                              <td colSpan={9}>
                                <ItemDetail item={item} />
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <p className="footnote">
                Source: {data.source ?? 'unknown'} · Last synchronised {formatDateTime(data.lastUpdatedAt)}
              </p>
            </Card>
          )}

          {tab === 'plans' && (
            <Card title="Plans" subtitle="Plans customers can be assigned to. Created from catalogue items; stored in the database.">
              {plans.loading && !plans.data && <Loading />}
              {plans.error && <ErrorPanel message={plans.error} onRetry={plans.reload} />}
              {plans.data && (
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Plan</th>
                        <th>Code</th>
                        <th>Category</th>
                        <th>Status</th>
                        <th className="num">Customers</th>
                      </tr>
                    </thead>
                    <tbody>
                      {plans.data.map((p) => (
                        <tr key={p.id}>
                          <td>
                            <div className="item-name">{p.name}</div>
                            <div className="item-sub">{p.description}</div>
                          </td>
                          <td>
                            <code className="code">{p.code}</code>
                          </td>
                          <td>{p.category ?? '—'}</td>
                          <td>
                            <Badge tone={p.status === 'Active' ? 'success' : 'neutral'}>{p.status}</Badge>
                          </td>
                          <td className="num">{p.customerCount}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}
        </>
      )}
    </>
  );
}

function firstLine(text: string | null) {
  return text?.split('\n')[0] ?? '';
}

function ItemDetail({ item }: { item: CatalogueItem }) {
  return (
    <div className="item-detail">
      {item.warnings.length > 0 && (
        <div className="alert alert-warning">
          <AlertTriangle size={18} />
          <div>
            {item.warnings.map((w) => (
              <p key={w.code + w.message}>
                <strong>⚠ {w.title}.</strong> {w.message}
              </p>
            ))}
            <p className="muted small">The source values are shown unchanged. Confirm the correct price with Fitzobit before relying on it.</p>
          </div>
        </div>
      )}

      <div className="detail-grid">
        <div>
          <h4>Description (as published)</h4>
          <p className="pre">{item.description ?? '—'}</p>
        </div>
        <div>
          <h4>Pricing tiers</h4>
          {item.pricingTiers.length === 0 ? (
            <p className="muted">No pricing tiers are listed in this item's description.</p>
          ) : (
            <table className="table table-compact">
              <thead>
                <tr>
                  <th>Tier</th>
                  <th className="num">Price</th>
                  <th>Period</th>
                  <th className="num">Meals</th>
                  <th>Notes</th>
                </tr>
              </thead>
              <tbody>
                {item.pricingTiers.map((t) => (
                  <tr key={t.id} className={t.price === item.displayPrice ? 'row-match' : ''}>
                    <td>{t.tierName}</td>
                    <td className="num strong">{formatMoney(t.price, t.currency)}</td>
                    <td>{t.billingPeriod}</td>
                    <td className="num">{t.mealCount ?? '—'}</td>
                    <td className="muted">{t.description ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <dl className="facts">
            <dt>Fitzobit term</dt>
            <dd>{item.term ?? '—'}</dd>
            <dt>Duration (as written)</dt>
            <dd>{item.durationText ?? '—'}</dd>
            <dt>Included</dt>
            <dd>{item.includedItems ?? '—'}</dd>
            <dt>Availability</dt>
            <dd>{item.availability ?? '—'}</dd>
            <dt>Plan code</dt>
            <dd>{item.planCode ? <code className="code">{item.planCode}</code> : '—'}</dd>
            {item.sourceNotes && (
              <>
                <dt>Source notes</dt>
                <dd>{item.sourceNotes}</dd>
              </>
            )}
          </dl>
        </div>
      </div>
    </div>
  );
}
