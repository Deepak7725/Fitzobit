import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ArrowRight,
  CheckCircle2,
  Database,
  Download,
  FileSpreadsheet,
  MapPin,
  MessageCircle,
  RefreshCw,
  ShieldCheck,
  Upload,
  UserRound,
  UsersRound,
  XCircle,
} from 'lucide-react';
import { api, type AppSettings, type CatalogueSeedResult } from '../api';
import { useApi } from '../hooks';
import { useAuth } from '../auth';
import { formatBytes, formatNumber } from '../format';
import { Badge, Card, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { ConfirmDialog, useToast } from '../components/feedback';
import UsersTab from './settings/UsersTab';
import PlansTab from './settings/PlansTab';
import AuditTab from './settings/AuditTab';

const TABS = [
  { key: 'general', label: 'General' },
  { key: 'users', label: 'Users', admin: true },
  { key: 'plans', label: 'Plans', admin: true },
  { key: 'catalogue', label: 'Catalogue' },
  { key: 'groups', label: 'Delivery Groups' },
  { key: 'whatsapp', label: 'WhatsApp' },
  { key: 'import', label: 'Import Settings' },
  { key: 'export', label: 'Export Settings' },
  { key: 'audit', label: 'Audit Log', admin: true },
] as const;
type TabKey = (typeof TABS)[number]['key'];

export default function SettingsPage() {
  const { isAdmin } = useAuth();
  const [params, setParams] = useSearchParams();
  const settings = useApi(api.settings);
  const tabs = TABS.filter((t) => !('admin' in t) || isAdmin);
  const requested = params.get('tab') as TabKey | null;
  const tab: TabKey = tabs.some((t) => t.key === requested) ? requested! : 'general';

  const s = settings.data;
  return (
    <>
      <PageHeader
        title="Settings"
        subtitle={isAdmin ? 'Application, users, plans and data sources.' : 'Application and data-source information. Administrative settings are managed by an administrator.'}
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
      {tab === 'plans' && <PlansTab />}
      {tab === 'audit' && <AuditTab />}
      {tab === 'groups' && <GroupsTab settings={s} />}
      {!['users', 'plans', 'audit', 'groups'].includes(tab) && settings.loading && !s && <Loading />}
      {s && tab === 'general' && <GeneralTab settings={s} />}
      {s && tab === 'catalogue' && <CatalogueTab settings={s} onChanged={settings.reload} />}
      {s && tab === 'whatsapp' && <WhatsAppTab settings={s} />}
      {s && tab === 'import' && <ImportTab settings={s} />}
      {s && tab === 'export' && <ExportTab settings={s} />}
    </>
  );
}

function GeneralTab({ settings }: { settings: AppSettings }) {
  const { user, isAdmin } = useAuth();
  return (
    <div className="grid-2">
      <div className="stack">
        <Card title={<><UserRound size={18} /> Signed in</>}>
          <dl className="facts facts-wide">
            <dt>Name</dt>
            <dd>{user.displayName}</dd>
            <dt>User name</dt>
            <dd>{user.userName}</dd>
            <dt>Role</dt>
            <dd>
              <Badge tone={isAdmin ? 'info' : 'neutral'}>{user.role}</Badge>
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
      {isAdmin ? <SystemCard /> : (
        <Card title={<><Database size={18} /> Database</>}>
          <p className="muted">Database and migration details are visible to administrators.</p>
        </Card>
      )}
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
          <dt>Database file</dt>
          <dd>
            <code className="code wrap">{d.databaseFile ?? '—'}</code>
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

function CatalogueTab({ settings, onChanged }: { settings: AppSettings; onChanged: () => void }) {
  const { isAdmin } = useAuth();
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CatalogueSeedResult | null>(null);

  const resync = async () => {
    setBusy(true);
    try {
      const r = await api.resyncCatalogue();
      setResult(r);
      toast(r.skipped ? `Re-sync skipped: ${r.skipReason}` : 'Catalogue re-synced from the workbook.', r.skipped ? 'error' : 'success');
      onChanged();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Re-sync failed.', 'error');
    } finally {
      setBusy(false);
      setConfirming(false);
    }
  };

  return (
    <Card
      title={<><FileSpreadsheet size={18} /> Catalogue source</>}
      subtitle="Plans and prices come from the Fitzobit WhatsApp catalogue workbook. Values are copied as published; inconsistencies are flagged, never corrected."
      actions={
        isAdmin && (
          <button className="btn btn-secondary btn-sm" onClick={() => setConfirming(true)} disabled={busy || !settings.catalogue.exists}>
            <RefreshCw size={14} /> {busy ? 'Re-syncing…' : 'Re-sync now'}
          </button>
        )
      }
    >
      <dl className="facts facts-wide">
        <dt>Workbook</dt>
        <dd>
          <code className="code">{settings.catalogue.fileName}</code>
        </dd>
        <dt>Status</dt>
        <dd>
          {settings.catalogue.exists ? (
            <Badge tone="success" icon={<CheckCircle2 size={13} />}>
              Found
            </Badge>
          ) : (
            <Badge tone="danger" icon={<XCircle size={13} />}>
              Not found
            </Badge>
          )}
        </dd>
        <dt>Sync</dt>
        <dd>{settings.catalogue.syncOnStartup ? 'Synced every time the app starts (idempotent)' : 'Automatic sync is turned off'}</dd>
      </dl>
      {result && !result.skipped && (
        <div className="info-box" style={{ marginTop: 16 }}>
          <h4>Last re-sync</h4>
          <ul>
            <li>
              Items: {result.itemsCreated} created, {result.itemsUpdated} updated, {result.itemsUnchanged} unchanged
            </li>
            <li>
              Pricing tiers: {result.tiersCreated} created, {result.tiersUpdated} updated, {result.tiersRemoved} removed
            </li>
            <li>Plans created: {result.plansCreated}</li>
            {result.itemsNotInSource > 0 && <li>{result.itemsNotInSource} item(s) no longer in the workbook were kept</li>}
          </ul>
        </div>
      )}
      <p className="footnote">
        <Link to="/catalogue" className="link">
          Open Catalogue / Plans <ArrowRight size={13} />
        </Link>
      </p>
      {confirming && (
        <ConfirmDialog
          title="Re-sync the catalogue?"
          messages={[
            `Items and pricing tiers are updated from ${settings.catalogue.fileName}.`,
            'Customers, plans already assigned and delivery groups are not changed.',
          ]}
          confirmLabel="Re-sync"
          busy={busy}
          onConfirm={resync}
          onCancel={() => setConfirming(false)}
        />
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
      <Card title={<><MapPin size={18} /> Location recognition</>} subtitle="Used to read pincodes, localities and cities from customer locations (appsettings.json).">
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

function ImportTab({ settings }: { settings: AppSettings }) {
  return (
    <Card title={<><Upload size={18} /> Import settings</>} subtitle="Limits and checks applied to every customer Excel upload.">
      <dl className="facts facts-wide">
        <dt>File type</dt>
        <dd>Excel workbook ({settings.import.acceptedFileType}) only; files with macros are rejected; formulas are not evaluated</dd>
        <dt>Maximum size</dt>
        <dd>{formatBytes(settings.import.maxFileSizeBytes)}</dd>
        <dt>Maximum rows</dt>
        <dd>{formatNumber(settings.import.maxRows)}</dd>
        <dt>Required columns</dt>
        <dd>CustomerID, CustomerName, PhoneNumber, Location, Plan</dd>
        <dt>Validation</dt>
        <dd>CustomerID present and unique; name; 10-digit Indian mobile number; location with pincode; plan mapped to the catalogue</dd>
        <dt>Existing customers</dt>
        <dd>Never overwritten by an import; only their delivery group can change, and only when you choose to</dd>
      </dl>
      <p className="footnote">
        <Link to="/upload" className="link">
          Upload customers <ArrowRight size={13} />
        </Link>
      </p>
    </Card>
  );
}

function ExportTab({ settings }: { settings: AppSettings }) {
  return (
    <Card title={<><Download size={18} /> Export settings</>} subtitle="Excel exports from View & Manage Data.">
      <dl className="facts facts-wide">
        <dt>Scopes</dt>
        <dd>All customers · Filtered (exactly the current View Data filters) · Selected rows</dd>
        <dt>Maximum rows</dt>
        <dd>{formatNumber(settings.export.maxRows)} per export</dd>
        <dt>Dates</dt>
        <dd>Real Excel dates in {settings.export.timeZone}</dd>
        <dt>Columns</dt>
        <dd>
          <div className="tag-cloud">
            {settings.export.columns.map((c) => (
              <span key={c} className="tag">
                {c}
              </span>
            ))}
          </div>
        </dd>
        <dt>Format</dt>
        <dd>Formatted header, autofilter, frozen header row, phone and pincode stored as text (no scientific notation), plus an “Export info” sheet</dd>
      </dl>
    </Card>
  );
}
