import { Link, Navigate, useParams } from 'react-router-dom';
import { ArrowLeft, Ban, FileSpreadsheet } from 'lucide-react';
import { api } from '../api';
import { useApi } from '../hooks';
import { formatBytes, formatDateTime } from '../format';
import { Badge, Card, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { importSteps, ImportStepper, type StepKey } from './import/shared';
import ValidateStep from './import/ValidateStep';
import ClassifyStep from './import/ClassifyStep';
import GroupsStep from './import/GroupsStep';
import AssignStep from './import/AssignStep';
import ReviewStep from './import/ReviewStep';
import SaveStep from './import/SaveStep';

export default function ImportWizardPage() {
  const params = useParams();
  const batchId = Number(params.batchId);
  const step = (params.step ?? 'validate') as StepKey;
  const detail = useApi((signal) => api.importDetail(batchId, signal), [batchId]);

  if (!Number.isFinite(batchId) || !importSteps.some((s) => s.key === step) || step === 'upload') return <Navigate to="/upload" replace />;

  const d = detail.data;
  const locked = d !== null && d.status !== 'Validated';
  const shownStep: StepKey = d?.status === 'Completed' ? 'save' : step;

  return (
    <>
      <PageHeader
        title="Upload & Create Delivery Groups"
        subtitle="Each step only changes this draft import. Customers and groups are written when you Confirm & Save."
        actions={
          <Link to="/upload" className="btn btn-secondary">
            <ArrowLeft size={16} /> All uploads
          </Link>
        }
      />

      {detail.loading && !d && <Loading />}
      {detail.error && <ErrorPanel message={detail.error} onRetry={detail.reload} />}

      {d && (
        <>
          <Card className="wizard-card">
            <div className="file-banner">
              <FileSpreadsheet size={20} />
              <div>
                <strong>{d.fileName}</strong>
                <small className="muted">
                  {formatBytes(d.fileSizeBytes)} · sheet “{d.sheetName ?? '—'}” · uploaded {formatDateTime(d.uploadedAt)} by {d.uploadedBy ?? '—'}
                  {d.summary && ` · ${d.summary.totalRows.toLocaleString('en-IN')} rows`}
                </small>
              </div>
              <Badge tone={d.status === 'Completed' ? 'success' : d.status === 'Validated' ? 'info' : d.status === 'Failed' ? 'danger' : 'neutral'}>
                {d.status === 'Validated' ? 'Draft (not saved)' : d.status}
              </Badge>
            </div>
            <ImportStepper current={shownStep} batchId={batchId} locked={locked} />
          </Card>

          {d.status === 'Failed' && (
            <ErrorPanel message={d.failureReason ?? 'The file could not be read.'} />
          )}
          {d.status === 'Cancelled' && (
            <div className="alert alert-neutral">
              <Ban size={18} />
              <div>This import was cancelled. Nothing from it was saved.</div>
              <Link className="btn btn-secondary btn-sm" to="/upload">
                Upload a file
              </Link>
            </div>
          )}

          {d.status === 'Validated' && d.failureReason && (
            <ErrorPanel message={d.failureReason} />
          )}

          {(d.status === 'Validated' || d.status === 'Completed') && (
            <>
              {shownStep === 'validate' && <ValidateStep detail={d} onChanged={detail.reload} />}
              {shownStep === 'classify' && <ClassifyStep detail={d} onChanged={detail.reload} />}
              {shownStep === 'groups' && <GroupsStep detail={d} onChanged={detail.reload} />}
              {shownStep === 'assign' && <AssignStep detail={d} onChanged={detail.reload} />}
              {shownStep === 'review' && <ReviewStep detail={d} />}
              {shownStep === 'save' && <SaveStep detail={d} onChanged={detail.reload} />}
            </>
          )}
        </>
      )}
    </>
  );
}
