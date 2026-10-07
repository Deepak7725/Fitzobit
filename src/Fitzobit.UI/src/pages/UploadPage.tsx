import { useRef, useState, type DragEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AlertCircle, CheckCircle2, Download, FileSpreadsheet, Info, UploadCloud, X } from 'lucide-react';
import { api, templateUrl, type UploadResult } from '../api';
import { useApi } from '../hooks';
import { formatBytes, formatDateTime } from '../format';
import { Card, PageHeader } from '../components/ui';
import { useToast } from '../components/feedback';
import { ImportStepper, stepPath } from './import/shared';

const MaxBytes = 10 * 1024 * 1024;
const requiredColumns = ['CustomerID', 'CustomerName', 'PhoneNumber', 'Location', 'Plan'];

export default function UploadPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [rejected, setRejected] = useState<UploadResult | null>(null);
  const imports = useApi(api.imports);
  const inProgress = (imports.data ?? []).filter((b) => b.status === 'Validated');

  const choose = (f: File | undefined) => {
    setRejected(null);
    setProblem(null);
    if (!f) return;
    if (!f.name.toLowerCase().endsWith('.xlsx')) return setProblem('Only Excel workbooks (.xlsx) can be uploaded.');
    if (f.size > MaxBytes) return setProblem(`The file is ${formatBytes(f.size)}; the limit is 10 MB.`);
    if (f.size === 0) return setProblem('The file is empty.');
    setFile(f);
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    choose(e.dataTransfer.files?.[0]);
  };

  const upload = async () => {
    if (!file) return;
    setUploading(true);
    setProblem(null);
    try {
      const result = await api.upload(file);
      if (result.status === 'Validated') {
        toast(`${file.name} uploaded and validated`);
        navigate(stepPath(result.batchId, 'validate'));
      } else {
        setRejected(result);
        imports.reload();
      }
    } catch (e) {
      setProblem(e instanceof Error ? e.message : 'Upload failed');
    } finally {
      setUploading(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Upload & Create Delivery Groups"
        subtitle="Upload customer data, validate it, then decide the delivery groups and assign customers before saving."
        actions={
          <a className="btn btn-secondary" href={templateUrl} download>
            <Download size={16} /> Download Excel template
          </a>
        }
      />

      <Card>
        <ImportStepper current="upload" />

        <div className="upload-grid">
          <div>
            <div
              className={`dropzone ${dragging ? 'dragging' : ''} ${file ? 'has-file' : ''}`}
              onDragOver={(e) => {
                e.preventDefault();
                setDragging(true);
              }}
              onDragLeave={() => setDragging(false)}
              onDrop={onDrop}
            >
              <FileSpreadsheet size={44} strokeWidth={1.5} className="dropzone-icon" />
              <h3>Drag & drop your Excel file here</h3>
              <p className="muted">
                or{' '}
                <button className="link-button" onClick={() => inputRef.current?.click()}>
                  browse your computer
                </button>
              </p>
              <p className="muted small">Supported format: .xlsx · Max size 10 MB · Up to 20,000 rows</p>
              <input
                ref={inputRef}
                type="file"
                accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                hidden
                onChange={(e) => {
                  choose(e.target.files?.[0]);
                  e.target.value = '';
                }}
              />
              {file && (
                <div className="file-chip">
                  <FileSpreadsheet size={18} />
                  <span>
                    <strong>{file.name}</strong>
                    <small className="muted">{formatBytes(file.size)}</small>
                  </span>
                  <button className="icon-btn" onClick={() => setFile(null)} aria-label="Remove file" disabled={uploading}>
                    <X size={16} />
                  </button>
                </div>
              )}
              <button className="btn btn-primary btn-lg" disabled={!file || uploading} onClick={upload}>
                <UploadCloud size={18} /> {uploading ? 'Uploading & validating…' : 'Upload & validate'}
              </button>
            </div>

            {problem && (
              <div className="error-panel">
                <AlertCircle size={18} />
                <div>{problem}</div>
              </div>
            )}

            {rejected && (
              <div className="error-panel error-block">
                <AlertCircle size={18} />
                <div>
                  <strong>The file could not be imported.</strong>
                  <ul>
                    {rejected.errors.map((e) => (
                      <li key={e}>{e}</li>
                    ))}
                  </ul>
                  <span>Fix the file and upload it again. The template shows the expected layout.</span>
                </div>
              </div>
            )}
          </div>

          <div className="side-panel">
            <h3>Required columns</h3>
            <ul className="check-list">
              {requiredColumns.map((c) => (
                <li key={c}>
                  <CheckCircle2 size={16} /> {c}
                </li>
              ))}
            </ul>
            <div className="info-box">
              <h4>
                <Info size={16} /> Good to know
              </h4>
              <ul>
                <li>Header names are matched without regard to case or spacing.</li>
                <li>Phone numbers must be 10-digit Indian mobiles; keep the column as Text.</li>
                <li>Include the 6-digit pincode in the location so customers can be grouped.</li>
                <li>Plans are matched to the Fitzobit catalogue; anything unclear is flagged for you to decide.</li>
                <li>Rows with errors are listed. Fix and re-upload, or continue with the valid rows.</li>
                <li>Nothing is saved until you press Confirm &amp; Save.</li>
              </ul>
            </div>
          </div>
        </div>
      </Card>

      {inProgress.length > 0 && (
        <Card title="Imports in progress" subtitle="Validated files that have not been saved yet">
          <ul className="import-list">
            {inProgress.map((b) => (
              <li key={b.id}>
                <FileSpreadsheet size={18} className="muted" />
                <div>
                  <strong>{b.fileName}</strong>
                  <small className="muted">
                    Uploaded {formatDateTime(b.uploadedAt)} · {b.totalRows.toLocaleString('en-IN')} rows · {b.invalidRows} with errors
                  </small>
                </div>
                <Link className="btn btn-secondary btn-sm" to={stepPath(b.id, 'validate')}>
                  Continue
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  );
}
