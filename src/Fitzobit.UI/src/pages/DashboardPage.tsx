import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  AlertTriangle,
  ArrowRight,
  CalendarClock,
  CalendarDays,
  History,
  MapPin,
  MapPinOff,
  PauseCircle,
  UserCheck,
  UserMinus,
  UserPlus,
  UserX,
  Users,
  UsersRound,
} from 'lucide-react';
import { api, type DashboardSummary } from '../api';
import { useApi } from '../hooks';
import { formatDateTime, formatNumber } from '../format';
import { Card, EmptyState, ErrorPanel, Loading, PageHeader, StatCard } from '../components/ui';
import BarChart from '../components/BarChart';

const ACTION_LABELS: Record<string, string> = {
  CustomerCreated: 'Added',
  CustomerUpdated: 'Edited',
  CustomerPaused: 'Paused a day',
  CustomerAssigned: 'Assigned',
  CustomerReassigned: 'Moved',
  CustomerUnassigned: 'Removed from group',
  CustomerPackageAdded: 'Package added',
  CustomerPackageUpdated: 'Package edited',
  CustomerPackageDeleted: 'Package deleted',
  CustomerNoteAdded: 'Note added',
  CustomerNoteUpdated: 'Note edited',
  CustomerNoteDeleted: 'Note deleted',
};

export default function DashboardPage() {
  const { data, error, loading, reload } = useApi(api.dashboard);
  const [area, setArea] = useState<'pincode' | 'city'>('pincode');
  const [mix, setMix] = useState<'meal' | 'status'>('meal');

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle="Customers, packages, locations and delivery groups at a glance (live)."
        actions={
          <Link to="/customers/new" className="btn btn-primary">
            <UserPlus size={16} /> Add customer
          </Link>
        }
      />

      {loading && !data && <Loading />}
      {error && <ErrorPanel message={error} onRetry={reload} />}
      {data && <Summary data={data} area={area} setArea={setArea} mix={mix} setMix={setMix} />}
    </>
  );
}

function Summary({ data, area, setArea, mix, setMix }: {
  data: DashboardSummary;
  area: 'pincode' | 'city';
  setArea: (a: 'pincode' | 'city') => void;
  mix: 'meal' | 'status';
  setMix: (m: 'meal' | 'status') => void;
}) {
  const statusLink = (code: string) => {
    const id = data.customersByStatus.find((s) => s.code === code)?.id;
    return id ? `/data?status=${id}` : '/data';
  };
  const frequencies = data.customersByFrequency.filter((f) => f.key !== 'none');

  return (
    <>
      {data.incompleteCustomers > 0 && (
        <div className="alert alert-warning">
          <AlertTriangle size={18} />
          <div>
            <strong>
              {formatNumber(data.incompleteCustomers)} customer{data.incompleteCustomers === 1 ? ' has' : 's have'} incomplete details.
            </strong>{' '}
            They still need a package, a preferred delivery time or a pincode (e.g. customers whose packages were cleared).
          </div>
          <Link to="/data?incomplete=1" className="btn btn-secondary btn-sm">
            Review
          </Link>
        </div>
      )}

      <div className="stat-grid">
        <StatCard
          tone="blue"
          icon={<Users size={20} />}
          label="Total Customers"
          value={formatNumber(data.totalCustomers)}
          hint={`${formatNumber(data.totalPackages)} package${data.totalPackages === 1 ? '' : 's'}`}
          to="/data"
        />
        <StatCard tone="green" icon={<UserCheck size={20} />} label="Active Customers" value={formatNumber(data.activeCustomers)} to={statusLink('ACTIVE')} />
        <StatCard tone="amber" icon={<PauseCircle size={20} />} label="Paused Customers" value={formatNumber(data.pausedCustomers)} to={statusLink('PAUSED')} />
        <StatCard tone="slate" icon={<UserMinus size={20} />} label="Inactive Customers" value={formatNumber(data.inactiveCustomers)} to={statusLink('INACTIVE')} />
        <StatCard tone="teal" icon={<UsersRound size={20} />} label="Delivery Groups" value={formatNumber(data.totalDeliveryGroups)} hint="Active groups" to="/groups" />
        <StatCard
          tone="rose"
          icon={<UserX size={20} />}
          label="Unassigned Customers"
          value={formatNumber(data.unassignedCustomers)}
          hint={`${formatNumber(data.unassignedActiveCustomers)} of them active`}
          to="/data?group=none"
        />
        <StatCard
          tone="violet"
          icon={<MapPin size={20} />}
          label="With Coordinates"
          value={formatNumber(data.withCoordinates)}
          hint={data.approximateCoordinates > 0 ? `${formatNumber(data.approximateCoordinates)} approximate (pincode)` : 'Shown on the map'}
          to="/data?geo=located"
        />
        <StatCard
          tone="indigo"
          icon={<MapPinOff size={20} />}
          label="Without Coordinates"
          value={formatNumber(data.withoutCoordinates)}
          hint="Not on the map yet"
          to="/data?geo=missing"
        />
      </div>

      <div className="stat-grid">
        {frequencies.map((f) => (
          <StatCard
            key={f.key}
            tone="blue"
            icon={<CalendarDays size={20} />}
            label={`${f.label} Packages`}
            value={formatNumber(f.count)}
            to={`/data?frequency=${f.key}`}
          />
        ))}
        <StatCard
          tone="amber"
          icon={<CalendarClock size={20} />}
          label="Packages Ending Soon"
          value={formatNumber(data.endingSoon)}
          hint={`Of active customers, ending within ${data.endingSoonWindowDays} days`}
          to={`${statusLink('ACTIVE')}&sort=endDate`}
        />
      </div>

      {data.totalCustomers === 0 ? (
        <Card>
          <EmptyState
            icon={<Users size={22} />}
            title="No customers yet"
            message="Add your first Fitzobit customer to begin."
            action={
              <Link to="/customers/new" className="btn btn-primary">
                <UserPlus size={16} /> Add customer
              </Link>
            }
          />
        </Card>
      ) : (
        <>
          <div className="grid-2">
            <Card
              title="Customers by delivery group"
              subtitle={`${formatNumber(data.totalCustomers - data.unassignedCustomers)} assigned · ${formatNumber(data.unassignedCustomers)} without a group`}
              actions={
                <Link to="/map" className="link">
                  Classification <ArrowRight size={14} />
                </Link>
              }
            >
              <BarChart label="Customers by delivery group" items={data.customersByGroup} total={data.totalCustomers} linkFor={(i) => `/data?group=${i.key}`} />
            </Card>
            <RecentChanges data={data} />
          </div>
          <div className="grid-2">
            <Card
              title={mix === 'meal' ? 'Packages by meal type' : 'Customers by status'}
              subtitle="Values managed in Settings → Dropdown values"
              actions={
                <div className="tabs tabs-inline" role="tablist">
                  <button className={mix === 'meal' ? 'active' : ''} onClick={() => setMix('meal')}>
                    Meal type
                  </button>
                  <button className={mix === 'status' ? 'active' : ''} onClick={() => setMix('status')}>
                    Status
                  </button>
                </div>
              }
            >
              {mix === 'meal' ? (
                <BarChart
                  label="Packages by meal type"
                  unit="package"
                  items={data.customersByMealType}
                  total={data.totalPackages}
                  linkFor={(i) => `/data?meal=${i.key}`}
                />
              ) : (
                <BarChart
                  label="Customers by status"
                  items={data.customersByStatus.map((s) => ({ key: String(s.id), label: s.name, count: s.count }))}
                  total={data.totalCustomers}
                  linkFor={(i) => `/data?status=${i.key}`}
                />
              )}
            </Card>
            <Card
              title={area === 'pincode' ? 'Customers by pincode' : 'Customers by city'}
              subtitle="Top 8"
              actions={
                <div className="tabs tabs-inline" role="tablist">
                  <button className={area === 'pincode' ? 'active' : ''} onClick={() => setArea('pincode')}>
                    Pincode
                  </button>
                  <button className={area === 'city' ? 'active' : ''} onClick={() => setArea('city')}>
                    City
                  </button>
                </div>
              }
            >
              {(area === 'pincode' ? data.customersByPincode : data.customersByCity).length === 0 ? (
                <p className="muted">No {area === 'pincode' ? 'pincodes' : 'cities'} recorded yet.</p>
              ) : (
                <BarChart
                  label={area === 'pincode' ? 'Customers by pincode' : 'Customers by city'}
                  items={area === 'pincode' ? data.customersByPincode : data.customersByCity}
                  total={data.totalCustomers}
                  linkFor={(i) => (area === 'pincode' ? `/data?pincode=${i.key}` : `/data?address=${encodeURIComponent(i.key)}`)}
                />
              )}
            </Card>
          </div>
        </>
      )}
    </>
  );
}

function RecentChanges({ data }: { data: DashboardSummary }) {
  return (
    <Card title={<><History size={18} /> Recent customer changes</>} subtitle="Customers, packages, pauses, notes and group changes">
      {data.recentChanges.length === 0 ? (
        <p className="muted">No changes recorded yet.</p>
      ) : (
        <ul className="activity-list">
          {data.recentChanges.map((c) => (
            <li key={c.id}>
              <span className="activity-tag">{ACTION_LABELS[c.action] ?? c.action}</span>
              <div className="activity-body">
                {c.customerId && c.customerKey ? (
                  <Link to={`/data?q=${encodeURIComponent(c.customerId)}`} className="link strong">
                    {c.customerId}
                  </Link>
                ) : (
                  <strong>{c.customerId ?? '—'}</strong>
                )}{' '}
                <span className="activity-summary">{changeText(c.summary, c.customerId)}</span>
                <div className="item-sub">
                  {formatDateTime(c.occurredAt)}
                  {c.performedBy ? ` · ${c.performedBy}` : ''}
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

/** The audit summary without its "Customer updated:" prefix and the customer ID (shown separately as a link). */
function changeText(summary: string, customerId: string | null): string {
  let text = summary.replace(/^[A-Za-z ]+:\s*/, '');
  if (customerId && text.startsWith(customerId)) text = text.slice(customerId.length).trim();
  return text.replace(/^[,:·-]\s*/, '');
}
