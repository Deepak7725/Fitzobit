import { Link, useSearchParams } from 'react-router-dom';
import { ArrowRight, CheckCircle2, Database, Download, MapPin, MessageCircle, ShieldCheck, UserRound, UsersRound, XCircle } from 'lucide-react';
import { api, type AppSettings } from '../api';
import { useApi } from '../hooks';
import { useAuth } from '../auth';
import { formatBytes, formatNumber } from '../format';
import { Badge, Card, ErrorPanel, Loading, PageHeader } from '../components/ui';
import UsersTab from './settings/UsersTab';
import DictionariesTab from './settings/DictionariesTab';
import DeliveryAreaCard from './settings/DeliveryAreaCard';
import RenewalSettingsCard from './settings/RenewalSettingsCard';
import AuditTab from './settings/AuditTab';

const TABS = [
  { key: 'general', label: 'General' },
  { key: 'dropdowns', label: 'Dropdown values' },
  { key: 'users', label: 'Users' },
  { key: 'groups', label: 'Delivery Groups' },
  { key: 'whatsapp', label: 'WhatsApp' },
  { key: 'export', label: 'Export Settings' },
  { key: 'audit', label: 'Audit Log' },
] as const;
type TabKey = (typeof TABS)[number]['key'];

export default function SettingsPage() {
  const { isManager } = useAuth();
  const [params, setParams] = useSearchParams();
  const settings = useApi(api.settings);
  const tabs = TABS;
  const requested = params.get('tab') as TabKey | null;
  const tab: TabKey = tabs.some((t) => t.key === requested) ? requested! : 'general';

  const s = settings.data;
  return (
    <>
      <PageHeader
        title="Settings"
        subtitle={
          isManager
            ? 'Application, drop-down values, users and data.'
            : 'Application, drop-down values, users and data. Only a Manager account can change drop-down values, users, the delivery area and renewal reminders.'
        }
      />
      <div className="tabs tabs-scroll" role="tablist">
        {tabs.map((t) => (
          <button key={t.key} role="tab" aria-selected={tab === t.key} className={tab === t.key ? 'active' : ''} onClick={() => setParams({ tab: t.key }, { replace: true })}>
            {t.label}
          </button>
        ))}
      </div>

      {settings.error && <ErrorPanel message={settings.error} onRetry={settings.reload} />}
      {tab === 'users' && <UsersTab />}
      {tab === 'dropdowns' && <DictionariesTab />}
      {tab === 'audit' && <AuditTab />}
      {tab === 'groups' && <GroupsTab settings={s} />}
      {!['users', 'dropdowns', 'audit', 'groups'].includes(tab) && settings.loading && !s && <Loading />}
      {s && tab === 'general' && <GeneralTab settings={s} />}
      {s && tab === 'whatsapp' && <WhatsAppTab settings={s} />}
      {s && tab === 'export' && <ExportTab settings={s} />}
    </>
  );
}

function GeneralTab({ settings }: { settings: AppSettings }) {
  const { user, isManager } = useAuth();
  return (
    <div className="grid-2">
      <div className="stack">
        <DeliveryAreaCard />
        <RenewalSettingsCard />
        <Card title={<><UserRound size={18} /> Signed in</>}>
          <dl className="facts facts-wide">
            <dt>Name</dt>
            <dd>{user.displayName}</dd>
            <dt>User name</dt>
            <dd>{user.userName}</dd>
            <dt>Role</dt>
            <dd>
              <Badge tone={isManager ? 'info' : 'neutral'}>{user.role}</Badge>
            </dd>
          </dl>
          <p className="footnote">Change your password from the menu under your name (top right).</p>
        </Card>
        <Card title={<><ShieldCheck size={18} /> Security</>}>
          <dl className="facts facts-wide">
            <dt>Session length</dt>
            <dd>{settings.security.sessionHours} hours of inactivity</dd>
            <dt>Passwords</dt>
            <dd>At least {settings.security.passwordMinLength} characters, letters and numbers; stored as salted hashes</dd>
            <dt>Lockout</dt>
            <dd>
              {settings.security.maxFailedAttempts} failed sign-ins lock an account for {settings.security.lockoutMinutes} minutes
            </dd>
            <dt>Protection</dt>
            <dd>HttpOnly SameSite cookies, CSRF tokens, security headers, role checks on every API call</dd>
          </dl>
        </Card>
      </div>
      <SystemCard />
    </div>
  );
}

function SystemCard() {
  const system = useApi(api.system);
  const d = system.data;
  return (
    <Card title={<><Database size={18} /> Database</>}>
      {system.error && <ErrorPanel message={system.error} onRetry={system.reload} />}
      {system.loading && !d && <Loading />}
      {d && (
        <dl className="facts facts-wide">
          <dt>Provider</dt>
          <dd>
            <Badge tone="info">{d.databaseProvider}</Badge>
          </dd>
          <dt>Database</dt>
          <dd>
            <code className="code wrap">{d.database}</code>
          </dd>
          <dt>Size</dt>
          <dd>{formatBytes(d.databaseSizeBytes)}</dd>
          <dt>Migrations</dt>
          <dd>
            {d.pendingMigrations.length === 0 ? (
              <Badge tone="success" icon={<CheckCircle2 size={13} />}>
                Up to date ({d.appliedMigrations.length} applied)
              </Badge>
            ) : (
              <Badge tone="warning">{d.pendingMigrations.length} pending</Badge>
            )}
            <ul className="plain-list">
              {d.appliedMigrations.map((m) => (
                <li key={m}>
                  <code className="code">{m}</code>
                </li>
              ))}
            </ul>
          </dd>
          <dt>Tables</dt>
          <dd>
            <div className="tag-cloud">
              {d.tables.map((t) => (
                <span key={t} className="tag">
                  {t}
                </span>
              ))}
            </div>
          </dd>
          <dt>Version</dt>
          <dd>
            {d.applicationName} {d.version}
          </dd>
        </dl>
      )}
    </Card>
  );
}

function GroupsTab({ settings }: { settings: AppSettings | null }) {
  const groups = useApi(api.deliveryGroups);
  const g = groups.data;
  return (
    <div className="grid-2">
      <Card
        title={<><UsersRound size={18} /> Delivery groups</>}
        subtitle="Groups are created by managers; location and delivery group are separate."
        actions={
          <Link to="/groups" className="link">
            Manage groups <ArrowRight size={14} />
          </Link>
        }
      >
        {groups.error && <ErrorPanel message={groups.error} onRetry={groups.reload} />}
        {groups.loading && !g && <Loading />}
        {g && (
          <dl className="facts facts-wide">
            <dt>Groups</dt>
            <dd>
              {g.groups.filter((x) => x.status === 'Active').length} active · {g.groups.filter((x) => x.status !== 'Active').length} inactive
            </dd>
            <dt>Assigned</dt>
            <dd>{formatNumber(g.totalCustomers - g.unassignedCustomers)} customers</dd>
            <dt>Unassigned</dt>
            <dd>{formatNumber(g.unassignedCustomers)} customers</dd>
            <dt>Rules</dt>
            <dd>One active group per customer; every change is kept in the assignment history with a reason.</dd>
          </dl>
        )}
      </Card>
      <Card title={<><MapPin size={18} /> Location recognition</>} subtitle="Used to read localities and cities from customer addresses (appsettings.json).">
        {settings ? (
          <dl className="facts facts-wide">
            <dt>Known cities</dt>
            <dd>
              <div className="tag-cloud">
                {settings.locations.knownCities.map((c) => (
                  <span key={c} className="tag">
                    {c}
                  </span>
                ))}
              </div>
            </dd>
            <dt>City aliases</dt>
            <dd>
              {Object.entries(settings.locations.cityAliases)
                .map(([alias, city]) => `${alias} → ${city}`)
                .join(' · ') || '—'}
            </dd>
            <dt>Ignored words</dt>
            <dd>{settings.locations.regionWords.join(', ') || '—'}</dd>
          </dl>
        ) : (
          <Loading />
        )}
      </Card>
    </div>
  );
}

function WhatsAppTab({ settings }: { settings: AppSettings }) {
  const w = settings.whatsApp;
  return (
    <div className="grid-2">
      <Card title={<><MessageCircle size={18} /> WhatsApp Business</>} subtitle="Official WhatsApp Business (Cloud) API only. The app works normally without it.">
        <div className={`alert ${w.configured ? 'alert-info' : 'alert-neutral'}`}>
          {w.configured ? <CheckCircle2 size={18} /> : <XCircle size={18} />}
          <div>
            <strong>{w.message}</strong>
            {!w.configured && <p>No messages are sent and none are shown as sent.</p>}
          </div>
        </div>
        <dl className="facts facts-wide">
          <dt>API version</dt>
          <dd>{w.apiVersion}</dd>
          <dt>Phone number ID</dt>
          <dd>{w.phoneNumberIdMasked ?? '—'}</dd>
          <dt>Message history</dt>
          <dd>
            {w.messagesRecorded === 0
              ? 'No messages recorded'
              : Object.entries(w.messagesByStatus)
                  .map(([k, v]) => `${v} ${k.toLowerCase()}`)
                  .join(' · ')}
          </dd>
        </dl>
      </Card>
      <Card title="Configuration" subtitle="Set on the server; the access token is a secret and is never stored in files or shown here.">
        <ul className="plain-list config-list">
          <li>
            <code className="code">WhatsApp__PhoneNumberId</code>
          </li>
          <li>
            <code className="code">WhatsApp__BusinessAccountId</code>
          </li>
          <li>
            <code className="code">WhatsApp__AccessToken</code> <span className="muted small">(environment variable or user secrets only)</span>
          </li>
        </ul>
        <h4 className="list-heading drawer-section">Prepared for</h4>
        <div className="tag-cloud">
          {w.plannedFeatures.map((f) => (
            <span key={f} className="tag">
              {f}
            </span>
          ))}
        </div>
      </Card>
    </div>
  );
}

function ExportTab({ settings }: { settings: AppSettings }) {
  return (
    <Card title={<><Download size={18} /> Export settings</>} subtitle="Excel and PDF exports from Customers → Export.">
      <dl className="facts facts-wide">
        <dt>Formats</dt>
        <dd>Excel (.xlsx) to work with the data · PDF (A4) to print or share</dd>
        <dt>Rows</dt>
        <dd>Filtered (every page of the current filters and search) · This page · Selected rows · All customers. Rows are chosen on the server with the table's filters and sort order.</dd>
        <dt>Columns</dt>
        <dd>The columns visible in the table, in the same order (choose Columns in the table to change them), or all columns for a full export.</dd>
        <dt>Maximum rows</dt>
        <dd>
          {formatNumber(settings.export.maxRows)} per Excel export · {formatNumber(settings.export.pdfMaxRows)} per PDF
        </dd>
        <dt>Dates</dt>
        <dd>Shown as “12 Oct 2026” in {settings.export.timeZone}; Excel stores them as real dates</dd>
        <dt>Excel</dt>
        <dd>Formatted header, autofilter, frozen header row; Customer ID, phone and pincode stored as text (no scientific notation); an “Export info” sheet lists the filters</dd>
        <dt>PDF</dt>
        <dd>Heading with the filters, header row repeated on every page, page numbers and generation time; landscape and smaller text only when many columns are chosen</dd>
        <dt>Available columns</dt>
        <dd>
          <div className="tag-cloud">
            {settings.export.columns.map((c) => (
              <span key={c} className="tag">
                {c}
              </span>
            ))}
          </div>
        </dd>
      </dl>
    </Card>
  );
}
