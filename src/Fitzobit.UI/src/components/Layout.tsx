import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import {
  BookOpen,
  History,
  LayoutDashboard,
  Menu,
  Settings,
  Table2,
  Upload,
  UsersRound,
  X,
} from 'lucide-react';
import { useAuth } from '../auth';
import UserMenu from './UserMenu';

export const navItems = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/upload', label: 'Upload & Create Groups', icon: Upload },
  { to: '/data', label: 'View Data', icon: Table2 },
  { to: '/groups', label: 'Delivery Groups', icon: UsersRound },
  { to: '/imports', label: 'Import History', icon: History },
  { to: '/catalogue', label: 'Catalogue / Plans', icon: BookOpen },
  { to: '/settings', label: 'Settings', icon: Settings },
];

export default function Layout() {
  const [menuOpen, setMenuOpen] = useState(false);
  const location = useLocation();
  const { user } = useAuth();

  useEffect(() => setMenuOpen(false), [location.pathname]);

  const current = navItems.find((n) => (n.end ? location.pathname === n.to : location.pathname.startsWith(n.to)));

  return (
    <div className={`shell ${menuOpen ? 'menu-open' : ''}`}>
      <aside className="sidebar" aria-label="Main navigation">
        <div className="brand">
          <img src="/favicon.svg" alt="" width={32} height={32} />
          <div>
            <span className="brand-name">Fitzobit</span>
            <span className="brand-tag">Delivery Management</span>
          </div>
          <button className="icon-btn sidebar-close" onClick={() => setMenuOpen(false)} aria-label="Close menu">
            <X size={18} />
          </button>
        </div>
        <nav className="nav">
          {navItems.map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              <Icon size={18} strokeWidth={2} />
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-footer">
          <span className="dot" /> Signed in as {user.role}
        </div>
      </aside>
      <div className="scrim" onClick={() => setMenuOpen(false)} />

      <div className="main">
        <header className="topbar">
          <button className="icon-btn menu-btn" onClick={() => setMenuOpen(true)} aria-label="Open menu">
            <Menu size={20} />
          </button>
          <span className="topbar-title">{current?.label ?? 'Fitzobit'}</span>
          <UserMenu />
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
