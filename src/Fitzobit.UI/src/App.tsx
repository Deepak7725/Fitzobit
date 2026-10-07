import { Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import DashboardPage from './pages/DashboardPage';
import CataloguePage from './pages/CataloguePage';
import UploadPage from './pages/UploadPage';
import ImportWizardPage from './pages/ImportWizardPage';
import ViewDataPage from './pages/ViewDataPage';
import DeliveryGroupsPage from './pages/DeliveryGroupsPage';
import ImportHistoryPage from './pages/ImportHistoryPage';
import ImportDetailPage from './pages/ImportDetailPage';
import SettingsPage from './pages/SettingsPage';
import NotFoundPage from './pages/NotFoundPage';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="upload" element={<UploadPage />} />
        <Route path="upload/:batchId/:step" element={<ImportWizardPage />} />
        <Route path="data" element={<ViewDataPage />} />
        <Route path="groups" element={<DeliveryGroupsPage />} />
        <Route path="imports" element={<ImportHistoryPage />} />
        <Route path="imports/:id" element={<ImportDetailPage />} />
        <Route path="catalogue" element={<CataloguePage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
