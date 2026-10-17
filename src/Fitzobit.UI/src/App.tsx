import { Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import DashboardPage from './pages/DashboardPage';
import ViewDataPage from './pages/ViewDataPage';
import AddCustomerPage from './pages/AddCustomerPage';
import DeliveryGroupsPage from './pages/DeliveryGroupsPage';
import DeliveryMapPage from './pages/DeliveryMapPage';
import SettingsPage from './pages/SettingsPage';
import NotFoundPage from './pages/NotFoundPage';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="customers/new" element={<AddCustomerPage />} />
        <Route path="data" element={<ViewDataPage />} />
        <Route path="groups" element={<DeliveryGroupsPage />} />
        <Route path="map" element={<DeliveryMapPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
