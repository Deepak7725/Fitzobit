import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  AlertTriangle,
  ArrowRight,
  BookOpen,
  ClipboardList,
  FileSpreadsheet,
  Hash,
  MapPin,
  UserX,
  Users,
  UsersRound,
} from 'lucide-react';
import { api } from '../api';
import { useApi } from '../hooks';
import { formatDateTime, formatNumber } from '../format';
import { Card, EmptyState, ErrorPanel, Loading, PageHeader, StatCard, WarningBadge } from '../components/ui';
import BarChart from '../components/BarChart';

export default function DashboardPage() {
  const { data, error, loading, reload } = useApi(api.dashboard);
  const [area, setArea] = useState<'pincode' | 'city'>('pincode');

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle="Customers, delivery groups and the current Fitzobit catalogue at a glance."
        actions={
          <Link to="/upload" className="btn btn-primary">
            Upload customers <ArrowRight size={16} />
          </Link>
        }
      />

      {loading && !data && <Loading />}
      {error && <ErrorPanel message={error} onRetry={reload} />}

      {data && (
        <>
          {data.catalogueItemsWithWarnings > 0 && (
            <div className="alert alert-warning">
              <AlertTriangle size={18} />
              <div>
                <strong>
                  {data.catalogueItemsWithWarnings} catalogue item{data.catalogueItemsWithWarnings === 1 ? ' has' : 's have'} pricing
                  inconsistencies in the source catalogue.
                </strong>{' '}
                Values are kept exactly as published on WhatsApp. Review them before using these prices.
              </div>
              <Link to="/catalogue?warnings=1" className="btn btn-secondary btn-sm">
                Review
              </Link>
            </div>
          )}

          <div className="stat-grid">
            <StatCard tone="blue" icon={<Users size={20} />} label="Total Customers" value={formatNumber(data.totalCustomers)} />
            <StatCard tone="teal" icon={<UsersRound size={20} />} label="Delivery Groups" value={formatNumber(data.totalDeliveryGroups)} />
            <StatCard tone="violet" icon={<MapPin size={20} />} label="Unique Locations" value={formatNumber(data.uniqueLocations)} />
            <StatCard tone="indigo" icon={<Hash size={20} />} label="Unique Pincodes" value={formatNumber(data.uniquePincodes)} />
            <StatCard tone="green" icon={<ClipboardList size={20} />} label="Active Plans" value={formatNumber(data.activePlans)} />
            <StatCard
              tone="rose"
              icon={<UserX size={20} />}
              label="Unassigned Customers"
              value={formatNumber(data.unassignedCustomers)}
              hint="No active delivery group"
            />
            <StatCard
              tone="slate"
              icon={<FileSpreadsheet size={20} />}
              label="Recent Imports"
              value={formatNumber(data.recentImports)}
              hint={`Last ${data.recentImportWindowDays} days`}
            />
            <StatCard
              tone="amber"
              icon={<BookOpen size={20} />}
              label="Catalogue Items"
              value={formatNumber(data.catalogueItems)}
              hint={`${data.cataloguePricingTiers} pricing tiers`}
            />
          </div>

          {data.totalCustomers === 0 ? (
            <Card>
              <EmptyState
                icon={<Users size={22} />}
                title="No customers yet"
                message="Upload your first Fitzobit customer Excel file to begin."
                action={
                  <Link to="/upload" className="btn btn-primary">
                    Upload customers <ArrowRight size={16} />
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
                    <Link to="/groups" className="link">
                      Delivery groups <ArrowRight size={14} />
                    </Link>
                  }
                >
                  <BarChart
                    label="Customers by delivery group"
                    items={data.customersByGroup}
                    total={data.totalCustomers}
                    linkFor={(i) => `/data?group=${i.key}`}
                  />
                </Card>
                <Card title="Customers by plan" subtitle="Plan mapped during import">
                  <BarChart
                    label="Customers by plan"
                    items={data.customersByPlan}
                    total={data.totalCustomers}
                    linkFor={(i) => (i.key === 'none' ? null : `/data?plan=${i.key}`)}
                  />
                </Card>
              </div>
              <div className="grid-2">
                <Card
                  title={area === 'pincode' ? 'Customers by pincode' : 'Customers by city'}
                  subtitle="Top 8, from the customer locations"
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
                    <p className="muted">No {area === 'pincode' ? 'pincodes' : 'cities'} were detected in the customer locations.</p>
                  ) : (
                    <BarChart
                      label={area === 'pincode' ? 'Customers by pincode' : 'Customers by city'}
                      items={area === 'pincode' ? data.customersByPincode : data.customersByCity}
                      total={data.totalCustomers}
                      linkFor={(i) => (area === 'pincode' ? `/data?pincode=${i.key}` : `/data?location=${encodeURIComponent(i.key)}`)}
                    />
                  )}
                </Card>
                <Card
                  title="Recent imports"
                  subtitle="Customers imported by the latest uploads"
                  actions={
                    <Link to="/imports" className="link">
                      View history <ArrowRight size={14} />
                    </Link>
                  }
                >
                  {data.latestImports.length === 0 ? (
                    <EmptyState icon={<FileSpreadsheet size={22} />} title="No imports yet" message="Customer Excel uploads will appear here." />
                  ) : (
                    <>
                      <BarChart
                        label="Customers imported by recent uploads"
                        items={data.latestImports.map((b) => ({
                          key: String(b.id),
                          label: `${b.fileName} · ${formatDateTime(b.uploadedAt)}${b.status === 'Completed' ? '' : ` (${b.status === 'Validated' ? 'draft' : b.status.toLowerCase()})`}`,
                          count: b.status === 'Completed' ? b.importedCount : 0,
                        }))}
                        total={data.totalCustomers}
                        linkFor={(i) => `/imports/${i.key}`}
                      />
                      <p className="footnote">Drafts, cancelled and failed uploads save no customers.</p>
                    </>
                  )}
                </Card>
              </div>
            </>
          )}

          <Card
            title="Catalogue by category"
            subtitle="From the Fitzobit WhatsApp Business catalogue"
            actions={
              <Link to="/catalogue" className="link">
                Open catalogue <ArrowRight size={14} />
              </Link>
            }
          >
            {data.catalogueCategories.length === 0 ? (
              <EmptyState title="No catalogue loaded" message="The catalogue workbook was not found when the app started." />
            ) : (
              <ul className="category-list">
                {data.catalogueCategories.map((c) => (
                  <li key={c.name}>
                    <Link to={`/catalogue?category=${encodeURIComponent(c.name)}`}>
                      <span className="category-name">{c.name}</span>
                      <span className="muted">
                        {c.itemCount} item{c.itemCount === 1 ? '' : 's'}
                      </span>
                      <WarningBadge count={c.warningCount} label="Needs review" />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </>
      )}
    </>
  );
}
